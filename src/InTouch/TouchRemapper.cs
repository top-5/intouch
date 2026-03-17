using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WacomMTDN;

namespace InTouch
{
    internal enum TouchEventKind { Down, Move, Up, Tap }

    internal enum TouchMode
    {
        MoveTapDrag,   // cursor + tap + drag (default)
        MoveTap,       // cursor + tap only (no drag)
        MoveOnly       // cursor only (no tap, no drag)
    }

    internal readonly record struct TouchEventData(
        int ScreenX, int ScreenY,
        TouchEventKind Kind,
        int FingerID,
        float RawX, float RawY);

    /// <summary>
    /// Reads Wacom Multi-Touch finger data via HWND-based registration and
    /// remaps touch coordinates to absolute screen cursor position.
    /// 
    /// Uses WacomMTRegisterFingerReadHWND which posts WM_FINGERDATA (0x6205)
    /// messages to a specified window handle. Unlike the HitRect callback
    /// approach, HWND registration delivers data regardless of which window
    /// has foreground focus — the Wacom driver posts messages directly to
    /// the registered HWND's message queue.
    /// 
    /// Coordinate systems (per Wacom API docs):
    ///   Opaque tablets (Intuos Pro): finger.X/Y are normalized 0.0–1.0
    ///     → we map to virtual screen via GetSystemMetrics (called per-frame;
    ///       the Win32 call is trivially cheap — ~50ns — so no caching needed,
    ///       and this automatically handles resolution/DPI/monitor changes).
    ///   Integrated displays (Cintiq): finger.X/Y are screen pixel coordinates
    ///     → we pass through directly to SetCursorPos.
    /// </summary>
    internal sealed class TouchRemapper : IDisposable
    {
        private CWacomMTConfig? _config;
        private CWacomMTFingerClient? _fingerClient;
        private WacomMTAttachCallback? _attachCallback;  // prevent GC
        private WacomMTDetachCallback? _detachCallback;  // prevent GC
        private int _deviceId = -1;
        private bool _isDisplayTablet;                   // true if integrated (Cintiq)
        private bool _disposed;
        private IntPtr _hwnd;                            // HWND for touch data delivery

        // ── Touch state machine ──────────────────────────────────────────
        //   Idle → Pending (on DOWN)
        //   Pending → Dragging (movement > DragThreshold)
        //   Pending → Idle (UP within TapMaxMs & TapMaxDist → click)
        //   Dragging → Idle (UP → release left button)
        private enum TouchState { Idle, Pending, Dragging }
        private TouchState _state = TouchState.Idle;
        private int _activeFingerId = -1;
        private float _downRawX, _downRawY;
        private long _downTick;

        // Thresholds (normalized 0–1 coordinates for opaque tablets)
        public float TapMaxDist { get; set; } = 0.025f;   // max movement to still count as tap (~5mm)
        public int TapMaxMs { get; set; } = 500;           // max duration for a tap (ms)
        public float DragThreshold { get; set; } = 0.04f;  // movement to enter drag mode (~9mm)

        public TouchMode Mode { get; set; } = TouchMode.MoveTapDrag;

        /// <summary>When false, callbacks do nothing (passthrough).</summary>
        public bool Enabled { get; set; } = true;

        public bool IsConnected => _deviceId >= 0;

        public event Action? StateChanged;

        /// <summary>Fired from the Wacom callback thread for every finger event.</summary>
        public event Action<TouchEventData>? TouchDataReceived;

        public void Start(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _config = new CWacomMTConfig();
            var err = CWacomMTInterface.WacomMTInitialize(WacomMTConstants.WACOM_MULTI_TOUCH_API_VERSION);
            if (err != WacomMTError.WMTErrorSuccess)
            {
                Log.Error($"WacomMTInitialize failed: {err}");
                return;
            }
            Log.Info($"WacomMT initialized, HWND=0x{hwnd:X}");

            _attachCallback = OnDeviceAttach;
            _detachCallback = OnDeviceDetach;

            // The attach callback fires immediately for each already-connected
            // device, so no need to poll WacomMTGetAttachedDeviceIDs separately.
            CWacomMTInterface.WacomMTRegisterAttachCallback(_attachCallback, IntPtr.Zero);
            CWacomMTInterface.WacomMTRegisterDetachCallback(_detachCallback, IntPtr.Zero);
        }

        private void OnDeviceAttach(WacomMTCapability cap, IntPtr userData)
        {
            Log.Info($"Device attached: ID={cap.DeviceID}, Type={cap.Type}, " +
                $"Physical={cap.PhysicalSizeX:F1}×{cap.PhysicalSizeY:F1}mm, " +
                $"Logical={cap.LogicalWidth}×{cap.LogicalHeight}");
            _config?.AddDevice(cap);
            if (_deviceId < 0)
            {
                _isDisplayTablet = cap.Type == WacomMTDeviceType.WMTDeviceTypeIntegrated;
                RegisterDevice((int)cap.DeviceID);
            }
        }

        private void OnDeviceDetach(int deviceId, IntPtr userData)
        {
            if (deviceId == _deviceId)
            {
                UnregisterDevice();
                _config?.RemoveDevice(deviceId);
            }
        }

        private void RegisterDevice(int deviceId)
        {
            try
            {
                // Use HWND-based registration: the Wacom driver posts WM_FINGERDATA
                // (0x6205) messages to the specified window handle. Unlike HitRect
                // callback registration, HWND delivery works regardless of which
                // window has foreground focus.
                //
                // Observer mode: the Wacom driver still generates its own mouse
                // events from touch (LEFTDOWN on contact, LEFTUP on release).
                // Our InjectClick cancels the driver's press before doing a
                // clean click sequence.
                _fingerClient = new CWacomMTFingerClient(WacomMTProcessingMode.WMTProcessingModeObserver);
                _fingerClient.RegisterHWNDClient(deviceId, _hwnd, bufferDepth_I: 1);

                _deviceId = deviceId;
                Log.Info($"Registered touch device {deviceId} via HWND=0x{_hwnd:X} (display={_isDisplayTablet}, mode=Observer)");
                StateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("RegisterDevice failed", ex);
            }
        }

        private void UnregisterDevice()
        {
            try
            {
                _fingerClient?.UnregisterHWNDClient();
            }
            catch { }
            _fingerClient = null;
            _deviceId = -1;
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Process a WM_FINGERDATA message received by the registered HWND.
        /// Called from the ghost overlay's WndProc on the UI thread.
        /// 
        /// State machine:
        ///   Idle → Pending:  finger DOWN — move cursor, wait to classify
        ///   Pending → Dragging: finger moved past DragThreshold — hold LEFTDOWN
        ///   Pending → Idle:  finger UP within TapMaxMs + TapMaxDist — click
        ///   Dragging → Idle: finger UP — release LEFTUP
        /// </summary>
        public void ProcessFingerData(IntPtr lParam)
        {
            if (!Enabled)
                return;

            try
            {
                var collection = CMemUtils.PtrToStructure<WacomMTFingerCollection>(lParam);

                for (uint i = 0; i < collection.FingerCount; i++)
                {
                    var finger = collection.GetFingerByIndex(i);
                    if (!finger.Confidence)
                        continue;

                    var (sx, sy) = ToScreen(finger.X, finger.Y, _isDisplayTablet);
                    var (absX, absY) = ToAbsolute(sx, sy);
                    bool isActive = finger.FingerID == _activeFingerId;

                    switch (finger.TouchState)
                    {
                        case WacomMTFingerState.WMTFingerStateDown:
                            if (_state == TouchState.Idle)
                            {
                                // First finger down — claim it, but don't move cursor yet.
                                // Cursor moves only when we commit (tap click or drag start)
                                // to avoid micro-moves that apps interpret as drag.
                                //
                                // The Wacom driver (Observer mode) already injected a
                                // LEFTDOWN into the input queue before this callback.
                                // Cancel it immediately so apps don't start a selection.
                                CancelDriverPress();
                                _state = TouchState.Pending;
                                _activeFingerId = finger.FingerID;
                                _downRawX = finger.X;
                                _downRawY = finger.Y;
                                _downTick = Environment.TickCount64;
                                Log.Info($"DOWN fid={finger.FingerID} raw=({finger.X:F4},{finger.Y:F4}) scr=({sx},{sy}) → Pending");
                            }
                            TouchDataReceived?.Invoke(new TouchEventData(sx, sy, TouchEventKind.Down, finger.FingerID, finger.X, finger.Y));
                            break;

                        case WacomMTFingerState.WMTFingerStateHold:
                            if (_state == TouchState.Idle)
                            {
                                // Finger already in contact but not tracked (e.g. after the
                                // active finger released, or SDK missed the Down event,
                                // or Confidence toggled false→true mid-touch). Adopt it.
                                CancelDriverPress();
                                _state = TouchState.Pending;
                                _activeFingerId = finger.FingerID;
                                _downRawX = finger.X;
                                _downRawY = finger.Y;
                                _downTick = Environment.TickCount64;
                                Log.Info($"ADOPT fid={finger.FingerID} raw=({finger.X:F4},{finger.Y:F4}) scr=({sx},{sy}) → Pending");
                            }
                            else if (_state == TouchState.Pending && isActive)
                            {
                                float dist = RawDistance(finger.X, finger.Y, _downRawX, _downRawY);
                                // Only enter drag when movement is clearly intentional
                                // (well beyond tap jitter). DragThreshold is large enough
                                // that normal tap jitter never triggers it.
                                if (Mode == TouchMode.MoveTapDrag && dist >= DragThreshold)
                                {
                                    var (startSx, startSy) = ToScreen(_downRawX, _downRawY, _isDisplayTablet);
                                    var (startAbsX, startAbsY) = ToAbsolute(startSx, startSy);
                                    InjectButtonDown(startAbsX, startAbsY);
                                    InjectMove(absX, absY);
                                    _state = TouchState.Dragging;
                                    Log.Info($"DRAG fid={finger.FingerID} dist={dist:F4} → Dragging");
                                }
                                // else: movement below drag threshold, don't move cursor
                            }
                            else if (_state == TouchState.Dragging && isActive)
                            {
                                InjectMove(absX, absY);
                            }
                            TouchDataReceived?.Invoke(new TouchEventData(sx, sy, TouchEventKind.Move, finger.FingerID, finger.X, finger.Y));
                            break;

                        case WacomMTFingerState.WMTFingerStateUp:
                            if (isActive)
                            {
                                long elapsed = Environment.TickCount64 - _downTick;

                                if (_state == TouchState.Pending)
                                {
                                    float dist = RawDistance(finger.X, finger.Y, _downRawX, _downRawY);
                                    // Tap = small movement. Time is secondary — a slow
                                    // tap with little movement is still a tap.
                                    if (Mode != TouchMode.MoveOnly && dist < TapMaxDist)
                                    {
                                        // Click at the DOWN position for a clean tap —
                                        // no intermediate moves, just teleport + click.
                                        var (downSx, downSy) = ToScreen(_downRawX, _downRawY, _isDisplayTablet);
                                        var (downAbsX, downAbsY) = ToAbsolute(downSx, downSy);
                                        InjectClick(downAbsX, downAbsY);
                                        Log.Info($"TAP  fid={finger.FingerID} elapsed={elapsed}ms dist={dist:F4}");
                                        TouchDataReceived?.Invoke(new TouchEventData(sx, sy, TouchEventKind.Tap, finger.FingerID, finger.X, finger.Y));
                                    }
                                    else
                                    {
                                        Log.Info($"UP   fid={finger.FingerID} elapsed={elapsed}ms dist={dist:F4} (no tap)");
                                    }
                                }
                                else if (_state == TouchState.Dragging)
                                {
                                    InjectButtonUp(absX, absY);
                                    Log.Info($"DRAGEND fid={finger.FingerID} elapsed={Environment.TickCount64 - _downTick}ms");
                                }

                                _state = TouchState.Idle;
                                _activeFingerId = -1;
                            }
                            TouchDataReceived?.Invoke(new TouchEventData(sx, sy, TouchEventKind.Up, finger.FingerID, finger.X, finger.Y));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ProcessFingerData error", ex);
            }
        }

        private static float RawDistance(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Convert raw Wacom coordinates to screen pixel position.
        /// Opaque tablets: normalized 0–1 → virtual screen.
        /// Integrated displays: already in pixels, pass through.
        /// </summary>
        private static (int x, int y) ToScreen(float rawX, float rawY, bool isDisplayTablet)
        {
            if (isDisplayTablet)
                return ((int)rawX, (int)rawY);

            int vLeft  = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int vTop   = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int vWidth  = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int vHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            return (vLeft + (int)(rawX * vWidth), vTop + (int)(rawY * vHeight));
        }

        /// <summary>
        /// Convert screen pixel position to SendInput absolute coordinates (0–65535).
        /// </summary>
        private static (int absX, int absY) ToAbsolute(int screenX, int screenY)
        {
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            return ((int)((screenX * 65535L) / w), (int)((screenY * 65535L) / h));
        }

        /// <summary>
        /// Move cursor via SendInput with MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE.
        /// Unlike SetCursorPos, this goes through the input queue and works
        /// regardless of which window has foreground focus.
        /// </summary>
        private static uint InjectMove(int absX, int absY)
        {
            var input = new NativeMethods.INPUT[1];
            input[0].type = NativeMethods.INPUT_MOUSE;
            input[0].mi.dx = absX;
            input[0].mi.dy = absY;
            input[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
                                | NativeMethods.MOUSEEVENTF_MOVE;
            return NativeMethods.SendInput(1, input, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        private static void InjectButtonDown(int absX, int absY)
        {
            var input = new NativeMethods.INPUT[1];
            input[0].type = NativeMethods.INPUT_MOUSE;
            input[0].mi.dx = absX;
            input[0].mi.dy = absY;
            input[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
                                | NativeMethods.MOUSEEVENTF_MOVE
                                | NativeMethods.MOUSEEVENTF_LEFTDOWN;
            NativeMethods.SendInput(1, input, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        private static void InjectButtonUp(int absX, int absY)
        {
            var input = new NativeMethods.INPUT[1];
            input[0].type = NativeMethods.INPUT_MOUSE;
            input[0].mi.dx = absX;
            input[0].mi.dy = absY;
            input[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
                                | NativeMethods.MOUSEEVENTF_MOVE
                                | NativeMethods.MOUSEEVENTF_LEFTUP;
            NativeMethods.SendInput(1, input, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        /// <summary>
        /// Send a LEFTUP to cancel the Wacom driver's LEFTDOWN.
        /// In Observer mode the driver injects its own mouse press on touch-down;
        /// we must cancel it immediately so apps don't interpret it as a drag.
        /// Harmless if the button is already up.
        /// </summary>
        private static void CancelDriverPress()
        {
            var input = new NativeMethods.INPUT[1];
            input[0].type = NativeMethods.INPUT_MOUSE;
            input[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_LEFTUP;
            NativeMethods.SendInput(1, input, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        private static void InjectClick(int absX, int absY)
        {
            int size = Marshal.SizeOf<NativeMethods.INPUT>();

            // Cancel any driver-injected LEFTDOWN that may still be held.
            var cancel = new NativeMethods.INPUT[1];
            cancel[0].type = NativeMethods.INPUT_MOUSE;
            cancel[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_LEFTUP;
            NativeMethods.SendInput(1, cancel, size);

            // LEFTDOWN + LEFTUP both at the EXACT same absolute position,
            // in one atomic SendInput call. No separate MOVE step — the
            // position is baked into each button event so there's zero
            // chance of coordinate mismatch.
            var click = new NativeMethods.INPUT[2];
            click[0].type = NativeMethods.INPUT_MOUSE;
            click[0].mi.dx = absX;
            click[0].mi.dy = absY;
            click[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
                                | NativeMethods.MOUSEEVENTF_MOVE
                                | NativeMethods.MOUSEEVENTF_LEFTDOWN;
            click[1].type = NativeMethods.INPUT_MOUSE;
            click[1].mi.dx = absX;
            click[1].mi.dy = absY;
            click[1].mi.dwFlags = NativeMethods.MOUSEEVENTF_ABSOLUTE
                                | NativeMethods.MOUSEEVENTF_MOVE
                                | NativeMethods.MOUSEEVENTF_LEFTUP;
            NativeMethods.SendInput(2, click, size);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnregisterDevice();
            try { _config?.Quit(); } catch { }
        }
    }
}

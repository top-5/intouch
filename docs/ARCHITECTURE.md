# InTouch — Architecture

## Goal

Remap Wacom Intuos Pro M **touch** input so that touching the tablet surface moves the
Windows cursor to the corresponding **absolute screen position** (instead of the default
relative trackpad behaviour). Tap = click, drag = drag, and it works no matter which
window has focus. A system-tray app with a Ctrl+Alt+T hotkey controls the feature.

---

## High-Level Design

```
┌──────────────────────────┐
│   Wacom Multi-Touch API  │   WacomMT.dll (ships with Wacom driver)
│   (WacomMTDN C# wrapper) │   HWND-registered: posts WM_FINGERDATA (0x6205)
└────────────┬─────────────┘
             │  WacomMTFingerCollection per frame
             │  finger.X/Y ∈ [0,1]  (opaque tablet)
             ▼
┌──────────────────────────┐
│  GhostOverlay            │   1×1 invisible layered window
│  (WndProc receives       │   WS_EX_LAYERED | WS_EX_TRANSPARENT
│   WM_FINGERDATA)         │   Always exists regardless of focus
└────────────┬─────────────┘
             │
             ▼
┌──────────────────────────┐
│  TouchRemapper            │   Core logic: coordinate mapping + state machine
│                           │
│  Multi-finger processing: │   All confident fingers → TouchDataReceived event
│    finger 0 (active):     │   State machine: Idle → Pending → Dragging
│      Pending + quick up   │     → InjectClick (SendInput LEFTDOWN+LEFTUP)
│      Pending + movement   │     → InjectButtonDown → InjectMove (drag)
│    other fingers:          │   Visualization only (no cursor control)
│                           │
│  Coordinate mapping:      │   Opaque:  rawXY × VirtualScreen → screen px
│    per-frame              │   Display: rawXY pass-through
│    GetSystemMetrics       │   Then → SendInput MOUSEEVENTF_ABSOLUTE
└────────────┬─────────────┘
             │ TouchDataReceived event
             ▼
┌──────────────────────────┐
│  TrayApp (WinForms)       │   ApplicationContext, no visible main window
│  • NotifyIcon (T icon)    │   Green=ON, Orange=waiting, Grey=OFF
│  • Ctrl+Alt+T toggle      │   MessageWindow receives WM_HOTKEY
│  • Settings... menu       │   Opens fullscreen SettingsForm
│  • Touch ripple            │   Optional RipplePopup on each Down event
│  • Device detection        │   WMI (VID_056A) + WacomDeviceDatabase
└──────────────────────────┘
```

---

## Technology Choices

| Concern | Choice | Why |
|---------|--------|-----|
| Runtime | **.NET 8 / WinForms** (`net8.0-windows`) | Lightweight tray app, WacomMTDN wrapper is .NET, ships self-contained |
| Touch API | **WacomMT.dll via WacomMTDN** | Provides normalised (0–1) finger coordinates; HWND registration works regardless of focus |
| Cursor control | **SendInput** | `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE` (0–65535 coords) — works through input queue regardless of foreground window |
| Hotkey | **RegisterHotKey** | Global hotkey via hidden message-only window; `Ctrl+Alt+T` |
| Packaging | **Single-file publish** | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` |

### Why C# instead of TypeScript/Tauri?

The Wacom Multi-Touch SDK ships a **native C DLL** (`WacomMT.dll`). The existing
`WacomMTDN` wrapper provides ready-made P/Invoke declarations, data structures, and
helper classes. Calling `WacomMT.dll` from Node/Electron would require building a
native addon (N-API) with complex struct marshalling — significantly more effort for
zero benefit, since the app is a tiny background process with no web UI.

---

## Key Components

### 1. `WacomMTDN/` (linked from submodule)

Source files linked via `<Compile Include="..\..\external\..." />` from the
`wacom-device-kit-windows` submodule — not copied. Provides:

- **`CWacomMTInterface`** — P/Invoke bindings to `WacomMT.dll`
- **`CWacomMTConfig`** — device enumeration and capability query
- **`CWacomMTFingerClient`** — HWND-based touch registration
- **`CMemUtils`** — safe unmanaged memory marshalling

### 2. `GhostOverlay.cs` — Touch Data Receiver

A tiny 1×1 transparent, click-through, topmost window:

- `WS_EX_LAYERED` (1/255 opacity — invisible but "exists" for the Wacom driver)
- `WS_EX_TRANSPARENT` (all mouse/touch input passes through)
- `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (no taskbar, never steals focus)
- `WndProc` catches `WM_FINGERDATA` (0x6205) → calls `TouchRemapper.ProcessFingerData(lParam)`

This is the key to **background operation**: the Wacom driver posts finger data as window
messages to the registered HWND. Unlike HitRect callback registration, HWND delivery works
regardless of which window has foreground focus.

### 3. `TouchRemapper.cs` — Core Logic

**Initialization:**
```
WacomMTInitialize(API_VERSION)
RegisterAttachCallback → on device attach:
  CWacomMTFingerClient(WMTProcessingModeObserver)
  RegisterHWNDClient(deviceId, ghostHwnd, bufferDepth: 1)
```

`bufferDepth: 1` is critical — higher values cause the SDK to queue multiple frames,
leading to input lag when the UI thread falls behind on processing.

**Multi-finger processing:**
All confident fingers in each `WacomMTFingerCollection` are processed. Every finger fires
`TouchDataReceived` for visualization. But only the **first finger down** (when state is
Idle) claims the cursor state machine.

**Three-state machine (first finger only):**

```
                  ┌─────────┐
                  │  Idle   │
                  └────┬────┘
                       │ finger DOWN → InjectMove, record position + time
                       ▼
                  ┌─────────┐
           ┌──────│ Pending │──────┐
           │      └─────────┘      │
           │  movement ≥            │  finger UP within
           │  DragThreshold         │  TapMaxMs & TapMaxDist
           ▼                        ▼
      ┌──────────┐            ┌──────────┐
      │ Dragging │            │   Tap    │
      │          │            │ (inject  │
      │ LEFTDOWN │            │  click)  │
      │ held     │            └────┬─────┘
      └────┬─────┘                 │
           │ finger UP →           │
           │ LEFTUP                │
           └───────┬───────────────┘
                   ▼
              ┌─────────┐
              │  Idle   │
              └─────────┘
```

**Configurable thresholds** (public properties, adjustable from Settings UI):
- `TapMaxMs` — max duration for a tap gesture (default: 300ms)
- `TapMaxDist` — max normalized movement to still count as tap (default: 0.015)
- `DragThreshold` — movement to enter drag mode (default: 0.008)

**Coordinate mapping:**

| Tablet type | Raw coordinates | Mapping |
|-------------|----------------|---------|
| Opaque (Intuos Pro) | Normalized 0.0–1.0 | `VirtualScreen.Left + rawX × VirtualScreen.Width` |
| Integrated (Cintiq) | Screen pixels | Pass-through |

`GetSystemMetrics` is called per-frame (~50ns) — no caching needed, automatically handles
resolution/DPI/monitor changes.

Cursor movement uses `SendInput(MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE)` with coordinates
normalized to 0–65535. This goes through the system input queue and works regardless of which
window has foreground focus.

### 4. `TrayApp.cs` — Application Shell

`ApplicationContext` subclass (no visible main window):

- **NotifyIcon** — runtime-generated coloured circle icon with "T" letter:
  - Green = active (touch → cursor)
  - Orange = enabled but no tablet connected
  - Grey = disabled
- **ContextMenuStrip** — "Status (Ctrl+Alt+T)" (informational), "Settings...", "Quit"
- **Double-click** tray icon toggles on/off
- **Touch ripple** — optional `RipplePopup` (per-pixel-alpha layered window, 400ms
  expanding/fading circle) on each finger Down event. Toggled from Settings.
- **Device detection** — background WMI query (`Win32_PnPEntity` for `VID_056A`) matched
  against `WacomDeviceDatabase` (static PID → device info dictionary covering Intuos Pro,
  Cintiq Pro, Wacom One, Movink, etc.)
- **Graceful shutdown** — `SystemEvents.SessionEnding` + `Application.ApplicationExit`
  ensure `WacomMTQuit` runs so touch isn't left in suppressed mode

### 5. `MessageWindow.cs` — Hotkey Receiver

Hidden message-only window (`HWND_MESSAGE` parent):
- Registers `Ctrl+Alt+T` via `RegisterHotKey` with `MOD_NOREPEAT`
- `WndProc` catches `WM_HOTKEY` → calls `Toggle()` callback
- Properly unregisters on dispose

### 6. `SettingsForm.cs` — Fullscreen Diagnostics & Configuration

A frameless, maximized, topmost `Form` that serves as both diagnostic canvas and settings UI:

**Canvas (CanvasPanel inner class):**
- `DoubleBuffered` panel, `Dock = Fill`, dark background
- Touch marks stored as `TouchMark` records with screen position, raw position, tick, kind, finger ID
- 40fps timer drives repaints (25ms interval). No per-event `Invalidate()` — prevents 133Hz+ input from flooding the paint queue
- **Paint order** (bottom to top): log text → grid → crosshair → move trails → down/tap/up marks
- **Move trails**: grouped by finger ID, rendered as connected `DrawLine` segments (2.5px) with per-segment alpha fade. Stroke breaks on >200ms time gaps between points.
- **Per-finger colors**: cyan (0), magenta (1), green (2), orange (3), purple (4); cycles for higher IDs
- **Head dot**: 10px filled circle on the newest point of each active finger trail
- **Log entries**: muted text at bottom (DOWN/UP/TAP events with finger ID, raw coords, screen coords), drawn first so trails paint on top. Fade over 10 seconds.
- **Max 400 marks** retained; older get purged

**Floating buttons** (top-left of canvas):
- Settings, Log Folder, ✕ Close
- Explicitly sized via `TextRenderer.MeasureText` (not `AutoSize`) to ensure `.Right` is correct before positioning the next button

**Settings overlay** (centered panel, toggled by Settings button or Escape):
- Tap max duration slider (50–800ms)
- Drag threshold slider (0.1–5.0%, mapped to 0.001–0.050 normalized)
- Show touch ripple checkbox
- Info line (device name, ON/OFF status, log file path)
- Changes apply immediately to `TouchRemapper` properties

### 7. `NativeMethods.cs` — Win32 P/Invoke

All native interop declarations:
- `SetCursorPos`, `SendInput` (with `INPUT`/`MOUSEINPUT` structs)
- `RegisterHotKey` / `UnregisterHotKey`
- `GetSystemMetrics` (virtual screen bounds)
- `GetForegroundWindow`, `GetWindowThreadProcessId`
- `UpdateLayeredWindow`, `CreateCompatibleDC`, `SelectObject`, `DeleteDC`, `DeleteObject` (for ripple popup and ghost overlay per-pixel-alpha rendering)

### 8. `RipplePopup` (in `TouchRipple.cs`)

Per-pixel-alpha layered window showing a 400ms expanding/fading circle at touch position:
- `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`
- 80×80px bitmap rendered via GDI `UpdateLayeredWindow` at 60fps (16ms timer)
- Ease-out quadratic animation: expanding radius (10→32px) + fading alpha
- Static `s_active` list prevents GC while animating
- Toggled on/off from Settings; gates on `_showRipple` in TrayApp

### 9. Supporting Files

- **`Log.cs`** — Thread-safe file logger to `%APPDATA%\InTouch\intouch.log`, 1MB rotation, never throws
- **`DeviceIdentifier.cs`** — WMI `Win32_PnPEntity` query for `VID_056A` devices, cross-referenced against database
- **`WacomDeviceDatabase.cs`** — Static dictionary of ~25 Wacom devices by USB PID (Intuos Pro gen 1/2, Cintiq Pro 13/16/24/32, Cintiq 13HD Touch, DTH-1152, Wacom One, Movink). Includes size estimation from physical dimensions.

---

## Project Layout

```
intouch/
├── docs/
│   └── ARCHITECTURE.md              ← this file
├── external/                         ← git submodules (5 Wacom SDKs)
│   ├── sdk-for-multi-display/
│   ├── signature-sdk-js/
│   ├── wacom-device-kit-windows/     ← WacomMTDN source linked from here
│   ├── wacom-device-kit-web/
│   └── sdk-for-devices-win-classic/
├── src/
│   └── InTouch/                      ← Main tray application (WinExe)
│       ├── InTouch.csproj            ← net8.0-windows, WinForms, links WacomMTDN
│       ├── Program.cs                ← Entry point (STAThread, HighDpi, Run TrayApp)
│       ├── TrayApp.cs                ← ApplicationContext: tray icon, menu, hotkey, device detect
│       ├── TouchRemapper.cs          ← Core: WacomMT HWND registration, state machine, SendInput
│       ├── GhostOverlay.cs           ← 1×1 invisible HWND for WM_FINGERDATA reception
│       ├── SettingsForm.cs           ← Fullscreen dark canvas + settings overlay
│       ├── TouchRipple.cs            ← RipplePopup: per-pixel-alpha expanding ring animation
│       ├── NativeMethods.cs          ← Win32 P/Invoke (SendInput, SetCursorPos, hotkey, GDI)
│       ├── MessageWindow.cs          ← Hidden NativeWindow for WM_HOTKEY (Ctrl+Alt+T)
│       ├── DeviceIdentifier.cs       ← WMI USB VID/PID detection
│       ├── WacomDeviceDatabase.cs    ← Static PID→device info dictionary
│       └── Log.cs                    ← Thread-safe file logger (%APPDATA%\InTouch)
├── src/
│   └── InTouch.DeviceInfo/           ← CLI diagnostic tool
│       └── Program.cs                ← WMI + Wintab + WacomMT capability dump
├── tests/
│   └── InTouch.Tests/                ← 19 xUnit tests
│       ├── WacomMTFixture.cs         ← Shared fixture (process-global WacomMT singleton)
│       ├── WacomDriverTests.cs       ← WacomMT API verification tests
│       └── TouchToggleTests.cs       ← Toggle and state management tests
├── .github/
│   └── agents/
│       └── intouch-coder.agent.md    ← VS Code agent customization
├── .gitmodules
└── README.md
```

---

## Build & Run

```bash
cd src/InTouch
dotnet build
dotnet run
```

Or publish as single-file:

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

**Prerequisites:** Wacom tablet driver installed (provides `WacomMT.dll`).

---

## Processing Modes

| Mode | Behaviour | Use |
|------|-----------|-----|
| `Observer` (1) | Data goes to our HWND AND to OS | **Current** — we move cursor via SendInput while OS also processes touch |
| `Consumer` (0) | Data goes **only** to our callback; OS does NOT process the touch | Alternative if OS touch interference is a problem |
| `PassThrough` (2) | Data goes only to OS | Not used |

Currently using **Observer** mode with HWND registration. The cursor is driven by
`SendInput(MOUSEEVENTF_ABSOLUTE)` which overrides any OS-level touch-to-cursor mapping.

---

## Performance Considerations

- **bufferDepth: 1** — The SDK `RegisterHWNDClient` accepts a buffer depth parameter.
  Higher values cause the driver to queue multiple finger data frames. When the UI thread
  falls behind (e.g., during GC or heavy painting), queued frames create a "draining"
  lag effect. Buffer depth 1 ensures only the latest frame is delivered.

- **No per-event Invalidate** — The Wacom Intuos Pro reports touch at ~133Hz. Calling
  `Invalidate()` on every touch event would flood the WinForms paint queue. Instead, a
  25ms timer (40fps) drives canvas repaints, and `AddEvent()` only starts the timer.

- **Max 400 marks** — Touch trail data is capped to prevent linearly growing paint cost.
  Older marks are pruned by both count and age (4s fade timeout).

- **Single Pen per finger** — Move trail rendering reuses one `Pen` object per finger,
  updating its `Color` property per segment, rather than allocating a new Pen per line.

---

## Future Enhancements

- Per-monitor mapping (map tablet regions to specific monitors)
- Configurable hotkey via settings file
- Pressure → scroll-speed mapping (press harder = scroll faster)
- Two-finger gestures (pinch-to-zoom passthrough)
- Tablet ExpressKey integration via Wintab API (map a physical button to toggle)
- NSIS installer / GitHub Actions CI
- Settings persistence to JSON file

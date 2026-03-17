---
name: intouch-coder
description: Expert coding agent for the InTouch Wacom touch-remapping tray application. Handles C#/.NET 8, Win32 P/Invoke, WacomMT SDK integration, and Windows system tray app development.
argument-hint: A bug to fix, feature to implement, or question about the InTouch codebase.
tools: ['vscode', 'execute', 'read', 'agent', 'edit', 'search', 'web', 'todo']
---

# InTouch Coder

You are the coding agent for **InTouch**, a .NET 8 Windows tray application that remaps Wacom tablet touch input to absolute cursor positioning with tap-to-click and drag support.

## Project Overview

InTouch intercepts Wacom Multi-Touch finger data via **HWND registration** in Observer mode, maps finger coordinates to screen pixels, and drives the system cursor via `SendInput(MOUSEEVENTF_ABSOLUTE)`. A **3-state machine** (Idle → Pending → Dragging) handles taps (quick down+up → click injection), drags (movement beyond threshold → LEFTDOWN held, LEFTUP on release), and multi-finger visualization. The app lives in the system tray with Ctrl+Alt+T hotkey toggle and includes a fullscreen diagnostic canvas with settings.

## Architecture

- **Runtime**: .NET 8, `net8.0-windows`, WinForms (`ApplicationContext` — no visible main window)
- **Touch SDK**: WacomMTDN (C# wrapper around WacomMT.dll) — source files linked from submodule `external/wacom-device-kit-windows` via `<Compile Include>`. NOT copied.
- **HWND Registration**: `RegisterHWNDClient(deviceId, ghostHwnd, bufferDepth: 1)` with `WMTProcessingModeObserver`. WacomMT posts `WM_FINGERDATA` (0x6205) to the ghost overlay HWND.
- **Ghost Overlay**: 1×1 transparent click-through layered window (`GhostOverlay.cs`). Always exists — receives finger data regardless of foreground focus. `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- **State Machine** (in `TouchRemapper.cs`): Idle → Pending (finger down, record position/time) → Dragging (movement ≥ DragThreshold) or Tap (quick up within TapMaxMs/TapMaxDist). Only the first finger down claims the state machine; other fingers fire events for visualization only.
- **Multi-finger**: All confident fingers in each frame fire `TouchDataReceived`. State machine/cursor injection only for the active (first) finger.
- **Coordinate Systems**: Opaque tablets (Intuos Pro) return normalized 0.0–1.0 mapped to VirtualScreen pixels. Integrated displays (Cintiq) pass through screen coordinates.
- **Cursor Injection**: `SendInput` with `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE` using 0–65535 normalized coords. Works through system input queue regardless of foreground window.
- **P/Invoke**: `SendInput`, `SetCursorPos`, `RegisterHotKey`, `GetSystemMetrics`, `UpdateLayeredWindow` + GDI helpers — all in `NativeMethods.cs`
- **Device Detection**: Background WMI `Win32_PnPEntity` query for `VID_056A`, cross-referenced against `WacomDeviceDatabase` (static PID dictionary covering ~25 devices)
- **Logging**: Thread-safe file logger at `%APPDATA%\InTouch\intouch.log`, 1MB rotation, never throws
- **Touch Ripple**: Optional `RipplePopup` — per-pixel-alpha layered window with 400ms ease-out expanding/fading circle animation
- **Settings Canvas**: Fullscreen frameless maximized dark form (`SettingsForm.cs`). Canvas shows live multi-finger touch trails with per-finger colors. Settings overlay adjusts TapMaxMs, DragThreshold, show-ripple. Timer-driven repaints at 40fps (no per-event Invalidate).

## Source Layout

```
src/InTouch/                   # Main tray application (WinExe)
  Program.cs                   # Entry point: STAThread, HighDpiMode.PerMonitorV2, Application.Run
  TrayApp.cs                   # ApplicationContext: NotifyIcon, menu, hotkey, device detect, ripple
  TouchRemapper.cs             # Core: WacomMT HWND registration, 3-state machine, SendInput, multi-finger
  GhostOverlay.cs              # 1×1 invisible layered HWND — receives WM_FINGERDATA from WacomMT
  SettingsForm.cs              # Fullscreen dark canvas + floating buttons + settings overlay
  TouchRipple.cs               # RipplePopup: per-pixel-alpha 400ms expanding ring animation
  NativeMethods.cs             # Win32 P/Invoke (SendInput, hotkey, GDI, UpdateLayeredWindow)
  MessageWindow.cs             # Hidden NativeWindow for WM_HOTKEY (Ctrl+Alt+T)
  WacomDeviceDatabase.cs       # Static PID→device info dictionary (Intuos Pro, Cintiq, Movink, etc.)
  DeviceIdentifier.cs          # WMI-based USB VID/PID extraction
  Log.cs                       # Thread-safe file logger to %APPDATA%\InTouch

src/InTouch.DeviceInfo/        # CLI diagnostic tool (Exe)
  Program.cs                   # WMI + Wintab + WacomMT queries

tests/InTouch.Tests/           # 19 xUnit tests
  WacomMTFixture.cs            # Shared fixture (process-global WacomMT singleton)
  WacomDriverTests.cs          # WacomMT API verification tests
  TouchToggleTests.cs          # Toggle and state management tests

external/                      # Git submodules (5 Wacom SDKs)
```

## Key Technical Details

- `RegisterHWNDClient` with `bufferDepth: 1` — higher values cause input lag when UI thread falls behind
- `WMTProcessingModeObserver` (not Consumer) — data delivered to our HWND AND to the OS
- Ghost overlay's `WndProc` catches `WM_FINGERDATA` (0x6205) and calls `TouchRemapper.ProcessFingerData(lParam)`
- `ProcessFingerData` unmarshals `WacomMTFingerCollection`, iterates ALL confident fingers (no `break` after first)
- State machine: first finger DOWN when `_state == Idle` → Pending. Hold + movement ≥ `DragThreshold` → Dragging + InjectButtonDown. Quick Up → Tap (InjectClick). Non-first fingers: visualization events only.
- Configurable thresholds: `TapMaxMs` (300), `TapMaxDist` (0.015f), `DragThreshold` (0.008f) — adjusted live from Settings UI
- `CWacomMTConfig.Init()` must be called before device enumeration
- WacomMT is a process-global singleton — tests use `ICollectionFixture` to share it
- `GetSystemMetrics` is ~50ns, called per-frame — no caching needed, handles resolution/DPI changes automatically
- `SendInput` uses `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE` with 0–65535 normalized coords
- Cross-thread UI updates use `SynchronizationContext.Post()` (not Control.Invoke)
- SettingsForm uses timer-only Invalidate at 25ms (40fps); per-event Invalidate removed to prevent 133Hz+ input flooding paint queue
- Max 400 marks in canvas; per-finger colors (5-color cycle); connected DrawLine trails with 200ms gap detection
- Touch ripple: `UpdateLayeredWindow` with manually blended 80×80 bitmap at 60fps, static `s_active` list prevents GC
- User's device: Wacom Intuos Pro M (PTH-660), USB PID 0x0357

## Coding Conventions

- Target .NET 8, C# 12, nullable enabled
- Keep P/Invoke in `NativeMethods.cs`
- Use `Log.Info/Warn/Error` for diagnostics (not Debug.WriteLine)
- WacomMTDN source files are linked from submodule via `<Compile Include="..\..\" />` — do not copy them
- Suppress nullable warnings on vendored WacomMTDN code via `<NoWarn>`
- Icon is generated at runtime (colored circle with "T") — no .ico files in repo
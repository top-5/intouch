# InTouch

**Absolute touch-to-cursor remapper for Wacom Intuos Pro (and other Wacom touch tablets).**

## The Story

It all started with me buying an Intuos Pro M — PTH-660 — for $19. My son looked at it
and said: *"You know, dad, it would be much cooler if you can use it as a regular touch
screen, not as mouse input. Then you can play osu! and other tap-based games on it easily
without needing an extra driver."*

And he was right. Normally the Intuos Pro touch surface acts like a trackpad — finger
movement is relative, like dragging on a laptop. But the tablet *knows* exactly where your
finger is in absolute coordinates. So if you tap the bottom-left corner of the tablet, the
cursor should jump to the bottom-left corner of the screen — like a touchscreen.

No Wacom driver setting does this. So this app was written.

## What It Does

InTouch intercepts raw touch coordinates from the Wacom Multi-Touch API and drives the
Windows cursor to the **absolute screen position** matching where you touched. Tap = click.
Drag = drag. Two fingers show up in different colors in the diagnostic view. It sits quietly
in your system tray until you need it.

## Features

- **Absolute mapping** — touch bottom-left of tablet → cursor goes to bottom-left of screen
- **Tap-to-click** — quick tap-and-lift injects a left mouse click via `SendInput`
- **Drag support** — hold and move past a threshold → left button held, cursor follows
- **Multi-touch** — tracks all fingers; first finger controls the cursor, additional fingers are visualized
- **Multi-monitor** — maps across the full virtual screen automatically
- **System tray** — green (active), orange (waiting for tablet), grey (disabled)
- **Global hotkey** — `Ctrl+Alt+T` toggles the remapper on/off
- **Touch ripple** — optional expanding ring animation at each touch point
- **Fullscreen diagnostics** — dark canvas showing real-time touch trails, per-finger colors, event log, and live settings adjustment
- **Background operation** — works regardless of which window has focus (HWND-based touch registration via a ghost overlay window)

## Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build)
- **Wacom tablet driver** installed (provides `WacomMT.dll`)

## Build & Run

```bash
cd src/InTouch
dotnet run
```

Or publish a single-file executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The resulting binary is in `bin/Release/net8.0-windows/win-x64/publish/InTouch.exe`.

## Usage

1. Run `InTouch.exe` — a green **T** icon appears in the system tray
2. Touch the Intuos Pro surface — cursor jumps to the corresponding screen position
3. Quick tap — sends a left click
4. Slide finger across → drag (left button held while moving)
5. **Ctrl+Alt+T** — toggle on/off
6. Right-click tray icon → **Settings...** to open the fullscreen diagnostic canvas
7. Right-click tray icon → **Quit** to exit

## Settings / Diagnostics Screen

Opening **Settings...** from the tray launches a fullscreen dark canvas:

- **Touch canvas** — 1:1 screen mapping. Touch marks appear at the exact screen pixel your finger is on. Smooth connected trails per finger with fade-out. Each finger gets a distinct color (cyan, magenta, green, orange, purple).
- **Event log** — DOWN / UP / TAP events shown as muted text at the bottom, drawn underneath the touch trails.
- **Floating buttons** — Settings, Log Folder, and Close in the top-left corner.
- **Settings overlay** — centered panel with sliders for tap duration (50–800ms), drag threshold (0.1–5.0%), and a ripple toggle. Changes apply instantly.
- **Keyboard** — Escape closes the screen (or dismisses the settings overlay first).

## How It Works

The app uses the **WacomMTDN** wrapper (vendored from `external/wacom-device-kit-windows`)
to call `WacomMT.dll`. A 1×1 invisible **ghost overlay** window receives `WM_FINGERDATA`
(0x6205) messages via HWND-based registration, which works regardless of foreground focus.

Touch coordinates from opaque tablets (Intuos Pro) arrive as normalized 0.0–1.0 values.
These are mapped to virtual screen pixels using `GetSystemMetrics` (called per-frame — the
Win32 call is ~50ns so no caching is needed, and it automatically handles resolution/DPI/monitor
changes). Cursor movement and clicks are injected via `SendInput` with `MOUSEEVENTF_ABSOLUTE`.

A three-state machine classifies each touch:
- **Idle → Pending** on finger down (cursor moves, waiting to see if it's a tap or drag)
- **Pending → Tap** on finger up within configurable time & distance thresholds
- **Pending → Dragging** when movement exceeds drag threshold (left button held)
- **Dragging → Idle** on finger up (left button released)

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full technical design.

## Testing

```bash
dotnet test
```

19 xUnit tests covering WacomMT API integration, device detection, toggle behavior, and
state management. Tests use a shared `WacomMTFixture` since the WacomMT API is a
process-global singleton.

## License

Application code: MIT. Wacom SDK samples are included under their original license
(see each submodule's LICENSE file).

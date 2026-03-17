using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InTouch
{
    /// <summary>
    /// System-tray application context. No visible window.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly TouchRemapper _remapper;
        private readonly MessageWindow _msgWindow;
        private readonly GhostOverlay _ghost;
        private readonly ToolStripMenuItem _stateItem;
        private readonly SynchronizationContext? _syncCtx;
        private string _deviceName = "Detecting...";
        private bool _showRipple = true;

        public TrayApp()
        {
            _remapper = new TouchRemapper();
            _remapper.StateChanged += OnRemapperStateChanged;

            // State item (disabled — informational only, shows device + status)
            _stateItem = new ToolStripMenuItem("Detecting...") { Enabled = false };

            var settingsItem = new ToolStripMenuItem("Settings...", null, OnSettings);
            var quitItem = new ToolStripMenuItem("Quit", null, OnExit);

            var menu = new ContextMenuStrip();
            menu.Items.Add(_stateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(quitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = CreateIcon(Color.LimeGreen),
                Text = "InTouch — Detecting...",
                Visible = true,
                ContextMenuStrip = menu
            };
            _trayIcon.DoubleClick += OnToggle;

            _msgWindow = new MessageWindow(Toggle);

            // Ghost overlay: transparent 1×1 window that receives WM_FINGERDATA
            // messages from the Wacom driver regardless of foreground focus.
            _ghost = new GhostOverlay();
            _ghost.SetRemapper(_remapper);
            _ghost.Show();

            // Graceful shutdown: ensure WacomMTQuit runs on logoff/shutdown/close
            // so touch isn't left in consumer (suppressed) mode.
            SystemEvents.SessionEnding += OnSessionEnding;
            Application.ApplicationExit += OnApplicationExit;

            _remapper.TouchDataReceived += OnTouchData;
            _remapper.Start(_ghost.Handle);

            // Identify device in background (WMI query is fast but off-UI-thread is cleaner)
            _syncCtx = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(_ => DetectDevice());
        }

        private void DetectDevice()
        {
            _deviceName = DeviceIdentifier.GetDeviceSummary();
            // Marshal back to the UI thread; use SynchronizationContext
            // since the ContextMenuStrip handle may not exist yet.
            _syncCtx?.Post(_ => UpdateIcon(), null);
        }

        private void OnRemapperStateChanged()
        {
            UpdateIcon();
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            _remapper.Dispose();
        }

        private void OnApplicationExit(object? sender, EventArgs e)
        {
            _remapper.Dispose();
        }

        private void Toggle()
        {
            _remapper.Enabled = !_remapper.Enabled;
            Log.Info($"Toggle → {(_remapper.Enabled ? "ON" : "OFF")}");
            UpdateIcon();
        }

        private void OnToggle(object? sender, EventArgs e) => Toggle();

        private void OnTouchData(TouchEventData e)
        {
            if (_showRipple && e.Kind == TouchEventKind.Down)
                _syncCtx?.Post(_ => RipplePopup.ShowAt(e.ScreenX, e.ScreenY), null);
        }

        private void OnSettings(object? sender, EventArgs e)
        {
            using var dlg = new SettingsForm(_remapper, _deviceName, _showRipple);
            dlg.ShowDialog();
            _showRipple = dlg.ShowRipple;
        }

        private void OnExit(object? sender, EventArgs e)
        {
            Log.Info("User clicked Quit");
            _trayIcon.Visible = false;
            _msgWindow.Dispose();
            _remapper.Dispose();
            Application.Exit();
        }

        private void UpdateIcon()
        {
            bool active = _remapper.Enabled && _remapper.IsConnected;
            bool enabledNoDevice = _remapper.Enabled && !_remapper.IsConnected;

            string stateText;
            if (active)
            {
                _trayIcon.Icon = CreateIcon(Color.LimeGreen);
                stateText = $"ON — {_deviceName}";
                _trayIcon.Text = $"InTouch — {_deviceName} (ON)";
            }
            else if (enabledNoDevice)
            {
                _trayIcon.Icon = CreateIcon(Color.Orange);
                stateText = "Waiting for tablet...";
                _trayIcon.Text = "InTouch — Waiting for tablet...";
            }
            else
            {
                _trayIcon.Icon = CreateIcon(Color.Gray);
                stateText = $"OFF — {_deviceName}";
                _trayIcon.Text = $"InTouch — {_deviceName} (OFF)";
            }

            stateText += "  (Ctrl+Alt+T)";
            _stateItem.Text = stateText;
        }

        /// <summary>
        /// Generate a simple coloured circle icon at runtime (no .ico files needed).
        /// </summary>
        private static Icon CreateIcon(Color color)
        {
            const int size = 32;
            using var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, size - 4, size - 4);

            // "T" letter in the center
            using var font = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("T", font, textBrush, new RectangleF(0, 0, size, size), sf);

            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _remapper.TouchDataReceived -= OnTouchData;
                SystemEvents.SessionEnding -= OnSessionEnding;
                Application.ApplicationExit -= OnApplicationExit;
                _trayIcon.Dispose();
                _msgWindow.Dispose();
                _ghost.Close();
                _ghost.Dispose();
                _remapper.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

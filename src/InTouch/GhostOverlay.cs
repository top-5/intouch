using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WacomMTDN;

namespace InTouch
{
    /// <summary>
    /// A tiny 1×1 transparent, click-through, topmost window that serves as the
    /// HWND target for Wacom MT finger data delivery via WM_FINGERDATA (0x6205).
    /// 
    /// Using HWND-based registration (WacomMTRegisterFingerReadHWND) instead of
    /// HitRect callback ensures the Wacom driver posts touch data as window
    /// messages to this handle, regardless of which window has foreground focus.
    /// 
    /// The window is:
    ///   - WS_EX_LAYERED + 1% opacity (invisible but "exists" for the driver)
    ///   - WS_EX_TRANSPARENT (all clicks/touches pass through to windows below)
    ///   - WS_EX_TOOLWINDOW (doesn't appear in taskbar or Alt+Tab)
    ///   - WS_EX_NOACTIVATE (never steals focus)
    ///   - TopMost (stays above other windows so the driver sees it)
    /// </summary>
    internal sealed class GhostOverlay : Form
    {
        private const byte GhostOpacity = 1; // 1/255 ≈ 0.4% — essentially invisible
        private TouchRemapper? _remapper;

        /// <summary>
        /// Set the remapper that will process WM_FINGERDATA messages.
        /// Must be called before touch registration.
        /// </summary>
        public void SetRemapper(TouchRemapper remapper)
        {
            _remapper = remapper;
        }

        public GhostOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            // Tiny 1×1 window parked at top-left of primary screen
            Bounds = new Rectangle(0, 0, 1, 1);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000   // WS_EX_LAYERED
                            | 0x00000020   // WS_EX_TRANSPARENT  (click-through)
                            | 0x00000080   // WS_EX_TOOLWINDOW   (no Alt+Tab)
                            | 0x08000000;  // WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Set the layered window to nearly-zero opacity.
            // LWA_ALPHA = 0x02
            SetLayeredWindowAttributes(Handle, 0, GhostOpacity, 0x02);
            Log.Info($"GhostOverlay created: hwnd=0x{Handle:X} bounds={Bounds}");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WacomMTConstants.WM_FINGERDATA)
            {
                _remapper?.ProcessFingerData(m.LParam);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Prevent user from closing it accidentally
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        /// <summary>
        /// Resize to cover the current virtual screen (e.g. after monitor changes).
        /// </summary>
        public void ResizeToVirtualScreen()
        {
            int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            Bounds = new Rectangle(vx, vy, vw, vh);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    }
}

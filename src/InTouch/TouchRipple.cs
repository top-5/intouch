using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InTouch
{
    /// <summary>
    /// Shows an expanding, fading circle at a screen position to indicate a touch event,
    /// similar to the Windows "Touch indicator" accessibility feature.
    /// Each ripple is a small per-pixel-alpha layered window that self-destructs after the animation.
    /// </summary>
    internal sealed class RipplePopup : Form
    {
        private const int PopupSize = 80;
        private const int DurationMs = 400;
        private const int MinRadius = 10;
        private const int MaxRadius = 32;

        // Prevent GC while popup is animating (no owner form to hold a reference).
        private static readonly List<RipplePopup> s_active = new();

        private readonly long _startTick;
        private readonly System.Windows.Forms.Timer _timer;

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

        private RipplePopup(int screenX, int screenY)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(PopupSize, PopupSize);
            Location = new Point(screenX - PopupSize / 2, screenY - PopupSize / 2);

            _startTick = Environment.TickCount64;
            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += OnTick;
        }

        /// <summary>
        /// Show a ripple at the given screen coordinates. Must be called on the UI thread.
        /// </summary>
        public static void ShowAt(int screenX, int screenY)
        {
            var popup = new RipplePopup(screenX, screenY);
            s_active.Add(popup);
            popup.Show();
            popup.Render();
            popup._timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            float t = (Environment.TickCount64 - _startTick) / (float)DurationMs;
            if (t >= 1f)
            {
                _timer.Stop();
                _timer.Dispose();
                Close();
                s_active.Remove(this);
                Dispose();
                return;
            }
            Render();
        }

        private void Render()
        {
            float t = Math.Clamp((Environment.TickCount64 - _startTick) / (float)DurationMs, 0f, 1f);
            float eased = 1f - (1f - t) * (1f - t); // ease-out quadratic
            int radius = (int)(MinRadius + (MaxRadius - MinRadius) * eased);
            int alpha = (int)(180 * (1f - t));
            int ringAlpha = Math.Min(255, alpha + 50);

            using var bmp = new Bitmap(PopupSize, PopupSize, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int cx = PopupSize / 2, cy = PopupSize / 2;

                // Filled dark semi-transparent circle
                using var brush = new SolidBrush(Color.FromArgb(alpha, 40, 40, 40));
                g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);

                // Brighter ring around the edge
                using var pen = new Pen(Color.FromArgb(ringAlpha, 100, 100, 100), 2f);
                g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            SetLayeredBitmap(bmp);
        }

        private void SetLayeredBitmap(Bitmap bmp)
        {
            IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            IntPtr memDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            IntPtr oldBmp = NativeMethods.GdiSelectObject(memDc, hBmp);

            try
            {
                var ptDst = new NativeMethods.LPOINT { x = Left, y = Top };
                var size = new NativeMethods.LSIZE { cx = Width, cy = Height };
                var ptSrc = new NativeMethods.LPOINT { x = 0, y = 0 };
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = 0,            // AC_SRC_OVER
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1          // AC_SRC_ALPHA
                };

                NativeMethods.UpdateLayeredWindow(
                    Handle, IntPtr.Zero,
                    ref ptDst, ref size,
                    memDc, ref ptSrc,
                    0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                NativeMethods.GdiSelectObject(memDc, oldBmp);
                NativeMethods.DeleteObject(hBmp);
                NativeMethods.DeleteDC(memDc);
            }
        }
    }
}

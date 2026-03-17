using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace InTouch
{
    /// <summary>
    /// Fullscreen touch canvas that maps 1:1 to the screen.
    /// Touch events paint at the actual screen coordinates.
    /// Floating buttons in top-left for Settings overlay and Close.
    /// Event log overlaid semi-transparently at the bottom.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly TouchRemapper _remapper;
        private readonly CanvasPanel _canvas;
        private readonly Panel _settingsOverlay;
        private readonly Action<TouchEventData>? _handler;

        private TrackBar _tapDurationSlider;
        private TrackBar _dragThresholdSlider;
        private CheckBox _showRippleCheck;
        private Label _tapDurationValue;
        private Label _dragThresholdValue;

        public bool ShowRipple { get; private set; }

        public SettingsForm(TouchRemapper remapper, string deviceName, bool showRipple)
        {
            _remapper = remapper;
            ShowRipple = showRipple;

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(10, 10, 10);
            TopMost = true;
            ShowInTaskbar = true;
            KeyPreview = true;
            DoubleBuffered = true;

            var screen = Screen.PrimaryScreen!.Bounds;
            Bounds = screen;
            int W = screen.Width;
            int H = screen.Height;

            // ── Full-bleed touch canvas ─────────────────────────────
            _canvas = new CanvasPanel { Dock = DockStyle.Fill };
            Controls.Add(_canvas);

            // ── Floating buttons (top-left) ─────────────────────────
            var btnFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            Color btnBg = Color.FromArgb(180, 20, 20, 20);
            Color btnFg = Color.FromArgb(200, 200, 200);

            var settingsBtn = MakeFloatingButton("Settings", btnFont, btnBg, btnFg);
            settingsBtn.Location = new Point(12, 12);
            settingsBtn.Click += (_, _) => ToggleSettings();

            var logFolderBtn = MakeFloatingButton("Log Folder", btnFont, btnBg, btnFg);
            logFolderBtn.Location = new Point(settingsBtn.Right + 6, 12);
            logFolderBtn.Click += (_, _) =>
            {
                string? dir = Path.GetDirectoryName(Log.LogFilePath);
                Close();
                if (dir != null && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            };

            var closeBtn = MakeFloatingButton("\u2715", btnFont, btnBg, Color.FromArgb(255, 100, 100));
            closeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeBtn.Location = new Point(W - closeBtn.Width - 12, 12);
            closeBtn.Click += (_, _) => Close();

            var deviceLbl = new Label
            {
                Text = deviceName,
                ForeColor = Color.FromArgb(80, 80, 80),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(logFolderBtn.Right + 16, 16)
            };

            _canvas.Controls.Add(settingsBtn);
            _canvas.Controls.Add(logFolderBtn);
            _canvas.Controls.Add(closeBtn);
            _canvas.Controls.Add(deviceLbl);

            // ── Settings overlay panel ──────────────────────────────
            _settingsOverlay = BuildSettingsPanel(remapper, deviceName, showRipple, W, H);
            _settingsOverlay.Visible = false;
            _canvas.Controls.Add(_settingsOverlay);

            // Escape closes; Tab toggles settings
            KeyDown += (_, ke) =>
            {
                if (ke.KeyCode == Keys.Escape)
                {
                    if (_settingsOverlay.Visible)
                    {
                        _settingsOverlay.Visible = false;
                        _canvas.OverlayVisible = false;
                    }
                    else
                        Close();
                }
            };

            _handler = OnTouchEvent;
            _remapper.TouchDataReceived += _handler;
        }

        private void ToggleSettings()
        {
            _settingsOverlay.Visible = !_settingsOverlay.Visible;
            if (_settingsOverlay.Visible)
                _settingsOverlay.BringToFront();
            _canvas.OverlayVisible = _settingsOverlay.Visible;
        }

        private static Button MakeFloatingButton(string text, Font font, Color bg, Color fg)
        {
            var sz = TextRenderer.MeasureText(text, font);
            var btn = new Button
            {
                Text = text,
                Font = font,
                ForeColor = fg,
                BackColor = bg,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                AutoSize = false,
                Size = new Size(sz.Width + 32, sz.Height + 14),
                Padding = Padding.Empty
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 40, 40, 40);
            return btn;
        }

        private Panel BuildSettingsPanel(TouchRemapper remapper, string deviceName, bool showRipple, int screenW, int screenH)
        {
            int panelW = Math.Min(700, screenW - 100);
            int panelH = 420;

            var panel = new Panel
            {
                Size = new Size(panelW, panelH),
                Location = new Point((screenW - panelW) / 2, (screenH - panelH) / 2),
                BackColor = Color.FromArgb(24, 24, 24),
                BorderStyle = BorderStyle.None
            };

            // Rounded border via paint
            panel.Paint += (_, pe) =>
            {
                using var pen = new Pen(Color.FromArgb(60, 60, 60), 1.5f);
                pe.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            };

            int pad = 36;
            int y = 28;
            int lw = 220;
            int sw = panelW - lw - 120 - pad * 2;
            int vw = 90;
            int rowH = 68;
            var lf = new Font("Segoe UI", 11f);
            var vf = new Font("Segoe UI", 11f, FontStyle.Bold);

            // Header
            var hdr = new Label
            {
                Text = "Settings",
                ForeColor = Color.FromArgb(100, 180, 255),
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                Location = new Point(pad, y),
                AutoSize = true
            };

            // Close settings button (top-right of panel)
            var closeSettings = new Button
            {
                Text = "\u2715",
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(150, 150, 150),
                BackColor = Color.FromArgb(24, 24, 24),
                Font = new Font("Segoe UI", 11f),
                Size = new Size(36, 36),
                Location = new Point(panelW - 48, 8),
                Cursor = Cursors.Hand
            };
            closeSettings.FlatAppearance.BorderSize = 0;
            closeSettings.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
            closeSettings.Click += (_, _) => panel.Visible = false;

            y += 52;

            // ── Tap Duration ────────────────────────────────────────
            var tapLbl = new Label
            {
                Text = "Tap max duration:",
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = lf,
                Location = new Point(pad, y + 8),
                Size = new Size(lw, 24)
            };
            _tapDurationSlider = new TrackBar
            {
                Minimum = 50, Maximum = 800,
                Value = remapper.TapMaxMs,
                TickFrequency = 50, SmallChange = 10, LargeChange = 50,
                Location = new Point(pad + lw, y),
                Size = new Size(sw, 45),
                BackColor = Color.FromArgb(24, 24, 24)
            };
            _tapDurationValue = new Label
            {
                Text = $"{remapper.TapMaxMs} ms",
                ForeColor = Color.FromArgb(120, 220, 120),
                Font = vf,
                Location = new Point(pad + lw + sw + 12, y + 8),
                Size = new Size(vw, 24)
            };
            _tapDurationSlider.ValueChanged += (_, _) =>
            {
                _tapDurationValue.Text = $"{_tapDurationSlider.Value} ms";
                _remapper.TapMaxMs = _tapDurationSlider.Value;
            };
            y += rowH;

            // ── Drag Threshold ──────────────────────────────────────
            var dragLbl = new Label
            {
                Text = "Drag threshold:",
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = lf,
                Location = new Point(pad, y + 8),
                Size = new Size(lw, 24)
            };
            int dragPct = (int)(_remapper.DragThreshold * 1000);
            _dragThresholdSlider = new TrackBar
            {
                Minimum = 1, Maximum = 50,
                Value = Math.Clamp(dragPct, 1, 50),
                TickFrequency = 5, SmallChange = 1, LargeChange = 5,
                Location = new Point(pad + lw, y),
                Size = new Size(sw, 45),
                BackColor = Color.FromArgb(24, 24, 24)
            };
            _dragThresholdValue = new Label
            {
                Text = $"{dragPct * 0.1f:F1}%",
                ForeColor = Color.FromArgb(120, 220, 120),
                Font = vf,
                Location = new Point(pad + lw + sw + 12, y + 8),
                Size = new Size(vw, 24)
            };
            _dragThresholdSlider.ValueChanged += (_, _) =>
            {
                float val = _dragThresholdSlider.Value / 1000f;
                _dragThresholdValue.Text = $"{_dragThresholdSlider.Value * 0.1f:F1}%";
                _remapper.DragThreshold = val;
            };
            y += rowH;

            // ── Ripple toggle ───────────────────────────────────────
            _showRippleCheck = new CheckBox
            {
                Text = "  Show touch ripple on screen",
                Checked = showRipple,
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = lf,
                Location = new Point(pad, y + 4),
                AutoSize = true
            };
            _showRippleCheck.CheckedChanged += (_, _) => ShowRipple = _showRippleCheck.Checked;
            y += 48;

            // ── Info line ───────────────────────────────────────────
            var sep = new Panel
            {
                Location = new Point(pad, y),
                Size = new Size(panelW - pad * 2, 1),
                BackColor = Color.FromArgb(50, 50, 50)
            };
            y += 16;
            var infoLbl = new Label
            {
                Text = $"{deviceName}   ·   {(remapper.Enabled ? "ON" : "OFF")}   ·   {Log.LogFilePath}",
                ForeColor = Color.FromArgb(90, 90, 90),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(pad, y),
                Size = new Size(panelW - pad * 2, 20)
            };

            panel.Controls.AddRange(new Control[]
            {
                hdr, closeSettings,
                tapLbl, _tapDurationSlider, _tapDurationValue,
                dragLbl, _dragThresholdSlider, _dragThresholdValue,
                _showRippleCheck,
                sep, infoLbl
            });

            return panel;
        }

        private void OnTouchEvent(TouchEventData e)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(() => OnTouchEvent(e)); }
                catch (ObjectDisposedException) { }
                return;
            }
            _canvas.AddEvent(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_handler != null)
                _remapper.TouchDataReceived -= _handler;
            base.OnFormClosed(e);
        }

        // ─────────────────────────────────────────────────────────
        //  Full-screen touch canvas — maps 1:1 to screen coords
        // ─────────────────────────────────────────────────────────
        private sealed class CanvasPanel : Panel
        {
            private readonly record struct TouchMark(int ScreenX, int ScreenY, float RawX, float RawY, long Tick, TouchEventKind Kind, int FingerID);
            private readonly record struct LogEntry(string Text, long Tick, bool IsTap);

            /// <summary>When true, skip heavy canvas repainting to avoid flicker under the settings overlay.</summary>
            public bool OverlayVisible { get; set; }

            private static readonly Color[] FingerColors =
            {
                Color.FromArgb(0, 220, 255),    // cyan    — finger 0
                Color.FromArgb(255, 100, 220),   // magenta — finger 1
                Color.FromArgb(100, 255, 100),   // green   — finger 2
                Color.FromArgb(255, 180, 60),    // orange  — finger 3
                Color.FromArgb(180, 120, 255),   // purple  — finger 4
            };

            private readonly List<TouchMark> _marks = new();
            private readonly List<LogEntry> _log = new();
            private readonly System.Windows.Forms.Timer _timer;
            private const long FadeMs = 4000;
            private const long LogFadeMs = 10000;
            private const int MaxLog = 24;
            private const int MaxMarks = 400;

            public CanvasPanel()
            {
                DoubleBuffered = true;
                BackColor = Color.FromArgb(10, 10, 10);

                _timer = new System.Windows.Forms.Timer { Interval = 25 };
                _timer.Tick += (_, _) =>
                {
                    long cutoff = Environment.TickCount64 - Math.Max(FadeMs, LogFadeMs);
                    _marks.RemoveAll(m => m.Tick < cutoff);
                    _log.RemoveAll(l => l.Tick < cutoff);
                    if (_marks.Count == 0 && _log.Count == 0)
                        _timer.Stop();
                    if (!OverlayVisible)
                        Invalidate();
                };
            }

            public void AddEvent(TouchEventData e)
            {
                _marks.Add(new TouchMark(e.ScreenX, e.ScreenY, e.RawX, e.RawY,
                    Environment.TickCount64, e.Kind, e.FingerID));
                while (_marks.Count > MaxMarks)
                    _marks.RemoveAt(0);

                if (e.Kind != TouchEventKind.Move)
                {
                    string kind = e.Kind switch
                    {
                        TouchEventKind.Down => "DOWN",
                        TouchEventKind.Up   => "UP",
                        TouchEventKind.Tap  => "TAP!",
                        _ => e.Kind.ToString()
                    };
                    string txt = $"{DateTime.Now:HH:mm:ss.fff}  {kind,-5} fid={e.FingerID}  " +
                        $"({e.RawX:F3},{e.RawY:F3})  → ({e.ScreenX},{e.ScreenY})";
                    _log.Add(new LogEntry(txt, Environment.TickCount64, e.Kind == TouchEventKind.Tap));
                    while (_log.Count > MaxLog) _log.RemoveAt(0);
                }

                // Don't Invalidate() per event — the 40fps timer handles repaints.
                // This prevents 133Hz+ input from flooding the paint queue.
                if (!_timer.Enabled) _timer.Start();
            }

            private static Color WithAlpha(Color c, int a) => Color.FromArgb(Math.Clamp(a, 0, 255), c.R, c.G, c.B);

            private void DrawHeadDot(Graphics g, int fid, List<(int x, int y, long tick)> pts)
            {
                if (pts.Count == 0) return;
                var last = pts[^1];
                float age = (Environment.TickCount64 - last.tick) / (float)FadeMs;
                if (age > 1) return;
                var baseColor = FingerColors[fid % FingerColors.Length];
                using var brush = new SolidBrush(WithAlpha(baseColor, Math.Min((int)(255 * (1 - age)), 220)));
                g.FillEllipse(brush, last.x - 5, last.y - 5, 10, 10);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                long now = Environment.TickCount64;

                // ── Log layer (drawn first — underneath the canvas surface) ─
                if (_log.Count > 0)
                {
                    using var logFont = new Font("Consolas", 9f);
                    int lineH = 17;
                    int logH = _log.Count * lineH + 16;
                    int logY = Height - logH - 4;

                    for (int i = 0; i < _log.Count; i++)
                    {
                        float logAge = (now - _log[i].Tick) / (float)LogFadeMs;
                        if (logAge > 1) continue;
                        int a = (int)(100 * (1 - logAge * 0.5f));

                        var color = _log[i].IsTap
                            ? Color.FromArgb(a, 180, 160, 60)
                            : Color.FromArgb(a, 80, 110, 80);

                        using var brush = new SolidBrush(color);
                        g.DrawString(_log[i].Text, logFont, brush, 16, logY + 8 + i * lineH);
                    }
                }

                // ── Subtle grid ─────────────────────────────────────
                using (var gridPen = new Pen(Color.FromArgb(18, 18, 18), 0.5f))
                {
                    for (int i = 1; i < 8; i++)
                    {
                        int gx = Width * i / 8;
                        g.DrawLine(gridPen, gx, 0, gx, Height);
                    }
                    for (int i = 1; i < 5; i++)
                    {
                        int gy = Height * i / 5;
                        g.DrawLine(gridPen, 0, gy, Width, gy);
                    }
                }

                // Crosshair at center
                using (var pen = new Pen(Color.FromArgb(25, 25, 25), 0.5f))
                {
                    g.DrawLine(pen, Width / 2, 0, Width / 2, Height);
                    g.DrawLine(pen, 0, Height / 2, Width, Height / 2);
                }

                // ── Smooth move trails (connected lines per finger) ─
                // Group consecutive move points per finger into strokes.
                // A stroke breaks when there's a gap > 200ms between points.
                var movesByFinger = new Dictionary<int, List<(int x, int y, long tick)>>();
                foreach (var m in _marks)
                {
                    if (m.Kind != TouchEventKind.Move) continue;
                    if (now - m.Tick > FadeMs) continue;
                    if (!movesByFinger.TryGetValue(m.FingerID, out var list))
                    {
                        list = new List<(int, int, long)>();
                        movesByFinger[m.FingerID] = list;
                    }
                    list.Add((m.ScreenX, m.ScreenY, m.Tick));
                }

                foreach (var (fid, pts) in movesByFinger)
                {
                    if (pts.Count < 2) { DrawHeadDot(g, fid, pts); continue; }
                    var baseColor = FingerColors[fid % FingerColors.Length];

                    // Draw segments — batch consecutive points at similar alpha
                    using var pen = new Pen(baseColor, 2.5f);
                    for (int i = 1; i < pts.Count; i++)
                    {
                        // Break stroke if time gap > 200ms (separate touch sequence)
                        if (pts[i].tick - pts[i - 1].tick > 200) continue;

                        float age = (now - pts[i].tick) / (float)FadeMs;
                        int a = Math.Min((int)(255 * (1 - age)), 160);
                        pen.Color = WithAlpha(baseColor, a);
                        g.DrawLine(pen, pts[i - 1].x, pts[i - 1].y, pts[i].x, pts[i].y);
                    }
                    DrawHeadDot(g, fid, pts);
                }

                // ── Down / Tap / Up marks ───────────────────────────
                foreach (var m in _marks)
                {
                    if (m.Kind == TouchEventKind.Move) continue;
                    float age = (now - m.Tick) / (float)FadeMs;
                    if (age > 1) continue;
                    int alpha = (int)(255 * (1 - age));
                    int px = m.ScreenX;
                    int py = m.ScreenY;
                    var fc = FingerColors[m.FingerID % FingerColors.Length];

                    switch (m.Kind)
                    {
                        case TouchEventKind.Tap:
                            float expand = age * 12;
                            int r = (int)(16 + expand);
                            using (var pen = new Pen(Color.FromArgb(alpha, 255, 220, 0), 2.5f))
                                g.DrawEllipse(pen, px - r, py - r, r * 2, r * 2);
                            using (var brush = new SolidBrush(Color.FromArgb(alpha / 2, 255, 200, 0)))
                                g.FillEllipse(brush, px - 4, py - 4, 8, 8);
                            break;

                        case TouchEventKind.Down:
                            using (var brush = new SolidBrush(WithAlpha(fc, alpha)))
                                g.FillEllipse(brush, px - 8, py - 8, 16, 16);
                            using (var pen = new Pen(WithAlpha(fc, alpha / 3), 1f))
                                g.DrawEllipse(pen, px - 16, py - 16, 32, 32);
                            break;

                        case TouchEventKind.Up:
                            using (var pen = new Pen(Color.FromArgb(alpha / 2, 100, 100, 100), 1f))
                                g.DrawEllipse(pen, px - 6, py - 6, 12, 12);
                            break;
                    }
                }
            }
        }
    }
}

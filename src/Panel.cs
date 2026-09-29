using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ExtraDim
{
    enum Kind { Text, Slider, Switch, Row, Sep, Link, Pill, Segment }

    class El
    {
        public Kind Kind;
        public Rectangle R;
        public string Text;
        public Font F;
        public Color C;
        public Func<bool> GetB;
        public Action<bool> SetB;
        public Func<int> GetI;
        public Action<int> SetI;
        public Func<int, string> Fmt;
        public int Min, Max, Step, Snap = 1;
        public bool Invert;
        public Action Click;

        public bool Interactive { get { return Kind != Kind.Text && Kind != Kind.Sep && Kind != Kind.Pill; } }

        public float Frac(int v)
        {
            float f = (v - Min) / (float)(Max - Min);
            return Invert ? 1 - f : f;
        }
    }

    // The tray popup. Everything is custom drawn so it stays crisp at any DPI.
    class Panel : Form
    {
        readonly App app;
        float scale = 1;
        List<El> els = new List<El>();
        El hot, drag;
        Font fTitle, fBody, fSmall, fZh;
        bool anchorBottom = true;
        DateTime hiddenAt;

        public Panel(App app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Ui.Bg;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            SetScale(1, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.Dwm(Handle, 20, 1); // dark border
            Native.Dwm(Handle, 33, 2); // Windows 11 rounded corners
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        void SetScale(float s, bool force)
        {
            if (!force && s == scale) return;
            scale = s;
            if (fTitle != null) { fTitle.Dispose(); fBody.Dispose(); fSmall.Dispose(); fZh.Dispose(); }
            fTitle = new Font("Segoe UI Semibold", 16 * s, GraphicsUnit.Pixel);
            fBody = new Font(Ui.Font, 13 * s, GraphicsUnit.Pixel);
            fSmall = new Font(Ui.Font, 11.5f * s, GraphicsUnit.Pixel);
            fZh = new Font("Microsoft YaHei UI", 11.5f * s, GraphicsUnit.Pixel);
        }

        public void LanguageChanged()
        {
            SetScale(scale, true);
            Rebuild();
        }

        public void Toggle()
        {
            if (Visible) Hide();
            else if ((DateTime.Now - hiddenAt).TotalMilliseconds > 300) ShowNear();
        }

        public void ShowNear()
        {
            var screen = Screen.FromPoint(Cursor.Position);
            SetScale(Native.ScaleAt(Cursor.Position), false);
            Rebuild();
            Rectangle wa = screen.WorkingArea, b = screen.Bounds;
            int m = S(12);
            int x = wa.Left > b.Left ? wa.Left + m : wa.Right - Width - m;
            anchorBottom = !(wa.Top > b.Top);
            int y = anchorBottom ? wa.Bottom - Height - m : wa.Top + m;
            Location = new Point(x, y);
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            Native.SetTop(Handle);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            drag = null;
            Hide();
            hiddenAt = DateTime.Now;
            app.TrimSoon();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Hide();
            base.OnKeyDown(e);
        }

        // ---------- layout ----------

        public void Rebuild()
        {
            int height;
            els = BuildLayout(out height);
            var size = new Size(S(304), height);
            if (ClientSize != size)
            {
                int bottom = Bottom;
                ClientSize = size;
                if (Visible && anchorBottom) Top = bottom - Height;
            }
            Invalidate();
        }

        List<El> BuildLayout(out int height)
        {
            var L = new List<El>();
            var st = app.S;
            int x0 = S(20), x1 = S(304) - S(20), w = x1 - x0, y = S(18);
            Func<int, string> pct = v => v + "%";

            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x0, y, w, S(24)), Text = "Extra Dim", F = fTitle, C = Ui.Fg });
            L.Add(new El { Kind = Kind.Switch, R = new Rectangle(x1 - S(40), y + S(2), S(40), S(22)),
                           GetB = () => st.Enabled, SetB = v => app.SetEnabled(v) });
            y += S(26);
            string status = st.Enabled
                ? Lang.T("On", "已开启") + (st.EyeCare ? Lang.T(" · Eye care ", " · 护眼 ") + st.Temperature + "K" : "")
                : Lang.T("Paused", "已暂停");
            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x0, y, w, S(18)), Text = status, F = fSmall, C = Ui.Sub });
            y += S(18) + S(16);

            if (!st.PerMonitor)
                AddSlider(L, ref y, x0, w, Lang.T("Dim", "调暗"), () => st.Level, v => app.SetLevel(null, v),
                          0, Settings.MaxLevel, 5, 1, false, pct);
            else
            {
                var screens = Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                {
                    string dev = screens[i].DeviceName;
                    string name = Lang.T("Display ", "显示器 ") + (i + 1) + (screens[i].Primary ? Lang.T(" · Main", " · 主屏") : "");
                    AddSlider(L, ref y, x0, w, name, () => st.LevelFor(dev), v => app.SetLevel(dev, v),
                              0, Settings.MaxLevel, 5, 1, false, pct);
                }
            }

            AddRow(L, ref y, x0, w, Lang.T("Eye care", "护眼模式"), () => st.EyeCare, v => app.SetEyeCare(v));
            if (st.EyeCare)
            {
                y += S(4);
                AddSlider(L, ref y, x0, w, Lang.T("Warmth", "色温"), () => st.Temperature, v => app.SetTemperature(v),
                          ColorTemp.Warmest, ColorTemp.Neutral, 100, 100, true,
                          v => (v == ColorTemp.Default ? Lang.T("Recommended  ", "推荐  ") : "") + v + "K");
            }
            AddSep(L, ref y, x0, w);

            if (Screen.AllScreens.Length > 1)
                AddRow(L, ref y, x0, w, Lang.T("Adjust each display", "每个显示器单独调节"), () => st.PerMonitor, v => app.SetPerMonitor(v));
            AddRow(L, ref y, x0, w, Lang.T("Start with Windows", "开机自动启动"), Autostart.Get, v => Autostart.Set(v));

            // Language: a two-segment pill.
            int segW = S(52), segH = S(24), rowH = S(32);
            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x0, y, w, rowH), Text = Lang.T("Language", "语言"), F = fBody, C = Ui.Fg });
            var pill = new Rectangle(x1 - 2 * segW, y + (rowH - segH) / 2, 2 * segW, segH);
            L.Add(new El { Kind = Kind.Pill, R = pill });
            L.Add(new El { Kind = Kind.Segment, R = new Rectangle(pill.X, pill.Y, segW, segH), Text = "EN", F = fSmall,
                           GetB = () => !Lang.Zh, Click = () => app.SetLanguage("en") });
            L.Add(new El { Kind = Kind.Segment, R = new Rectangle(pill.X + segW, pill.Y, segW, segH), Text = "中文",
                           F = fZh,
                           GetB = () => Lang.Zh, Click = () => app.SetLanguage("zh") });
            y += rowH + S(2);
            AddSep(L, ref y, x0, w);

            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x0, y, w, S(18)),
                           Text = app.HotkeyConflict
                               ? Lang.T("Hotkeys are used by another app", "快捷键被其他程序占用")
                               : Lang.T("Ctrl+Alt+PgUp/PgDn dim · End pause", "Ctrl+Alt+PgUp/PgDn 调节 · End 暂停"),
                           F = fSmall, C = app.HotkeyConflict ? Ui.Warn : Ui.Sub });
            L.Add(new El { Kind = Kind.Link, R = new Rectangle(x1 - S(36), y, S(36), S(18)), Text = Lang.T("Quit", "退出"),
                           F = fSmall, C = Ui.Sub, Click = () => app.Exit() });
            y += S(18);
            height = y + S(14);
            return L;
        }

        void AddSlider(List<El> L, ref int y, int x, int w, string label, Func<int> get, Action<int> set,
                       int min, int max, int step, int snap, bool invert, Func<int, string> fmt)
        {
            L.Add(new El { Kind = Kind.Text, R = new Rectangle(x, y, w, S(20)), Text = label, F = fBody, C = Ui.Fg, GetI = get, Fmt = fmt });
            y += S(22);
            L.Add(new El { Kind = Kind.Slider, R = new Rectangle(x, y, w, S(22)), GetI = get, SetI = set,
                           Min = min, Max = max, Step = step, Snap = snap, Invert = invert });
            y += S(22) + S(12);
        }

        void AddRow(List<El> L, ref int y, int x, int w, string label, Func<bool> get, Action<bool> set)
        {
            L.Add(new El { Kind = Kind.Row, R = new Rectangle(x, y, w, S(32)), Text = label, F = fBody, GetB = get, SetB = set });
            y += S(34);
        }

        void AddSep(List<El> L, ref int y, int x, int w)
        {
            y += S(6);
            L.Add(new El { Kind = Kind.Sep, R = new Rectangle(x, y, w, 1) });
            y += S(10);
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            bool enabled = app.S.Enabled;

            foreach (var el in els)
            {
                bool isHot = el == hot || el == drag;
                switch (el.Kind)
                {
                    case Kind.Text:
                        TextRenderer.DrawText(g, el.Text, el.F, el.R, el.C, left);
                        if (el.Fmt != null)
                            TextRenderer.DrawText(g, el.Fmt(el.GetI()), el.F, el.R, Ui.Sub, right);
                        break;
                    case Kind.Slider:
                    {
                        int cy = el.R.Top + el.R.Height / 2, th = Math.Max(3, S(4));
                        float fx = el.R.Left + el.Frac(el.GetI()) * el.R.Width;
                        Ui.FillRound(g, Ui.Off, new RectangleF(el.R.Left, cy - th / 2f, el.R.Width, th), th / 2f);
                        if (fx > el.R.Left)
                            Ui.FillRound(g, enabled ? Ui.Accent : Ui.Sub, new RectangleF(el.R.Left, cy - th / 2f, fx - el.R.Left, th), th / 2f);
                        float kr = isHot ? S(8) : S(7);
                        using (var b = new SolidBrush(Ui.Fg))
                            g.FillEllipse(b, fx - kr, cy - kr, kr * 2, kr * 2);
                        break;
                    }
                    case Kind.Switch:
                        Ui.Switch(g, el.R, el.GetB(), isHot);
                        break;
                    case Kind.Row:
                    {
                        TextRenderer.DrawText(g, el.Text, el.F, el.R, Ui.Fg, left);
                        int sw = S(34), sh = S(18);
                        Ui.Switch(g, new Rectangle(el.R.Right - sw, el.R.Top + (el.R.Height - sh) / 2, sw, sh), el.GetB(), isHot);
                        break;
                    }
                    case Kind.Pill:
                        Ui.FillRound(g, Ui.Surface, el.R, el.R.Height / 2f);
                        break;
                    case Kind.Segment:
                    {
                        bool sel = el.GetB();
                        if (sel) Ui.FillRound(g, Ui.SurfaceHot, Rectangle.Inflate(el.R, -S(2), -S(2)), el.R.Height / 2f);
                        TextRenderer.DrawText(g, el.Text, el.F, el.R, sel || isHot ? Ui.Fg : Ui.Sub, center);
                        break;
                    }
                    case Kind.Sep:
                        using (var p = new Pen(Ui.Line))
                            g.DrawLine(p, el.R.Left, el.R.Top, el.R.Right, el.R.Top);
                        break;
                    case Kind.Link:
                        TextRenderer.DrawText(g, el.Text, el.F, el.R, isHot ? Ui.Fg : el.C, right);
                        break;
                }
            }
        }

        // ---------- input ----------

        El Hit(Point p)
        {
            for (int i = els.Count - 1; i >= 0; i--)
            {
                var el = els[i];
                if (!el.Interactive) continue;
                var r = el.Kind == Kind.Slider ? Rectangle.Inflate(el.R, S(8), S(4)) : el.R;
                if (r.Contains(p)) return el;
            }
            return null;
        }

        void SetFromX(El el, int x)
        {
            float frac = Math.Max(0, Math.Min(1, (x - el.R.Left) / (float)el.R.Width));
            if (el.Invert) frac = 1 - frac;
            int v = el.Min + (int)Math.Round(frac * (el.Max - el.Min) / el.Snap) * el.Snap;
            if (v != el.GetI()) el.SetI(v);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var el = Hit(e.Location);
            if (el == null || e.Button != MouseButtons.Left) return;
            switch (el.Kind)
            {
                case Kind.Slider:
                    drag = el;
                    Capture = true;
                    SetFromX(el, e.X);
                    break;
                case Kind.Switch:
                case Kind.Row:
                    el.SetB(!el.GetB());
                    break;
                case Kind.Segment:
                case Kind.Link:
                    el.Click();
                    break;
            }
            if (!IsDisposed) Rebuild();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag != null) { SetFromX(drag, e.X); return; }
            var h = Hit(e.Location);
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag != null) { drag = null; Capture = false; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != null) { hot = null; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var el = Hit(e.Location);
            if (el == null || el.Kind != Kind.Slider) return;
            // Wheel up always means "more": darker, or warmer.
            int dir = (e.Delta > 0 ? 1 : -1) * (el.Invert ? -1 : 1);
            el.SetI(Settings.Clamp(el.GetI() + dir * el.Step, el.Min, el.Max));
            Rebuild();
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ExtraDim
{
    // Small on-screen indicator shown when a hotkey is used.
    class Osd : Form
    {
        readonly Timer hold = new Timer { Interval = 1200 }, fade = new Timer { Interval = 15 };
        string label = "";
        int value = -1, alpha;
        float scale = 1;
        Font fBody;

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(28, 28, 33);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            hold.Tick += delegate { hold.Stop(); fade.Start(); };
            fade.Tick += delegate
            {
                alpha -= 18;
                if (alpha <= 0) { fade.Stop(); Hide(); return; }
                Native.SetLayeredWindowAttributes(Handle, 0, (byte)alpha, 2);
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                            | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetLayeredWindowAttributes(Handle, 0, 0, 2);
            Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        public void Flash(Screen screen, string text, int percent)
        {
            label = text;
            value = percent;
            var center = new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
            float s = Native.ScaleAt(center);
            if (fBody == null || s != scale || fBody.Name != Ui.Font)
            {
                scale = s;
                if (fBody != null) fBody.Dispose();
                fBody = new Font(Ui.Font, 14 * s, GraphicsUnit.Pixel);
            }
            var size = new Size(S(240), percent >= 0 ? S(62) : S(44));
            var wa = screen.WorkingArea;
            Bounds = new Rectangle(wa.Left + (wa.Width - size.Width) / 2, wa.Bottom - size.Height - S(80), size.Width, size.Height);
            using (var p = Ui.Round(new RectangleF(0, 0, size.Width, size.Height), S(12)))
            {
                var old = Region;
                Region = new Region(p);
                if (old != null) old.Dispose();
            }
            fade.Stop();
            alpha = 240;
            Native.SetLayeredWindowAttributes(Handle, 0, (byte)alpha, 2);
            if (!Visible) Show();
            Native.SetTop(Handle);
            Invalidate();
            hold.Stop();
            hold.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (value < 0)
            {
                TextRenderer.DrawText(g, label, fBody, ClientRectangle, Ui.Fg, flags | TextFormatFlags.HorizontalCenter);
                return;
            }
            var top = new Rectangle(S(18), S(10), Width - S(36), S(24));
            TextRenderer.DrawText(g, label, fBody, top, Ui.Fg, flags | TextFormatFlags.Left);
            TextRenderer.DrawText(g, value + "%", fBody, top, Ui.Sub, flags | TextFormatFlags.Right);
            var bar = new RectangleF(S(18), S(42), Width - S(36), S(5));
            Ui.FillRound(g, Ui.Off, bar, bar.Height / 2);
            float frac = value / (float)Settings.MaxLevel;
            if (frac > 0) Ui.FillRound(g, Ui.Accent, new RectangleF(bar.X, bar.Y, bar.Width * frac, bar.Height), bar.Height / 2);
        }
    }
}

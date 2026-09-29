using System;
using System.Drawing;
using System.Windows.Forms;

namespace ExtraDim
{
    // A click-through, never-focused layer covering one display.
    class Overlay : Form
    {
        // Eye-care fallback tint, only used when the driver rejects gamma ramps.
        Color tint = Color.Black;

        public readonly string Device;
        public double CurD, CurT, TgtD, TgtT;

        public Overlay(Screen screen)
        {
            Device = screen.DeviceName;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            BackColor = Color.Black;
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
            // Screenshots and screen sharing see the real, undimmed screen.
            Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // WM_NCHITTEST -> HTTRANSPARENT
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }  // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref m);
        }

        // Sets the fallback tint immediately (colour temperature is animated by the caller).
        public void SetTint(Color color, double strength)
        {
            tint = color;
            CurT = TgtT = strength;
        }

        // Eases current values toward targets; returns true while still moving.
        public bool Step()
        {
            bool a = Approach(ref CurD, TgtD), b = Approach(ref CurT, TgtT);
            return a || b;
        }

        static bool Approach(ref double cur, double target)
        {
            double diff = target - cur;
            if (Math.Abs(diff) < 0.002)
            {
                if (cur == target) return false;
                cur = target;
                return true;
            }
            cur += diff * 0.2;
            return true;
        }

        // A black layer (dim d) over a tint layer (strength t), folded into one
        // layered window: alpha = 1-(1-d)(1-t), colour = (1-d)*t*Tint / alpha.
        public void Apply()
        {
            double d = CurD, t = CurT, a = 1 - (1 - d) * (1 - t);
            if (a < 0.004)
            {
                if (Visible) Hide();
                return;
            }
            double k = (1 - d) * t / a;
            var c = Color.FromArgb((int)(tint.R * k), (int)(tint.G * k), (int)(tint.B * k));
            if (BackColor != c) BackColor = c;
            Native.SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(a * 255), 2);
            if (!Visible)
            {
                Show();
                Native.SetTop(Handle);
            }
        }
    }
}

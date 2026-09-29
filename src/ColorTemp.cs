using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExtraDim
{
    // Eye-care colour temperature. Scales R/G/B like f.lux / Night Light do:
    // whites get warmer while blacks stay black, so there is no haze.
    static class ColorTemp
    {
        public const int Neutral = 6500, Warmest = 3400, Default = 4800;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDC(string driver, string device, string output, IntPtr init);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr dc, ushort[] ramp);

        // Channel multipliers for a black-body colour temperature, normalised so 6500K = (1,1,1).
        public static void Multipliers(double kelvin, out double r, out double g, out double b)
        {
            double r0, g0, b0, rk, gk, bk;
            Blackbody(Neutral, out r0, out g0, out b0);
            Blackbody(kelvin, out rk, out gk, out bk);
            r = Math.Min(1, rk / r0);
            g = Math.Min(1, gk / g0);
            b = Math.Min(1, bk / b0);
        }

        // Curve fit of the Planckian locus in sRGB (valid ~1000K-40000K).
        static void Blackbody(double kelvin, out double r, out double g, out double b)
        {
            double t = kelvin / 100.0;
            if (t <= 66)
            {
                r = 255;
                g = 99.4708025861 * Math.Log(t) - 161.1195681661;
                b = t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            }
            else
            {
                r = 329.698727446 * Math.Pow(t - 60, -0.1332047592);
                g = 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
                b = 255;
            }
            r = Math.Max(0, Math.Min(255, r)) / 255;
            g = Math.Max(0, Math.Min(255, g)) / 255;
            b = Math.Max(0, Math.Min(255, b)) / 255;
        }

        // ---- Preferred: full-screen colour matrix (Magnification API). Exact per-channel multiply,
        // works where gamma ramps are blocked (HDR / auto colour management), and Windows drops it
        // automatically if the process dies.

        [StructLayout(LayoutKind.Sequential)]
        struct ColorEffect { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)] public float[] M; }

        [DllImport("Magnification.dll")] static extern bool MagInitialize();
        [DllImport("Magnification.dll")] static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] static extern bool MagSetFullscreenColorEffect(ref ColorEffect e);

        static bool magReady, magBroken;

        // Applies to every display at once. Returns false if unavailable.
        // At neutral it releases the magnification runtime entirely, so an idle app holds nothing.
        public static bool ApplyMatrix(double kelvin)
        {
            if (magBroken) return false;
            try
            {
                if (kelvin >= Neutral)
                {
                    if (!magReady) return true;
                    var id = new ColorEffect { M = new float[25] };
                    id.M[0] = id.M[6] = id.M[12] = id.M[18] = id.M[24] = 1;
                    MagSetFullscreenColorEffect(ref id);
                    MagUninitialize();
                    magReady = false;
                    return true;
                }
                if (!magReady && !(magReady = MagInitialize())) { magBroken = true; return false; }
                double r, g, b;
                Multipliers(kelvin, out r, out g, out b);
                var e = new ColorEffect { M = new float[25] };
                e.M[0] = (float)r; e.M[6] = (float)g; e.M[12] = (float)b; e.M[18] = 1; e.M[24] = 1;
                if (MagSetFullscreenColorEffect(ref e)) return true;
            }
            catch { }
            magBroken = true;
            return false;
        }

        // ---- Fallback: per-display gamma ramp.

        static bool touched;

        public static bool Apply(string device, double kelvin)
        {
            double r, g, b;
            Multipliers(kelvin, out r, out g, out b);
            var ramp = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                double v = i * 257;
                ramp[i] = (ushort)(v * r);
                ramp[256 + i] = (ushort)(v * g);
                ramp[512 + i] = (ushort)(v * b);
            }
            IntPtr dc = CreateDC(null, device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            if (kelvin < Neutral) touched = true;
            try { return SetDeviceGammaRamp(dc, ramp); }
            finally { DeleteDC(dc); }
        }

        // Restores neutral colour, but only if we changed it (unless forced), so other tools are left alone.
        public static void ResetAll(bool force = false)
        {
            if (magReady) ApplyMatrix(Neutral);
            if (!touched && !force) return;
            foreach (var sc in Screen.AllScreens) Apply(sc.DeviceName, Neutral);
            touched = false;
        }

        // Fallback when a driver refuses gamma ramps: an overlay tint that maps white exactly
        // to the target colour (blacks lift slightly, unavoidable with alpha blending).
        public static void OverlayTint(double kelvin, out Color color, out double alpha)
        {
            double r, g, b;
            Multipliers(kelvin, out r, out g, out b);
            alpha = 1 - b;
            if (alpha < 0.001) { color = Color.Black; alpha = 0; return; }
            color = Color.FromArgb(255, (int)Math.Round(255 * Math.Max(0, g - b) / alpha), 0);
        }
    }
}

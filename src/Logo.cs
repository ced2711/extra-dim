using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ExtraDim
{
    static class Logo
    {
        public static Bitmap Render(int size, bool on)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var path = Ui.Round(new RectangleF(0, 0, size, size), size * 0.24f))
                using (var bg = new SolidBrush(Color.FromArgb(28, 28, 33)))
                using (var moon = new SolidBrush(on ? Color.FromArgb(242, 206, 132) : Color.FromArgb(118, 118, 128)))
                {
                    g.FillPath(bg, path);
                    g.SetClip(path);
                    float m = size * 0.2f, d = size - 2 * m;
                    g.FillEllipse(moon, m, m, d, d);
                    g.FillEllipse(bg, m + size * 0.22f, m - size * 0.16f, d, d);
                }
            }
            return bmp;
        }

        public static Icon MakeIcon(int size, bool on, out IntPtr handle)
        {
            using (var bmp = Render(size, on))
            {
                handle = bmp.GetHicon();
                return Icon.FromHandle(handle);
            }
        }

        // Writes a multi-resolution .ico made of PNG frames.
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var frames = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Render(s, true))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim);
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) w.Write(f);
            }
        }
    }
}

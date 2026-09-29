using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ExtraDim
{
    static class Native
    {
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                         WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
        public const uint VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23;
        public const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        public const uint EVENT_SYSTEM_FOREGROUND = 3;

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
        [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr proc, int cls, ref PowerThrottling info, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct PowerThrottling { public uint Version, ControlMask, StateMask; }

        // Hands pages we are not using back to Windows; they fault back in cheaply if needed.
        public static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }

        // Low priority + Windows 11 efficiency mode (EcoQoS): never competes with real work.
        public static void BeQuiet()
        {
            try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal; } catch { }
            try
            {
                // ProcessPowerThrottling: EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION
                var p = new PowerThrottling { Version = 1, ControlMask = 1 | 4, StateMask = 1 | 4 };
                SetProcessInformation(GetCurrentProcess(), 4, ref p, Marshal.SizeOf(p));
            }
            catch { }
        }

        public static void SetTop(IntPtr h)
        {
            // HWND_TOPMOST, NOSIZE | NOMOVE | NOACTIVATE | NOOWNERZORDER
            SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
        }

        public static float ScaleAt(Point p)
        {
            try
            {
                uint dx, dy;
                IntPtr mon = MonitorFromPoint(new POINT(p.X, p.Y), 2);
                if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0) return dx / 96f;
            }
            catch { }
            return 1f;
        }

        public static void Dwm(IntPtr h, int attr, int val)
        {
            try { DwmSetWindowAttribute(h, attr, ref val, 4); } catch { }
        }
    }
}

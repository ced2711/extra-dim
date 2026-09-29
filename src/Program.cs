using System;
using System.Threading;
using System.Windows.Forms;

namespace ExtraDim
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--write-icon")
            {
                Logo.WriteIco(args[1]);
                return;
            }

            bool first;
            using (var mutex = new Mutex(true, @"Local\ExtraDim.SingleInstance", out first))
            {
                if (!first)
                {
                    // Already running: just pop its panel open.
                    Native.PostMessage(new IntPtr(0xffff), MsgWindow.WM_SHOWPANEL, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                // Never leave the screen tinted if something goes wrong.
                Application.ThreadException += (s, e) => { ColorTemp.ResetAll(); Environment.Exit(1); };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ColorTemp.ResetAll();
                AppDomain.CurrentDomain.ProcessExit += (s, e) => ColorTemp.ResetAll();
                Native.BeQuiet();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
                GC.KeepAlive(mutex);
            }
        }
    }
}

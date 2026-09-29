using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ExtraDim
{
    class Settings
    {
        public const int MaxLevel = 90; // never go fully black

        public bool Enabled = true;
        public int Level = 30;
        public bool EyeCare = false;
        public int Temperature = ColorTemp.Default;
        public string Language = "en";
        public bool PerMonitor = false;
        public Dictionary<string, int> Monitors = new Dictionary<string, int>();
        public bool FirstRun;

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExtraDim", "settings.ini"); }
        }

        public int LevelFor(string device)
        {
            int v;
            if (PerMonitor && Monitors.TryGetValue(device, out v)) return v;
            return Level;
        }

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) { s.FirstRun = true; return s; }
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n;
                    int.TryParse(v, out n);
                    bool b = v == "1";
                    if (k.StartsWith("monitor:")) { s.Monitors[k.Substring(8)] = Clamp(n, 0, MaxLevel); continue; }
                    switch (k)
                    {
                        case "enabled": s.Enabled = b; break;
                        case "level": s.Level = Clamp(n, 0, MaxLevel); break;
                        case "eyeCare": s.EyeCare = b; break;
                        case "temperature": s.Temperature = Clamp(n, ColorTemp.Warmest, ColorTemp.Neutral); break;
                        case "language": s.Language = v == "zh" ? "zh" : "en"; break;
                        case "perMonitor": s.PerMonitor = b; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("enabled=" + (Enabled ? 1 : 0));
                sb.AppendLine("level=" + Level);
                sb.AppendLine("eyeCare=" + (EyeCare ? 1 : 0));
                sb.AppendLine("temperature=" + Temperature);
                sb.AppendLine("language=" + Language);
                sb.AppendLine("perMonitor=" + (PerMonitor ? 1 : 0));
                foreach (var kv in Monitors) sb.AppendLine("monitor:" + kv.Key + "=" + kv.Value);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "ExtraDim";

        public static bool Get()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(Name) != null;
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}

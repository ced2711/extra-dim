using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace ExtraDim
{
    // Idle cost is kept at zero: no polling while nothing is on screen, windows are created
    // on first use, and memory is handed back to Windows after each interaction.
    class App : ApplicationContext
    {
        public readonly Settings S;
        public bool HotkeyConflict;

        readonly Dictionary<string, Overlay> overlays = new Dictionary<string, Overlay>();
        readonly SynchronizationContext ui;
        readonly MsgWindow msg;
        readonly NotifyIcon tray;
        readonly MenuItem miToggle, miEyeCare, miSettings, miQuit;
        readonly Timer anim = new Timer { Interval = 15 };
        readonly Timer safetyNet = new Timer { Interval = 5000 };
        readonly Timer saveLater = new Timer { Interval = 600 };
        readonly Timer displayChanged = new Timer { Interval = 800 };
        readonly Timer trimLater = new Timer { Interval = 4000 };
        readonly Native.WinEventProc onForeground; // kept referenced so the GC can't collect it
        Panel panel;
        Osd osd;
        IntPtr fgHook, iconHandle;
        bool? iconEnabled;
        bool usingMatrix = true;
        double curK = ColorTemp.Neutral, tgtK = ColorTemp.Neutral; // colour temperature, animated

        public App()
        {
            ui = new WindowsFormsSynchronizationContext();
            S = Settings.Load();
            Lang.Zh = S.Language == "zh";
            if (S.EyeCare) ColorTemp.ResetAll(true); // in case a previous run was killed while warm
            if (Autostart.Get()) Autostart.Set(true);  // keep the entry pointing at this exe if it moved

            msg = new MsgWindow(this);
            HotkeyConflict = !msg.RegisterHotkeys();
            onForeground = delegate { RaiseAll(); };

            anim.Tick += delegate { Animate(); };
            safetyNet.Tick += delegate { SafetyNet(); };
            saveLater.Tick += delegate { saveLater.Stop(); S.Save(); };
            displayChanged.Tick += delegate { displayChanged.Stop(); SyncOverlays(); ApplyColor(); Refresh(); };
            trimLater.Tick += delegate { trimLater.Stop(); Native.TrimMemory(); };
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionEnding += OnSessionEnding;

            miToggle = new MenuItem("", delegate { SetEnabled(!S.Enabled); });
            miEyeCare = new MenuItem("", delegate { SetEyeCare(!S.EyeCare); });
            miSettings = new MenuItem("", delegate { ShowPanel(); });
            miQuit = new MenuItem("", delegate { Exit(); });
            tray = new NotifyIcon
            {
                ContextMenu = new ContextMenu(new[] { miToggle, miEyeCare, miSettings, new MenuItem("-"), miQuit })
            };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Panel.Toggle();
                else if (e.Button == MouseButtons.Middle) SetEnabled(!S.Enabled);
            };

            Refresh();
            tray.Visible = true;
            if (S.FirstRun)
            {
                tray.ShowBalloonTip(4000, "Extra Dim",
                    Lang.T("Running in the tray. Click the moon icon to adjust brightness and eye care.",
                           "已在托盘运行，点击月亮图标即可调节亮度和护眼模式。"), ToolTipIcon.None);
                S.Save();
            }
            TrimSoon();
        }

        Panel Panel
        {
            get
            {
                if (panel == null) { panel = new Panel(this); panel.CreateControl(); }
                return panel;
            }
        }

        Osd Osd
        {
            get
            {
                if (osd == null) { osd = new Osd(); osd.CreateControl(); }
                return osd;
            }
        }

        bool PanelVisible { get { return panel != null && panel.Visible; } }

        public void TrimSoon() { trimLater.Stop(); trimLater.Start(); }

        // ---------- state changes (called by the panel, tray and hotkeys) ----------

        public void SetEnabled(bool on) { S.Enabled = on; Changed(); }

        public void SetLevel(string device, int v)
        {
            v = Settings.Clamp(v, 0, Settings.MaxLevel);
            if (device == null) S.Level = v; else S.Monitors[device] = v;
            S.Enabled = true;
            Changed();
        }

        public void SetEyeCare(bool on) { S.EyeCare = on; if (on) S.Enabled = true; Changed(); }

        public void SetTemperature(int k)
        {
            S.Temperature = Settings.Clamp(k, ColorTemp.Warmest, ColorTemp.Neutral);
            S.EyeCare = true;
            S.Enabled = true;
            Changed();
        }

        public void SetPerMonitor(bool on)
        {
            if (on)
                foreach (var sc in Screen.AllScreens)
                    if (!S.Monitors.ContainsKey(sc.DeviceName)) S.Monitors[sc.DeviceName] = S.Level;
            S.PerMonitor = on;
            Changed();
        }

        public void SetLanguage(string lang)
        {
            S.Language = lang;
            Lang.Zh = lang == "zh";
            if (panel != null) panel.LanguageChanged();
            Changed();
        }

        void Changed()
        {
            Refresh();
            saveLater.Stop();
            saveLater.Start();
            if (PanelVisible) panel.Rebuild();
        }

        public void Hotkey(int id)
        {
            var screen = Screen.FromPoint(Cursor.Position);
            if (id == MsgWindow.HK_TOGGLE)
            {
                SetEnabled(!S.Enabled);
                Osd.Flash(screen, S.Enabled ? Lang.T("Extra Dim on", "Extra Dim 已开启") : Lang.T("Extra Dim paused", "Extra Dim 已暂停"), -1);
            }
            else
            {
                int delta = id == MsgWindow.HK_BRIGHTER ? -5 : 5;
                string dev = S.PerMonitor ? screen.DeviceName : null;
                int level = S.Enabled ? S.LevelFor(screen.DeviceName) : 0;
                SetLevel(dev, level + delta);
                Osd.Flash(screen, Lang.T("Dim", "调暗"), S.LevelFor(screen.DeviceName));
            }
            TrimSoon();
        }

        public void ShowPanel() { Panel.ShowNear(); }

        public void Exit()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            if (fgHook != IntPtr.Zero) Native.UnhookWinEvent(fgHook);
            ColorTemp.ResetAll();
            S.Save();
            tray.Visible = false;
            tray.Dispose();
            msg.DestroyHandle();
            foreach (var o in overlays.Values) o.Dispose();
            if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
            ExitThread();
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            ColorTemp.ResetAll();
            S.Save();
        }

        // ---------- overlays & colour ----------

        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // May arrive on another thread and in bursts; debounce on the UI thread.
            ui.Post(delegate { displayChanged.Stop(); displayChanged.Start(); }, null);
        }

        // Overlays are only created for displays that actually need one.
        Overlay OverlayFor(Screen sc)
        {
            Overlay o;
            if (!overlays.TryGetValue(sc.DeviceName, out o))
            {
                o = new Overlay(sc);
                o.CreateControl();
                overlays[sc.DeviceName] = o;
            }
            return o;
        }

        void SyncOverlays()
        {
            var screens = Screen.AllScreens;
            foreach (var sc in screens)
            {
                Overlay o;
                if (overlays.TryGetValue(sc.DeviceName, out o) && o.Bounds != sc.Bounds) o.Bounds = sc.Bounds;
            }
            foreach (var gone in overlays.Keys.Where(k => screens.All(sc => sc.DeviceName != k)).ToList())
            {
                overlays[gone].Dispose();
                overlays.Remove(gone);
            }
        }

        void Refresh()
        {
            foreach (var sc in Screen.AllScreens)
            {
                double d = S.Enabled ? S.LevelFor(sc.DeviceName) / 100.0 : 0;
                Overlay o;
                if (overlays.TryGetValue(sc.DeviceName, out o)) o.TgtD = d;
                else if (d > 0) OverlayFor(sc).TgtD = d;
            }
            tgtK = S.Enabled && S.EyeCare ? S.Temperature : ColorTemp.Neutral;
            anim.Start();
            UpdateTray();
        }

        void Animate()
        {
            bool moving = false;
            if (curK != tgtK)
            {
                double diff = tgtK - curK;
                curK = Math.Abs(diff) < 15 ? tgtK : curK + diff * 0.2;
                ApplyColor();
                moving = true;
            }
            foreach (var o in overlays.Values)
                if (o.Step()) { o.Apply(); moving = true; }
            if (!moving)
            {
                anim.Stop();
                UpdateWatch();
            }
        }

        // Warm the screen: colour matrix first, then gamma ramp, then an overlay tint as a last resort.
        void ApplyColor()
        {
            usingMatrix = ColorTemp.ApplyMatrix(curK);
            foreach (var sc in Screen.AllScreens)
            {
                Overlay o;
                overlays.TryGetValue(sc.DeviceName, out o);
                if (usingMatrix || ColorTemp.Apply(sc.DeviceName, curK) || curK >= ColorTemp.Neutral)
                {
                    if (o != null) { o.SetTint(Color.Black, 0); o.Apply(); }
                }
                else
                {
                    Color c;
                    double t;
                    ColorTemp.OverlayTint(curK, out c, out t);
                    o = OverlayFor(sc);
                    o.SetTint(c, t);
                    o.Apply();
                }
            }
        }

        // While a layer is on screen, re-raise it whenever the foreground window changes
        // (other always-on-top windows such as the taskbar can climb above it). A slow safety
        // net also re-asserts a gamma ramp, which some games reset. When nothing is shown,
        // there is no hook and no timer at all.
        void UpdateWatch()
        {
            bool shown = overlays.Values.Any(o => o.Visible);
            if (shown && fgHook == IntPtr.Zero)
                fgHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                                                IntPtr.Zero, onForeground, 0, 0, 0);
            else if (!shown && fgHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(fgHook);
                fgHook = IntPtr.Zero;
            }
            safetyNet.Enabled = shown || (curK < ColorTemp.Neutral && !usingMatrix);
        }

        void RaiseAll()
        {
            foreach (var o in overlays.Values)
                if (o.Visible) Native.SetTop(o.Handle);
            if (PanelVisible) Native.SetTop(panel.Handle);
            if (osd != null && osd.Visible) Native.SetTop(osd.Handle);
        }

        void SafetyNet()
        {
            RaiseAll();
            if (curK < ColorTemp.Neutral && !usingMatrix && !anim.Enabled) ApplyColor();
        }

        void UpdateTray()
        {
            if (iconEnabled != S.Enabled)
            {
                iconEnabled = S.Enabled;
                var old = tray.Icon;
                IntPtr oldHandle = iconHandle;
                tray.Icon = Logo.MakeIcon(SystemInformation.SmallIconSize.Width, S.Enabled, out iconHandle);
                if (old != null) old.Dispose();
                if (oldHandle != IntPtr.Zero) Native.DestroyIcon(oldHandle);
            }

            string text = "Extra Dim · ";
            if (!S.Enabled) text += Lang.T("Paused", "已暂停");
            else if (S.PerMonitor) text += Lang.T("Per display", "分屏调节");
            else text += Lang.T("Dim ", "调暗 ") + S.Level + "%";
            if (S.Enabled && S.EyeCare) text += Lang.T(" · Eye care", " · 护眼");
            tray.Text = text;

            miToggle.Text = S.Enabled ? Lang.T("Pause", "暂停") : Lang.T("Resume", "开启");
            miEyeCare.Text = Lang.T("Eye care", "护眼模式");
            miEyeCare.Checked = S.EyeCare;
            miSettings.Text = Lang.T("Settings…", "设置…");
            miQuit.Text = Lang.T("Quit", "退出");
        }
    }

    // Invisible window that receives global hotkeys and the "show panel" ping from a second launch.
    class MsgWindow : NativeWindow
    {
        public const int HK_BRIGHTER = 1, HK_DARKER = 2, HK_TOGGLE = 3;
        public static readonly int WM_SHOWPANEL = Native.RegisterWindowMessage("ExtraDim.ShowPanel");
        readonly App app;

        public MsgWindow(App app)
        {
            this.app = app;
            CreateHandle(new CreateParams { Caption = "ExtraDim" });
        }

        public bool RegisterHotkeys()
        {
            uint mods = Native.MOD_CONTROL | Native.MOD_ALT;
            bool ok = Native.RegisterHotKey(Handle, HK_BRIGHTER, mods, Native.VK_PRIOR);
            ok &= Native.RegisterHotKey(Handle, HK_DARKER, mods, Native.VK_NEXT);
            ok &= Native.RegisterHotKey(Handle, HK_TOGGLE, mods | Native.MOD_NOREPEAT, Native.VK_END);
            return ok;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) { app.Hotkey(m.WParam.ToInt32()); return; } // WM_HOTKEY
            if (m.Msg == WM_SHOWPANEL) { app.ShowPanel(); return; }
            base.WndProc(ref m);
        }
    }
}

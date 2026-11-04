using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("QuickCut")]
[assembly: System.Reflection.AssemblyProduct("QuickCut")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace QuickCut
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Build helpers: generate the exe icon / render a preview image of the ring.
            if (args.Length == 2 && args[0] == "--make-icon") { Art.WriteIco(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--make-store-logos") { Art.WriteStoreLogos(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--store-shots")
            {
                Application.EnableVisualStyles();
                StoreShots.Write(args[1]);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--preview")
            {
                // --preview out.png [hoverIndex] [theme] [hoverStyle]
                Config pc = Config.CreateDefault();
                if (args.Length > 3) pc.ThemeMode = args[3];
                if (args.Length > 4) pc.HoverStyle = args[4];
                RingForm.SavePreview(pc, args[1], args.Length > 2 ? int.Parse(args[2]) : 0);
                return 0;
            }

            if (args.Length >= 2 && args[0] == "--settings-preview")
            {
                // --settings-preview out.png [theme] [tabIndex] [size] [buttonColor] [accent]
                Application.EnableVisualStyles();
                SettingsForm.SavePreview(args[1], args.Length > 2 ? args[2] : "system", args.Length > 3 ? int.Parse(args[3]) : 0,
                    args.Length > 4 ? args[4] : "medium", args.Length > 5 ? args[5] : "", args.Length > 6 ? args[6] : "");
                return 0;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "QuickCut.SingleInstance", out created))
            {
                if (!created)
                {
                    // Already running: ask the running instance to open its settings.
                    Native.AllowSetForegroundWindow(-1);
                    Native.PostMessage((IntPtr)0xFFFF, App.ShowSettingsMsg, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
            }
            return 0;
        }
    }

    class App : ApplicationContext
    {
        public static App Instance;
        public static readonly uint ShowSettingsMsg = Native.RegisterWindowMessage("QuickCut.ShowSettings");
        const int HotkeyId = 1;

        volatile Config cfg; // also read by the mouse hook thread
        readonly NotifyIcon tray;
        readonly RingForm ring;
        readonly MessageWindow msgWin;
        SettingsForm settings;
        MouseHook mouseHook;
        string activeHotkey;
        // Screen area of the settings window (a boxed Rectangle), or null while it isn't showing.
        // A copy for the mouse hook thread, which must not touch the form itself.
        volatile object settingsArea;

        public App()
        {
            Instance = this;
            Autostart.Repair();
            bool firstRun;
            cfg = Config.Load(out firstRun);
            ring = new RingForm(cfg);
            msgWin = new MessageWindow(this);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(Str.MenuOpenRing, null, delegate { ring.ShowRing(); });
            menu.Items.Add(Str.MenuSettings, null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Str.MenuExit, null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = Art.CreateIcon(SystemInformation.SmallIconSize.Width);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenSettings(); };
            tray.Visible = true;

            if (!RegisterHotkey(cfg.Hotkey))
                Notify(string.Format(Str.HotkeyInUse, cfg.Hotkey), true);
            else if (firstRun)
                Notify(string.Format(Str.TrayRunning, cfg.Hotkey), false);
            else if (cfg.LaunchNotice)
            {
                // Show it once the app is up and idle, so it never delays startup.
                EventHandler once = null;
                once = delegate { Application.Idle -= once; LaunchToast.Show(cfg); };
                Application.Idle += once;
            }
            UpdateMouseHook();
            UpdateTrayText();
        }

        public void OnHotkey()
        {
            if (ring.Visible) { ring.HideRing(); return; }
            uint mods; Keys key;
            KeyCombo.TryParse(activeHotkey, out mods, out key);
            // With Alt or Win in the combo, letting go of it would open the app's menu bar or the
            // Start menu. An unassigned key press in between tells Windows it was part of a combo.
            if ((mods & (KeyCombo.MOD_ALT | KeyCombo.MOD_WIN)) != 0)
            {
                Native.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
                Native.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
            }
            ring.ShowRing((int)key);
        }

        void UpdateMouseHook()
        {
            if (cfg.MouseTrigger && mouseHook == null)
                mouseHook = new MouseHook(ring, delegate(System.Drawing.Point pt)
                {
                    // Not inside the settings window (right-clicks there are for text fields etc.),
                    // but everywhere else, so you can try your changes while settings stays open.
                    object area = settingsArea;
                    bool overSettings = area != null && ((System.Drawing.Rectangle)area).Contains(pt);
                    return !overSettings && !(cfg.SkipFullscreen && MouseHook.ForegroundIsFullscreen());
                });
            else if (!cfg.MouseTrigger && mouseHook != null)
            {
                mouseHook.Dispose();
                mouseHook = null;
            }
        }

        public bool RegisterHotkey(string combo)
        {
            uint mods; Keys key;
            if (!KeyCombo.TryParse(combo, out mods, out key)) return false;
            Native.UnregisterHotKey(msgWin.Handle, HotkeyId);
            bool ok = Native.RegisterHotKey(msgWin.Handle, HotkeyId, mods | KeyCombo.MOD_NOREPEAT, (uint)key);
            if (ok) activeHotkey = combo;
            return ok;
        }

        // While the user records a new hotkey, the old one must not swallow the keys.
        public void SuspendHotkey() { Native.UnregisterHotKey(msgWin.Handle, HotkeyId); }
        public void ResumeHotkey() { if (activeHotkey != null) RegisterHotkey(activeHotkey); }

        // True when the configured hotkey is actually registered with Windows.
        public bool HotkeyActive { get { return activeHotkey != null && activeHotkey == cfg.Hotkey; } }

        // Saves and applies everything. A hotkey that Windows refuses doesn't block saving:
        // it's reported in hotkeyError and the other changes still go through.
        public bool ApplyConfig(Config newCfg, out string saveError, out string hotkeyError)
        {
            saveError = hotkeyError = null;
            if (newCfg.Hotkey != activeHotkey)
            {
                string old = activeHotkey;
                if (!RegisterHotkey(newCfg.Hotkey))
                {
                    activeHotkey = null;
                    if (old != null) RegisterHotkey(old); // keep the previous one working
                    hotkeyError = string.Format(Str.HotkeyUnavailable, newCfg.Hotkey);
                }
            }
            cfg = newCfg;
            try { cfg.Save(); }
            catch (Exception ex) { saveError = string.Format(Str.SaveFailed, ex.Message); return false; }
            ring.SetConfig(cfg);
            UpdateMouseHook();
            UpdateTrayText();
            return true;
        }

        public void OpenSettings()
        {
            ring.HideRing();
            if (settings == null)
            {
                settings = new SettingsForm(this, cfg);
                EventHandler track = delegate { UpdateSettingsArea(); };
                settings.Move += track;
                settings.Resize += track;
                settings.VisibleChanged += track;
                settings.FormClosed += delegate { settings.Dispose(); settings = null; UpdateSettingsArea(); ResumeHotkey(); };
                settings.Show();
            }
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
            UpdateSettingsArea();
        }

        void UpdateSettingsArea()
        {
            settingsArea = settings != null && settings.Visible && settings.WindowState != FormWindowState.Minimized
                ? (object)settings.Bounds : null;
        }

        public void Notify(string text, bool warning)
        {
            tray.ShowBalloonTip(4000, "QuickCut", text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        void UpdateTrayText()
        {
            string t = "QuickCut - " + cfg.Hotkey;
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        protected override void ExitThreadCore()
        {
            Native.UnregisterHotKey(msgWin.Handle, HotkeyId);
            if (mouseHook != null) mouseHook.Dispose();
            tray.Visible = false;
            tray.Dispose();
            if (settings != null) settings.Close();
            ring.Close();
            msgWin.DestroyHandle();
            base.ExitThreadCore();
        }
    }

    // Invisible top-level window that receives the global hotkey and the "show settings" broadcast.
    class MessageWindow : NativeWindow
    {
        readonly App app;
        public MessageWindow(App app) { this.app = app; CreateHandle(new CreateParams()); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) app.OnHotkey();
            else if ((uint)m.Msg == App.ShowSettingsMsg) app.OpenSettings();
            base.WndProc(ref m);
        }
    }

    // Windows only launches a Run entry at login when Explorer's StartupApproved list marks it as
    // enabled (first byte 0x02; 0x03 = disabled in Task Manager). Newer Windows builds skip entries
    // that are missing from that list, so both values have to be written.
    //
    // The Store (MSIX) version can't use this: a packaged app's registry writes go to its own private
    // copy, which Explorer never reads. There it uses the package's startup task instead.
    static class Autostart
    {
        const string Name = "QuickCut";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

        // True when running from an installed package (the Store version).
        public static readonly bool Packaged = IsPackaged();

        static bool IsPackaged()
        {
            try
            {
                int len = 0;
                return GetCurrentPackageFullName(ref len, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
            }
            catch { return false; } // Windows 7: no package support at all
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);

        // Turned off by the user in Task Manager (Store version only): only they can turn it back on there.
        public static bool BlockedByUser()
        {
            if (!Packaged) return false;
            try { return StartupTaskWin.BlockedByUser(); } catch { return false; }
        }

        public static bool IsEnabled()
        {
            if (Packaged)
            {
                try { return StartupTaskWin.IsEnabled(); } catch { return false; }
            }
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                if (k == null || k.GetValue(Name) == null) return false;
            byte[] state = ApprovedState();
            return state == null || state.Length == 0 || (state[0] & 1) == 0; // odd = disabled
        }

        public static void Set(bool on)
        {
            if (Packaged)
            {
                try { StartupTaskWin.Set(on); } catch { }
                return;
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(Name, Command);
                else k.DeleteValue(Name, false);
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ApprovedKey))
            {
                if (on) k.SetValue(Name, new byte[12] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                else k.DeleteValue(Name, false);
            }
        }

        // Called at launch: if autostart is on, keep the path current (the exe may have moved) and
        // add the missing approval. Never overrides a "disabled" the user set in Task Manager.
        public static void Repair()
        {
            if (Packaged) return; // the startup task always points at the installed app
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null || k.GetValue(Name) == null) return;
                    if (!string.Equals(k.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase))
                        k.SetValue(Name, Command);
                }
                if (ApprovedState() == null) Set(true);
            }
            catch { } // registry not writable: nothing useful to do
        }

        static byte[] ApprovedState()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                return k == null ? null : k.GetValue(Name) as byte[];
        }
    }

    // The WinRT part, kept apart so the plain exe never loads it (and works on Windows 7).
    // TaskId must match the startupTask entry in the package manifest.
    static class StartupTaskWin
    {
        const string TaskId = "QuickCutStartup";

        static Windows.ApplicationModel.StartupTask Task()
        {
            return MediaWin.Wait(Windows.ApplicationModel.StartupTask.GetAsync(TaskId));
        }

        public static bool IsEnabled()
        {
            var s = Task().State;
            return s == Windows.ApplicationModel.StartupTaskState.Enabled
                || s == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
        }

        public static bool BlockedByUser()
        {
            return Task().State == Windows.ApplicationModel.StartupTaskState.DisabledByUser;
        }

        public static void Set(bool on)
        {
            var t = Task();
            if (on) MediaWin.Wait(t.RequestEnableAsync()); // does nothing if the user turned it off in Task Manager
            else t.Disable();
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, string l);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint to, bool on);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        // Scale factor (1.0 = 100%) of the monitor that contains the point.
        public static float ScaleAt(System.Drawing.Point p)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(new POINT(p.X, p.Y), 2 /*nearest*/), 0 /*effective*/, out x, out y) == 0) return x / 96f;
            }
            catch { } // shcore.dll missing (Windows 7)
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint attrs, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("shell32.dll", EntryPoint = "#727")]
        public static extern int SHGetImageList(int list, ref Guid riid, out IntPtr ppv);
        [DllImport("comctl32.dll")] public static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, int flags);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    }
}

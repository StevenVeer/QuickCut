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
            if (args.Length >= 2 && args[0] == "--preview")
            {
                RingForm.SavePreview(Config.CreateDefault(), args[1], args.Length > 2 ? int.Parse(args[2]) : 0);
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

        Config cfg;
        readonly NotifyIcon tray;
        readonly RingForm ring;
        readonly MessageWindow msgWin;
        SettingsForm settings;
        MouseHook mouseHook;
        string activeHotkey;

        public App()
        {
            Instance = this;
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
            UpdateMouseHook();
            UpdateTrayText();
        }

        public void OnHotkey()
        {
            if (ring.Visible) { ring.HideRing(); return; }
            uint mods; Keys key;
            KeyCombo.TryParse(activeHotkey, out mods, out key);
            ring.ShowRing((int)key);
        }

        void UpdateMouseHook()
        {
            if (cfg.MouseTrigger && mouseHook == null)
                mouseHook = new MouseHook(ring, delegate { return settings == null && !(cfg.SkipFullscreen && MouseHook.ForegroundIsFullscreen()); });
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

        public bool ApplyConfig(Config newCfg, bool autostart, out string error)
        {
            error = null;
            if (newCfg.Hotkey != activeHotkey)
            {
                string old = activeHotkey;
                if (!RegisterHotkey(newCfg.Hotkey))
                {
                    if (old != null) RegisterHotkey(old);
                    error = string.Format(Str.HotkeyUnavailable, newCfg.Hotkey);
                    return false;
                }
            }
            cfg = newCfg;
            try { cfg.Save(); }
            catch (Exception ex) { error = string.Format(Str.SaveFailed, ex.Message); return false; }
            ring.SetConfig(cfg);
            UpdateMouseHook();
            if (autostart != Autostart.IsEnabled()) Autostart.Set(autostart);
            UpdateTrayText();
            return true;
        }

        public void OpenSettings()
        {
            ring.HideRing();
            if (settings == null)
            {
                settings = new SettingsForm(this, cfg);
                settings.FormClosed += delegate { settings.Dispose(); settings = null; ResumeHotkey(); };
                settings.Show();
            }
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
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

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("QuickCut") != null;
        }

        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("QuickCut", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("QuickCut", false);
            }
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
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
        [DllImport("shell32.dll", EntryPoint = "#727")]
        public static extern int SHGetImageList(int list, ref Guid riid, out IntPtr ppv);
        [DllImport("comctl32.dll")] public static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, int flags);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    }
}

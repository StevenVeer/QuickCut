using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("QuickCut")]
[assembly: System.Reflection.AssemblyProduct("QuickCut")]
[assembly: System.Reflection.AssemblyVersion(QuickCut.Program.Version)]
[assembly: System.Reflection.AssemblyFileVersion(QuickCut.Program.Version)]

namespace QuickCut
{
    static class Program
    {
        // The one place to change the version. The exe, the bug report link in Settings and the Store
        // package (build-store.cmd stamps it into the manifest) all take it from here.
        // Store rule: raise it for every Store update, and the last number must stay 0.
        public const string Version = "1.1.0.0";

        [STAThread]
        static int Main(string[] args)
        {
            // Build helpers: generate the exe icon / render a preview image of the ring.
            if (args.Length == 2 && args[0] == "--make-icon") { Art.WriteIco(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--make-store-logos") { Art.WriteStoreLogos(args[1]); return 0; }
            if (args.Length == 3 && args[0] == "--stamp-manifest") return StampManifest(args[1], args[2]) ? 0 : 1;
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
                // Errors nobody caught: logged instead of WinForms' "Unhandled exception" dialog, which
                // means nothing to a user and offers to quit. A tray app is better off carrying on.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { CrashLog.Report(e.Exception); };
                // Other threads: the app ends anyway, but the log says why.
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    CrashLog.Write("crash", e.ExceptionObject as Exception);
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
            }
            return 0;
        }

        // Build helper for build-store.cmd: copies the package manifest and puts Version in its
        // <Identity Version="...">, so the Store package can't drift from the exe. False when the
        // manifest has no such attribute or the version breaks the Store's "last number is 0" rule.
        static bool StampManifest(string source, string dest)
        {
            if (!Version.EndsWith(".0")) return false;
            string xml = System.IO.File.ReadAllText(source);
            System.Text.RegularExpressions.Regex attr = new System.Text.RegularExpressions.Regex("(<Identity\\b[^>]*?\\sVersion=\")[^\"]*(\")");
            if (!attr.IsMatch(xml)) return false;
            xml = attr.Replace(xml, "${1}" + Version + "${2}", 1);
            System.IO.File.WriteAllText(dest, xml, new System.Text.UTF8Encoding(false));
            return true;
        }
    }

    // Writes errors to %APPDATA%\QuickCut\crash.log, so a bug report can include what went wrong.
    // It only ever stays on this PC. Never throws: logging must not cause a second error.
    static class CrashLog
    {
        const long MaxSize = 256 * 1024; // past this the log starts over, so it can't grow forever
        static readonly object gate = new object();
        static bool told;

        public static string FilePath { get { return System.IO.Path.Combine(Config.Folder, "crash.log"); } }

        public static void Write(string where, Exception ex)
        {
            try { WriteTo(FilePath, where, ex); } catch { }
        }

        public static void WriteTo(string path, string where, Exception ex)
        {
            string entry = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + where + "  (QuickCut " +
                typeof(CrashLog).Assembly.GetName().Version + ", " + Environment.OSVersion.VersionString + ")" +
                Environment.NewLine + (ex != null ? ex.ToString() : "(no details)") + Environment.NewLine + Environment.NewLine;
            lock (gate)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.FileInfo fi = new System.IO.FileInfo(path);
                if (fi.Exists && fi.Length > MaxSize) System.IO.File.WriteAllText(path, entry);
                else System.IO.File.AppendAllText(path, entry);
            }
        }

        // An error on the UI thread: log it and, once per run, say so with a tray notice.
        public static void Report(Exception ex)
        {
            Write("error", ex);
            if (told || App.Instance == null) return;
            told = true;
            try { App.Instance.Notify(string.Format(Str.CrashNotice, FilePath), true); } catch { }
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
            ThreadPool.QueueUserWorkItem(delegate { Autostart.Repair(); }); // runs schtasks.exe: not on the UI thread
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

    // "Start with Windows" for the plain exe: a Task Scheduler task that runs at sign-in. Any user can
    // create one for themselves, no admin needed.
    //
    // Earlier versions used the HKCU Run key. A copy started from inside a packaged app (the Claude
    // desktop app, for one) writes HKCU to that app's private copy, which Explorer never reads, so the
    // entry looks fine there but never runs. Task Scheduler is a system service, so a task is real
    // wherever it's created from. Old Run entries are moved over to a task at launch (see Repair).
    //
    // The Store (MSIX) version uses the package's startup task instead (see StartupTaskWin).
    static class Autostart
    {
        const string Name = "QuickCut";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        // Task names are shared by everyone on the PC, so each user gets their own (by SID, which
        // survives renaming the account).
        static string UserSid { get { return System.Security.Principal.WindowsIdentity.GetCurrent().User.Value; } }
        static string TaskName { get { return Name + "-" + UserSid; } }

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
            string xml = QueryTask();
            return xml != null && !TaskDisabled(xml);
        }

        public static void Set(bool on)
        {
            if (Packaged)
            {
                try { StartupTaskWin.Set(on); } catch { }
                return;
            }
            try
            {
                if (on) RegisterTask(true);
                else Schtasks("/delete /tn \"" + TaskName + "\" /f");
                RemoveRunEntry(); // from an older version; would otherwise start a second copy
            }
            catch { }
        }

        // Called at launch, off the UI thread (it runs schtasks.exe). Moves an old Run entry over to a
        // task, and keeps the task's path current (the exe may have moved). Keeps "off" off: a Run
        // entry disabled in Task Manager or a task disabled in Task Scheduler isn't turned back on.
        public static void Repair()
        {
            if (Packaged) return; // the startup task always points at the installed app
            try
            {
                bool hadRun;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    hadRun = k != null && k.GetValue(Name) != null;
                if (hadRun)
                {
                    byte[] state = ApprovedState();
                    bool off = state != null && state.Length > 0 && (state[0] & 1) != 0; // odd = disabled
                    if (off || RegisterTask(true)) RemoveRunEntry(); // only drop it once the task exists
                    return;
                }
                string xml = QueryTask();
                string exe = System.Security.SecurityElement.Escape(Application.ExecutablePath);
                if (xml != null && xml.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0)
                    RegisterTask(!TaskDisabled(xml));
            }
            catch { } // Task Scheduler or registry not available: nothing useful to do
        }

        static bool TaskDisabled(string xml) { return xml.Contains("<Enabled>false</Enabled>"); }

        static string QueryTask()
        {
            string output;
            return Schtasks("/query /tn \"" + TaskName + "\" /xml", out output) == 0 ? output : null;
        }

        static bool RegisterTask(bool enabled)
        {
            string exe = Application.ExecutablePath;
            Func<string, string> esc = System.Security.SecurityElement.Escape;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                  "<RegistrationInfo><Description>Starts QuickCut when you sign in.</Description></RegistrationInfo>" +
                  "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + UserSid + "</UserId></LogonTrigger></Triggers>" +
                  "<Principals><Principal id=\"Author\"><UserId>" + UserSid + "</UserId>" +
                    "<LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                  "<Settings>" +
                    "<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                    // Task Scheduler's defaults would skip it on battery and stop it after 3 days.
                    "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                    "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                    "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" +
                    "<Priority>4</Priority>" + // normal; the default (7) is below normal and would slow the ring
                    "<Enabled>" + (enabled ? "true" : "false") + "</Enabled>" +
                  "</Settings>" +
                  "<Actions Context=\"Author\"><Exec><Command>" + esc(exe) + "</Command>" +
                    "<WorkingDirectory>" + esc(System.IO.Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions>" +
                "</Task>";
            string file = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
                return Schtasks("/create /tn \"" + TaskName + "\" /xml \"" + file + "\" /f") == 0;
            }
            finally { try { System.IO.File.Delete(file); } catch { } }
        }

        static int Schtasks(string args) { string ignored; return Schtasks(args, out ignored); }

        static int Schtasks(string args, out string output)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                System.IO.Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                p.ErrorDataReceived += delegate { }; // drained so a full stderr pipe can't block it
                p.BeginErrorReadLine();
                output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return -1; }
                return p.ExitCode;
            }
        }

        static void RemoveRunEntry()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null) k.DeleteValue(Name, false);
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                if (k != null) k.DeleteValue(Name, false);
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
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
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

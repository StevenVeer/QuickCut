using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickCut
{
    // "If the app is already open, go to it": finds a visible window of the program and brings it forward.
    static class WindowSwitcher
    {
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

        static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        // Brings the program's window forward. False when it isn't running (or has no window).
        public static bool TrySwitch(string target)
        {
            string exe = ExeFor(target);
            if (exe == null) return false;
            IntPtr h = FindWindowOf(exe);
            if (h == IntPtr.Zero) return false;
            if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
            Focus(h);
            return true;
        }

        // The .exe a target starts: the file itself, or the target of a shortcut.
        static string ExeFor(string target)
        {
            try
            {
                string path = Actions.ExpandPath(target);
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) path = ShortcutTarget(path);
                return path != null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? Path.GetFullPath(path) : null;
            }
            catch { return null; }
        }

        static string ShortcutTarget(string lnk)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return null;
            object shell = Activator.CreateInstance(t);
            try
            {
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                try { return sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string; }
                finally { Marshal.ReleaseComObject(sc); }
            }
            finally { Marshal.ReleaseComObject(shell); }
        }

        // Top-most normal window (in z-order) whose process runs this exe.
        static IntPtr FindWindowOf(string exe)
        {
            IntPtr found = IntPtr.Zero;
            Dictionary<uint, string> paths = new Dictionary<uint, string>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero || GetWindowTextLength(h) == 0) return true;
                if ((GetWindowLong(h, -20 /*GWL_EXSTYLE*/) & 0x80 /*WS_EX_TOOLWINDOW*/) != 0) return true;
                int cloaked;
                if (DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out cloaked, 4) == 0 && cloaked != 0) return true; // hidden app windows
                StringBuilder cls = new StringBuilder(64);
                GetClassName(h, cls, cls.Capacity);
                if (Array.IndexOf(ShellClasses, cls.ToString()) >= 0) return true; // the desktop and taskbar
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string p;
                if (!paths.TryGetValue(pid, out p)) paths[pid] = p = ProcessPath(pid);
                if (p != null && string.Equals(p, exe, StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static string ProcessPath(uint pid)
        {
            IntPtr hp = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
            if (hp == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(hp, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(hp); }
        }

        static void Focus(IntPtr h)
        {
            // Same trick as the ring: borrow the active window's input queue so Windows allows the switch.
            IntPtr fg = Native.GetForegroundWindow();
            uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, IntPtr.Zero);
            uint me = Native.GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
            Native.SetForegroundWindow(h);
            if (attached) Native.AttachThreadInput(me, fgThread, false);
        }
    }
}

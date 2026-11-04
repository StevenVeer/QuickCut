using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QuickCut
{
    // Global low-level mouse hook. Only active when the "double right-click" trigger is on.
    // Windows routes every mouse event in the system through the thread that owns the hook, so it
    // runs on a thread of its own: when the UI thread is busy (drawing, starting a program, the
    // settings window), the mouse never stutters and Windows never drops the hook for being slow.
    // The callback does almost nothing (a few comparisons) and hands real work to the UI thread.
    class MouseHook : IDisposable
    {
        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_MBUTTONDOWN = 0x207;
        const uint LLMHF_INJECTED = 1, WM_QUIT = 0x12;

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public Native.POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public Native.POINT pt; }
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);

        readonly HookProc proc; // keep a reference so the GC doesn't collect the delegate
        readonly Thread thread;
        readonly RingForm ring;
        readonly Func<Point, bool> allowed; // may the ring open at this point? Called on the hook thread.
        IntPtr hook;
        uint threadId;
        // Only touched on the hook thread:
        uint lastDownTime;
        Point lastDownPt;
        bool swallowUp;

        public MouseHook(RingForm ring, Func<Point, bool> allowed)
        {
            this.ring = ring;
            this.allowed = allowed;
            proc = Callback;
            using (ManualResetEvent ready = new ManualResetEvent(false))
            {
                thread = new Thread(delegate() { Loop(ready); });
                thread.Name = "QuickCut mouse hook";
                thread.IsBackground = true;
                thread.Priority = ThreadPriority.AboveNormal; // the system's mouse input waits on this thread
                thread.Start();
                ready.WaitOne();
            }
        }

        void Loop(ManualResetEvent ready)
        {
            MSG m;
            PeekMessage(out m, IntPtr.Zero, 0, 0, 0); // creates the message queue, so WM_QUIT can be posted
            threadId = Native.GetCurrentThreadId();
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            ready.Set();
            if (hook == IntPtr.Zero) return;
            // Windows calls the hook while this thread waits in GetMessage, so it just waits until told to quit.
            while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            // Windows calls this from native code: an exception escaping here would end the whole app
            // without a word. On any error the click just goes through as if QuickCut weren't there.
            try
            {
                if (code >= 0)
                {
                    int msg = (int)wParam;
                    if (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP || msg == WM_LBUTTONDOWN || msg == WM_MBUTTONDOWN)
                    {
                        MSLLHOOKSTRUCT s = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                        if ((s.flags & LLMHF_INJECTED) == 0 && Handle(msg, new Point(s.pt.X, s.pt.Y), s.time))
                            return (IntPtr)1; // swallow
                    }
                }
            }
            catch (Exception ex)
            {
                swallowUp = false; // the press went through, so its release must too
                CrashLog.Write("mouse hook", ex);
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        bool Handle(int msg, Point pt, uint time)
        {
            if (msg == WM_RBUTTONUP)
            {
                if (!swallowUp) return false;
                swallowUp = false;
                ring.BeginInvoke((Action)ring.EndHold);
                return true;
            }

            if (ring.IsOpen)
            {
                // Click outside the ring closes it, even if the ring never got focus.
                if (!ring.ContainsScreenPoint(pt)) ring.BeginInvoke((Action)ring.HideRing);
                lastDownTime = 0;
                return false;
            }

            if (msg != WM_RBUTTONDOWN) return false;

            Size dbl = SystemInformation.DoubleClickSize;
            bool isDouble = lastDownTime != 0 && time - lastDownTime <= GetDoubleClickTime()
                && Math.Abs(pt.X - lastDownPt.X) <= dbl.Width && Math.Abs(pt.Y - lastDownPt.Y) <= dbl.Height;
            if (!isDouble) { lastDownTime = time; lastDownPt = pt; return false; }

            lastDownTime = 0;
            if (!allowed(pt)) return false;
            swallowUp = true;
            ring.BeginInvoke((Action)delegate { ring.ShowRing(RingForm.HoldMouse); });
            return true;
        }

        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;
        const int GWL_STYLE = -16, WS_CAPTION = 0x00C00000;

        // True when a game or video covers the whole screen (we don't pop up then).
        // Runs on the mouse hook thread: cheap, and never throws (a failed check just allows the ring).
        public static bool ForegroundIsFullscreen()
        {
            try
            {
                // Windows' own signal for exclusive-fullscreen games and presentation mode, which the
                // window's size doesn't always show.
                int state;
                if (SHQueryUserNotificationState(out state) == 0
                    && (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE)) return true;
                return IsFullscreenWindow(Native.GetForegroundWindow());
            }
            catch { return false; }
        }

        // A window that covers its whole monitor and isn't just maximized. With an auto-hiding taskbar
        // the work area is the whole screen, so every maximized window covers the monitor too (its
        // borders even stick out past the edges). Those are normal windows, told apart by a title bar
        // or by sticking out. Fullscreen video and borderless games fit the monitor exactly and have
        // no title bar (browsers drop theirs when going fullscreen).
        static bool IsFullscreenWindow(IntPtr w)
        {
            if (w == IntPtr.Zero) return false;
            StringBuilder cls = new StringBuilder(64);
            Native.GetClassName(w, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            Native.RECT r;
            if (!Native.GetWindowRect(w, out r)) return false;
            Rectangle b = Screen.FromHandle(w).Bounds;
            if (r.L > b.Left || r.T > b.Top || r.R < b.Right || r.B < b.Bottom) return false; // doesn't cover it
            if (!IsZoomed(w)) return true;
            bool titleBar = (GetWindowLong(w, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
            bool sticksOut = r.L < b.Left || r.T < b.Top || r.R > b.Right || r.B > b.Bottom;
            return !titleBar && !sticksOut;
        }

        public void Dispose()
        {
            if (thread.IsAlive && PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero)) thread.Join(1000);
        }
    }
}

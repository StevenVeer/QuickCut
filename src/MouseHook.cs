using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace QuickCut
{
    // Global low-level mouse hook. Only active when the "double right-click" trigger is on.
    // The callback does almost nothing (a few comparisons) and hands real work to the UI thread.
    class MouseHook : IDisposable
    {
        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_MBUTTONDOWN = 0x207;
        const uint LLMHF_INJECTED = 1;

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public Native.POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern uint GetDoubleClickTime();

        readonly HookProc proc; // keep a reference so the GC doesn't collect the delegate
        readonly IntPtr hook;
        readonly RingForm ring;
        readonly Func<bool> allowed;
        uint lastDownTime;
        Point lastDownPt;
        bool swallowUp;

        public MouseHook(RingForm ring, Func<bool> allowed)
        {
            this.ring = ring;
            this.allowed = allowed;
            proc = Callback;
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
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

            if (ring.Visible)
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
            if (!allowed()) return false;
            swallowUp = true;
            ring.BeginInvoke((Action)delegate { ring.ShowRing(RingForm.HoldMouse); });
            return true;
        }

        // True when a game or video covers the whole screen (we don't pop up then).
        public static bool ForegroundIsFullscreen()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            StringBuilder cls = new StringBuilder(64);
            Native.GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            Rectangle b = Screen.FromHandle(fg).Bounds;
            return r.L <= b.Left && r.T <= b.Top && r.R >= b.Right && r.B >= b.Bottom;
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        }
    }
}

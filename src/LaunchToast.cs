using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace QuickCut
{
    // Small "QuickCut started" notice in the bottom-right corner. Lives for about 3 seconds,
    // then closes and disposes itself: nothing stays behind once it's gone. Click to dismiss.
    class LaunchToast : Form
    {
        const int FadeInMs = 150, VisibleMs = 2500, FadeOutMs = 250;

        readonly Palette pal;
        readonly float sc;
        readonly int w, h;
        readonly Font titleFont, subFont;
        readonly Timer timer;
        readonly Stopwatch clock = new Stopwatch(); // high-res; DateTime.UtcNow only ticks every ~15 ms
        Bitmap frame;

        public static void Show(Config cfg)
        {
            try { new LaunchToast(cfg).Show(); }
            catch { } // a notice is never worth crashing the app over
        }

        LaunchToast(Config cfg)
        {
            pal = Palette.Get(cfg);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            sc = Native.ScaleAt(new Point(area.Right - 1, area.Bottom - 1));
            w = S(240); h = S(62);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(area.Right - w - S(12), area.Bottom - h - S(12), w, h);

            titleFont = new Font("Segoe UI Semibold", 13f * sc, GraphicsUnit.Pixel);
            subFont = new Font("Segoe UI", 12f * sc, GraphicsUnit.Pixel);
            frame = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            timer = new Timer();
            timer.Interval = 15; // ~60 fps so the bar glides instead of stepping
            timer.Tick += delegate { Tick(); };
            Click += delegate { Close(); };
        }

        int S(float v) { return (int)Math.Round(v * sc); }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // layered | tool window (no Alt+Tab) | topmost | never takes focus
                cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            clock.Start(); // count from when we're on screen, so a busy startup can't eat the fade-in
            Tick();
            timer.Start();
        }

        void Tick()
        {
            double t = clock.Elapsed.TotalMilliseconds;
            if (t >= FadeInMs + VisibleMs + FadeOutMs) { Close(); return; }

            double a = t < FadeInMs ? t / FadeInMs
                     : t > FadeInMs + VisibleMs ? 1 - (t - FadeInMs - VisibleMs) / FadeOutMs
                     : 1;
            double left = Math.Max(0, 1 - t / (FadeInMs + VisibleMs)); // share of the countdown bar
            Draw((float)left);
            Present((byte)Math.Max(0, Math.Min(255, a * 255)));
        }

        void Draw(float barShare)
        {
            float pw = Math.Max(1, sc), inset = pw / 2; // keep the whole border inside the bitmap
            using (Graphics g = Graphics.FromImage(frame))
            using (GraphicsPath shape = Rounded(new RectangleF(inset, inset, w - pw, h - pw), S(8)))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // ClearType like normal Windows text (same fix as the ring's panels). Works because the
                // card underneath is fully opaque; the grey AntiAlias smoothing looked soft and blurry.
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                using (SolidBrush b = new SolidBrush(pal.Card)) g.FillPath(b, shape);

                using (SolidBrush acc = new SolidBrush(pal.Accent))
                {
                    // Accent edge: fill the card's own anti-aliased outline, cut by a straight,
                    // pixel-aligned rectangle. Clipping to the rounded path (as before) is aliased
                    // in GDI+, which made the corners look jagged.
                    g.SetClip(new Rectangle(0, 0, S(3), h));
                    g.FillPath(acc, shape);
                    g.ResetClip();

                    // Countdown: a thin pill inset from the edges, over a faint track. Drawn as a
                    // path with a float width so it shrinks smoothly by sub-pixels, and it no longer
                    // overlaps the border or gets chopped by the rounded corners.
                    float bx = S(14), bh = Math.Max(2, S(3)), by = h - S(10), bw = w - S(14) - bx;
                    using (SolidBrush track = new SolidBrush(Color.FromArgb(40, pal.Muted)))
                    using (GraphicsPath tp = Rounded(new RectangleF(bx, by, bw, bh), bh / 2))
                        g.FillPath(track, tp);
                    float fw = bw * barShare;
                    if (fw > 0.5f)
                        using (GraphicsPath fp = Rounded(new RectangleF(bx, by, Math.Max(fw, bh), bh), bh / 2))
                            g.FillPath(acc, fp);
                }

                using (Pen p = new Pen(pal.Border, pw)) g.DrawPath(p, shape);

                using (SolidBrush tx = new SolidBrush(pal.Text)) g.DrawString(Str.ToastStarted, titleFont, tx, S(14), S(9));
                using (SolidBrush mu = new SolidBrush(pal.Muted)) g.DrawString(Str.ToastReady, subFont, mu, S(14), S(29));
            }
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            float d = rad * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Present(byte alpha)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = frame.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBmp);
            try
            {
                Native.SIZE sz = new Native.SIZE(w, h);
                Native.POINT src = new Native.POINT(0, 0), dst = new Native.POINT(Left, Top);
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION { Op = 0, Flags = 0, Alpha = alpha, Format = 1 };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref bf, 2);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            base.OnFormClosed(e); // a closed modeless form is disposed by WinForms right after this
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                titleFont.Dispose();
                subFont.Dispose();
                if (frame != null) { frame.Dispose(); frame = null; }
            }
            base.Dispose(disposing);
        }
    }
}

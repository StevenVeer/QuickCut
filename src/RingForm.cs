using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;

namespace QuickCut
{
    // The round menu. A per-pixel-alpha layered window: no redraw cost while hidden,
    // and it only renders on show / hover change.
    class RingForm : Form
    {
        const int HitNone = -1, HitCenter = -2;
        // How the ring was opened: HoldNone = click mode, HoldMouse = right button held, > 0 = virtual key held.
        public const int HoldNone = 0, HoldMouse = -1;

        Config cfg;
        readonly System.Windows.Forms.Timer holdTimer;
        int holdSource;
        bool holding, moved;
        int openHover;
        readonly float sc;
        readonly int size;
        readonly float cx, cy, rOuter, rInner, rCenter;
        readonly Font labelFont;
        readonly System.Windows.Forms.Timer fadeTimer;
        Bitmap frame;
        Color accent = Art.Brand;
        int hover = HitNone;
        byte alpha = 255;
        IntPtr prevWindow;

        public RingForm(Config cfg)
        {
            this.cfg = cfg;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            size = (int)Math.Round(320 * sc);
            cx = cy = size / 2f;
            rOuter = 148 * sc;
            rInner = 66 * sc;
            rCenter = 57 * sc;
            labelFont = new Font("Segoe UI", 11.5f * sc, GraphicsUnit.Pixel);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            Size = new Size(size, size);

            fadeTimer = new System.Windows.Forms.Timer();
            fadeTimer.Interval = 15;
            fadeTimer.Tick += delegate
            {
                alpha = (byte)Math.Min(255, alpha + 70);
                Apply();
                if (alpha == 255) fadeTimer.Stop();
            };
            // Runs only while a trigger is held down: follows the cursor and watches for the release.
            holdTimer = new System.Windows.Forms.Timer();
            holdTimer.Interval = 15;
            holdTimer.Tick += delegate
            {
                Point c = Cursor.Position;
                SetHover(HitTest(new Point(c.X - Left, c.Y - Top)));
                if (holdSource > 0 && (Native.GetAsyncKeyState(holdSource) & 0x8000) == 0) EndHold();
            };
            IntPtr h = Handle; // create the window now so the first show is instant
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 /*WS_EX_LAYERED*/ | 0x80 /*WS_EX_TOOLWINDOW: not in Alt+Tab*/;
                return cp;
            }
        }

        public void SetConfig(Config c)
        {
            cfg = c;
            Art.ClearIconCache();
        }

        public void ShowRing() { ShowRing(HoldNone); }

        public void ShowRing(int hold)
        {
            if (Visible) HideRing();
            prevWindow = Native.GetForegroundWindow();
            Point m = Cursor.Position;
            Rectangle wa = Screen.FromPoint(m).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(m.X - size / 2, wa.Right - size));
            int y = Math.Max(wa.Top, Math.Min(m.Y - size / 2, wa.Bottom - size));
            Location = new Point(x, y);
            accent = Art.Accent();
            hover = HitTest(new Point(m.X - x, m.Y - y));
            openHover = hover;
            holdSource = hold;
            holding = hold != HoldNone;
            moved = false;
            alpha = 60;
            Render();
            Show();
            TakeFocus();
            fadeTimer.Start();
            if (holding) holdTimer.Start();
        }

        // Windows only lets the active app hand out focus, so borrow its input queue for a moment.
        void TakeFocus()
        {
            IntPtr fg = Native.GetForegroundWindow();
            uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, IntPtr.Zero);
            uint me = Native.GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
            Native.SetForegroundWindow(Handle);
            Activate();
            if (attached) Native.AttachThreadInput(me, fgThread, false);
        }

        // Trigger released: run the button under the cursor, if the user moved to one.
        // Otherwise (a quick tap) the ring stays open and works with clicks.
        public void EndHold()
        {
            if (!holding) return;
            holding = false;
            holdTimer.Stop();
            if (!moved || hover == HitNone) return;
            if (hover == HitCenter) Run(cfg.Center);
            else if (hover < cfg.Items.Count) Run(cfg.Items[hover]);
        }

        public void HideRing()
        {
            fadeTimer.Stop();
            holdTimer.Stop();
            holding = false;
            if (Visible) Hide();
            hover = HitNone;
        }

        public bool ContainsScreenPoint(Point p)
        {
            float dx = p.X - Left - cx, dy = p.Y - Top - cy;
            return dx * dx + dy * dy <= rOuter * rOuter;
        }

        // ---------- input ----------

        int HitTest(Point p)
        {
            float dx = p.X - cx, dy = p.Y - cy;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= rCenter) return HitCenter;
            int n = cfg.Items.Count;
            // While holding, only the direction matters, so overshooting the ring still selects.
            if (n == 0 || d < rInner - 4 * sc || (d > rOuter && !holding)) return HitNone;
            double sweep = 360.0 / n;
            double rel = Math.Atan2(dy, dx) * 180 / Math.PI + 90 + sweep / 2;
            rel = ((rel % 360) + 360) % 360;
            return (int)(rel / sweep) % n;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            SetHover(HitTest(e.Location));
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!holding) SetHover(HitNone);
            base.OnMouseLeave(e);
        }

        void SetHover(int h)
        {
            if (h == hover) return;
            hover = h;
            if (h != openHover) moved = true;
            Cursor = h == HitNone ? Cursors.Default : Cursors.Hand;
            if (Visible) Render();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (holding) { holding = false; holdTimer.Stop(); }
            if (e.Button != MouseButtons.Left) { HideRing(); return; }
            int h = HitTest(e.Location);
            if (h == HitCenter) Run(cfg.Center);
            else if (h >= 0) Run(cfg.Items[h]);
            else HideRing();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Run(new RingItem(ActionType.Media, "", e.Delta > 0 ? "volup" : "voldown"));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            e.Handled = true;
            if (holding) return; // key repeats of the held hotkey
            Keys k = e.KeyCode;
            int n = -1;
            if (k >= Keys.D1 && k <= Keys.D9) n = k - Keys.D1;
            else if (k >= Keys.NumPad1 && k <= Keys.NumPad9) n = k - Keys.NumPad1;
            else if (k == Keys.D0 || k == Keys.NumPad0) n = 9;

            if (k == Keys.Escape) HideRing();
            else if (k == Keys.Space || k == Keys.Enter) Run(cfg.Center);
            else if (n >= 0 && n < cfg.Items.Count) Run(cfg.Items[n]);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideRing();
        }

        void Run(RingItem it)
        {
            // Volume steps keep the ring open so you can click/scroll a few times.
            bool keepOpen = it.Type == ActionType.Media && (it.Target == "volup" || it.Target == "voldown");
            if (!keepOpen) HideRing();
            if (it.Type == ActionType.Keys && prevWindow != IntPtr.Zero)
            {
                // Shortcuts go to the window that was active before the ring opened.
                Native.SetForegroundWindow(prevWindow);
                Thread.Sleep(60);
            }
            try { Actions.Execute(it); }
            catch (Exception ex)
            {
                if (App.Instance != null) App.Instance.Notify("Kon \"" + it.Label + "\" niet uitvoeren:\n" + ex.Message, true);
            }
        }

        // ---------- drawing ----------

        void Render()
        {
            if (frame == null) frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                DrawRing(g);
            }
            Apply();
        }

        void Apply()
        {
            if (frame == null) return;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = frame.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBmp);
            try
            {
                Native.SIZE sz = new Native.SIZE(size, size);
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

        void DrawRing(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int n = cfg.Items.Count;
            float sweep = n > 0 ? 360f / n : 360f;

            // Soft shadow
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(16, 0, 0, 0)))
                for (int i = 1; i <= 5; i++)
                {
                    float r = rOuter + i * 1.8f * sc;
                    g.FillEllipse(sh, cx - r, cy - r + 2 * sc, 2 * r, 2 * r);
                }

            // Ring
            using (GraphicsPath ring = Art.Donut(cx, cy, rOuter, rInner))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 30, 31, 38)))
                g.FillPath(b, ring);
            using (Pen rim = new Pen(Color.FromArgb(45, 255, 255, 255), sc))
            {
                g.DrawEllipse(rim, cx - rOuter, cy - rOuter, 2 * rOuter, 2 * rOuter);
                g.DrawEllipse(rim, cx - rInner, cy - rInner, 2 * rInner, 2 * rInner);
            }

            // Hovered segment
            if (hover >= 0 && n > 0)
            {
                float ro = rOuter - 5 * sc, ri = rInner + 5 * sc;
                float gapDeg = n > 1 ? (float)(4 * sc / ((ro + ri) / 2) * 180 / Math.PI) : 0;
                float start = -90 + hover * sweep - sweep / 2 + gapDeg / 2;
                using (GraphicsPath p = n == 1 ? Art.Donut(cx, cy, ro, ri) : Art.Sector(cx, cy, ro, ri, start, sweep - gapDeg))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(235, accent)))
                    g.FillPath(b, p);
            }

            // Separators
            if (n > 1)
                using (Pen sep = new Pen(Color.FromArgb(40, 255, 255, 255), 1.2f * sc))
                    for (int k = 0; k < n; k++)
                    {
                        if (hover >= 0 && (k == hover || k == (hover + 1) % n)) continue;
                        double a = (-90 - sweep / 2 + k * sweep) * Math.PI / 180;
                        float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                        g.DrawLine(sep, cx + cos * (rInner + 10 * sc), cy + sin * (rInner + 10 * sc),
                                        cx + cos * (rOuter - 10 * sc), cy + sin * (rOuter - 10 * sc));
                    }

            // Segment contents
            float rMid = (rOuter + rInner) / 2;
            float iconSz = (n > 8 ? 24 : 30) * sc;
            float labelW = Math.Min(86 * sc, n > 1 ? (float)(2 * rMid * Math.Sin(Math.PI / n)) - 6 * sc : 86 * sc);
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            using (SolidBrush text = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
            using (SolidBrush textHot = new SolidBrush(Color.White))
            {
                sf.Alignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                for (int i = 0; i < n; i++)
                {
                    double a = (-90 + i * sweep) * Math.PI / 180;
                    float px = cx + (float)Math.Cos(a) * rMid, py = cy + (float)Math.Sin(a) * rMid;
                    bool hot = i == hover;
                    RectangleF ir = new RectangleF(px - iconSz / 2, py - iconSz / 2 - 8 * sc, iconSz, iconSz);
                    Art.DrawItem(g, cfg.Items[i], ir, hot ? Color.White : Color.FromArgb(235, 236, 240));
                    RectangleF lr = new RectangleF(px - labelW / 2, ir.Bottom + 3 * sc, labelW, 16 * sc);
                    g.DrawString(cfg.Items[i].Label, labelFont, hot ? textHot : text, lr, sf);
                }

                // Center button
                bool hc = hover == HitCenter;
                using (SolidBrush cb = new SolidBrush(hc ? accent : Color.FromArgb(245, 44, 46, 56)))
                    g.FillEllipse(cb, cx - rCenter, cy - rCenter, 2 * rCenter, 2 * rCenter);
                using (Pen rim = new Pen(Color.FromArgb(50, 255, 255, 255), sc))
                    g.DrawEllipse(rim, cx - rCenter, cy - rCenter, 2 * rCenter, 2 * rCenter);
                float gs = 34 * sc;
                RectangleF gr = new RectangleF(cx - gs / 2, cy - gs / 2 - 8 * sc, gs, gs);
                Art.DrawItem(g, cfg.Center, gr, Color.White);
                RectangleF clr = new RectangleF(cx - rCenter + 8 * sc, gr.Bottom + 3 * sc, 2 * rCenter - 16 * sc, 16 * sc);
                g.DrawString(cfg.Center.Label, labelFont, textHot, clr, sf);
            }
        }

        // Renders the ring over a grey background into a PNG (used to check the design).
        public static void SavePreview(Config cfg, string path, int hoverIndex)
        {
            using (RingForm f = new RingForm(cfg))
            {
                f.hover = hoverIndex;
                f.frame = new Bitmap(f.size, f.size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(f.frame)) { g.Clear(Color.Transparent); f.DrawRing(g); }
                using (Bitmap outp = new Bitmap(f.size, f.size))
                {
                    using (Graphics g = Graphics.FromImage(outp))
                    {
                        g.Clear(Color.FromArgb(120, 130, 150));
                        g.DrawImage(f.frame, 0, 0);
                    }
                    outp.Save(path, ImageFormat.Png);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fadeTimer.Dispose();
                holdTimer.Dispose();
                labelFont.Dispose();
                if (frame != null) frame.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

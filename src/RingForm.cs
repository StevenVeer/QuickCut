using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace QuickCut
{
    // The round menu. A per-pixel-alpha layered window: no redraw cost while hidden,
    // and it only renders on show / hover change.
    class RingForm : Form
    {
        const int HitNone = RingRenderer.HitNone, HitCenter = RingRenderer.HitCenter;
        // How the ring was opened: HoldNone = click mode, HoldMouse = right button held, > 0 = virtual key held.
        public const int HoldNone = 0, HoldMouse = -1;

        Config cfg;
        Palette pal;
        float dpi; // scale of the monitor the ring was last shown on
        RingRenderer rr;
        int size, height; // height includes the volume panel when it's shown
        readonly System.Windows.Forms.Timer fadeTimer, holdTimer, repeatTimer, mediaTimer, scrollTimer;
        Bitmap ringLayer; // the ring without the panels, reused while only the scrolling title moves
        int volHover = RingRenderer.VolNone;
        int medHover = RingRenderer.MedNone;
        int repeatDir;      // -1 / +1 while - or + is held down
        bool volPressed;    // the current mouse press started on the volume panel
        int holdSource;
        bool holding, moved;
        int openHover;
        Bitmap frame;
        int hover = HitNone;
        byte alpha = 255;
        IntPtr prevWindow;

        public RingForm(Config cfg)
        {
            this.cfg = cfg;
            dpi = Native.ScaleAt(Cursor.Position);
            pal = Palette.Get(cfg);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            RebuildRenderer();

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
            // Holding - or + keeps stepping: a pause first, then a steady pace.
            repeatTimer = new System.Windows.Forms.Timer();
            repeatTimer.Tick += delegate { repeatTimer.Interval = 140; StepVolume(repeatDir); };
            // While the ring is open, what's playing is re-read every second (the read runs off the UI thread).
            mediaTimer = new System.Windows.Forms.Timer();
            mediaTimer.Interval = 1000;
            mediaTimer.Tick += delegate { Media.Refresh(); };
            Media.Changed += OnMediaChanged;
            // Runs only while a too-long title is scrolling; stops itself otherwise.
            scrollTimer = new System.Windows.Forms.Timer();
            scrollTimer.Interval = 15; // Windows rounds this to its 15.6 ms tick: ~64 even frames per second
            scrollTimer.Tick += delegate
            {
                if (Visible && rr.TitleScrolls) { rr.ScrollTick(); Render(true); }
                else scrollTimer.Stop();
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
            bool resized = c.SizeFactor != cfg.SizeFactor || c.VolumeBar != cfg.VolumeBar || c.MediaBar != cfg.MediaBar;
            cfg = c;
            Art.ClearIconCache();
            if (resized) { HideRing(); RebuildRenderer(); }
        }

        // (Re)creates the renderer for the DPI and the chosen ring size.
        void RebuildRenderer()
        {
            if (rr != null) rr.Dispose();
            rr = new RingRenderer(dpi * cfg.SizeFactor, cfg.SizeFactor, cfg.VolumeBar, cfg.MediaBar);
            size = rr.Size;
            height = rr.Height;
            if (frame != null) { frame.Dispose(); frame = null; }
            if (ringLayer != null) { ringLayer.Dispose(); ringLayer = null; }
            Size = new Size(size, height);
        }

        public void ShowRing() { ShowRing(HoldNone); }

        public void ShowRing(int hold)
        {
            if (Visible) HideRing();
            prevWindow = Native.GetForegroundWindow();
            Point m = Cursor.Position;
            // Render at the scale of this monitor, so the ring is sharp on every screen.
            float d = Native.ScaleAt(m);
            if (d != dpi) { dpi = d; RebuildRenderer(); }
            Rectangle wa = Screen.FromPoint(m).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(m.X - size / 2, wa.Right - size));
            int y = Math.Max(wa.Top, Math.Min(m.Y - size / 2, wa.Bottom - height));
            Location = new Point(x, y);
            pal = Palette.Get(cfg); // follows Windows light/dark and accent changes
            hover = HitTest(new Point(m.X - x, m.Y - y));
            openHover = hover;
            volHover = RingRenderer.VolNone;
            medHover = RingRenderer.MedNone;
            holdSource = hold;
            holding = hold != HoldNone;
            moved = false;
            alpha = 60;
            Render();
            Show();
            TakeFocus();
            fadeTimer.Start();
            if (holding) holdTimer.Start();
            if (cfg.MediaBar) { Media.Refresh(); mediaTimer.Start(); }
        }

        // Called from Media's background thread.
        void OnMediaChanged()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke((Action)delegate { if (Visible && cfg.MediaBar) Render(); }); } catch { }
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
            repeatTimer.Stop();
            mediaTimer.Stop();
            scrollTimer.Stop();
            holding = false;
            volPressed = false;
            if (Visible) Hide();
            hover = HitNone;
        }

        public bool ContainsScreenPoint(Point p)
        {
            return rr.Contains(new PointF(p.X - Left, p.Y - Top));
        }

        // ---------- input ----------

        // While holding, only the direction matters, so overshooting the ring still selects.
        int HitTest(Point p) { return rr.HitTest(p, cfg.Items.Count, holding); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            // The volume panel only reacts in click mode; while holding, only the ring's direction counts.
            int v = holding ? RingRenderer.VolNone : rr.HitVolume(e.Location, cfg.VolumeStep);
            bool volChanged = v != volHover;
            volHover = v;
            int m = holding ? RingRenderer.MedNone : rr.HitMedia(e.Location);
            if (m != medHover)
            {
                // Coming onto the panel: a long title starts scrolling from its beginning.
                if (medHover == RingRenderer.MedNone) rr.ResetScroll();
                medHover = m;
                volChanged = true;
            }
            if (!SetHover(HitTest(e.Location)) && volChanged && Visible) Render();
            UpdateCursor();
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!holding)
            {
                bool volChanged = volHover != RingRenderer.VolNone || medHover != RingRenderer.MedNone;
                volHover = RingRenderer.VolNone;
                medHover = RingRenderer.MedNone;
                if (!SetHover(HitNone) && volChanged && Visible) Render();
            }
            base.OnMouseLeave(e);
        }

        // True when it changed (and redrew).
        bool SetHover(int h)
        {
            if (h == hover) return false;
            hover = h;
            if (h != openHover) moved = true;
            UpdateCursor();
            if (Visible) Render();
            return true;
        }

        void UpdateCursor()
        {
            bool hand = hover != HitNone || volHover == RingRenderer.VolDown || volHover == RingRenderer.VolUp || volHover >= 0
                || medHover == RingRenderer.MedPrev || medHover == RingRenderer.MedPlay || medHover == RingRenderer.MedNext;
            Cursor = hand ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (holding || e.Button != MouseButtons.Left) return;
            int med = rr.HitMedia(e.Location);
            if (med != RingRenderer.MedNone)
            {
                volPressed = true; // like the volume panel: acts on press, the ring stays open
                if (med == RingRenderer.MedPrev) Media.Command(Media.Prev);
                else if (med == RingRenderer.MedPlay) Media.Command(Media.PlayPause);
                else if (med == RingRenderer.MedNext) Media.Command(Media.Next);
                return;
            }
            int v = rr.HitVolume(e.Location, cfg.VolumeStep);
            if (v == RingRenderer.VolNone) return;
            volPressed = true;
            if (v == RingRenderer.VolDown || v == RingRenderer.VolUp)
            {
                repeatDir = v == RingRenderer.VolUp ? 1 : -1;
                StepVolume(repeatDir);
                repeatTimer.Interval = 450;
                repeatTimer.Start();
            }
            else if (v >= 0) ClickBlock(v);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            repeatTimer.Stop();
            if (volPressed) { volPressed = false; return; } // the panel acts on press; the ring stays open
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
            StepVolume(e.Delta > 0 ? 1 : -1);
        }

        // ---------- volume ----------

        // One step of the chosen size, snapping to the step grid. Without Core Audio it falls back to
        // the volume keys (2% each).
        void StepVolume(int dir)
        {
            float level; bool muted;
            if (Volume.TryGet(out level, out muted))
                Volume.Set(Volume.NextStep((int)Math.Round(level * 100), cfg.VolumeStep, dir) / 100f);
            else
                for (int i = 0; i < cfg.VolumeStep / 2; i++)
                    Run(new RingItem(ActionType.Media, "", dir > 0 ? "volup" : "voldown"));
            if (Visible) Render();
        }

        // Sets the level to that block; clicking the block the level already sits on mutes.
        void ClickBlock(int index)
        {
            float level; bool muted;
            if (!Volume.TryGet(out level, out muted)) return;
            int target = (index + 1) * cfg.VolumeStep;
            if (!muted && (int)Math.Round(level * 100) == target) Volume.SetMute(true);
            else Volume.Set(target / 100f);
            Render();
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
                if (App.Instance != null) App.Instance.Notify(string.Format(Str.RunFailed, it.Label, ex.Message), true);
            }
        }

        // ---------- drawing ----------

        void Render() { Render(false); }

        // panelsOnly: the ring hasn't changed (a scroll step), so its cached layer is reused.
        void Render(bool panelsOnly)
        {
            if (frame == null) frame = new Bitmap(size, height, PixelFormat.Format32bppArgb);
            if (ringLayer == null) { ringLayer = new Bitmap(size, height, PixelFormat.Format32bppArgb); panelsOnly = false; }
            if (!panelsOnly)
                using (Graphics rg = Graphics.FromImage(ringLayer))
                {
                    rg.Clear(Color.Transparent);
                    rr.Draw(rg, cfg, pal, hover, HitNone, true);
                }
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImageUnscaled(ringLayer, 0, 0);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality; // as rr.Draw sets it
                if (rr.VolumeBar)
                {
                    float level; bool muted;
                    bool ok = Volume.TryGet(out level, out muted);
                    rr.DrawVolume(g, cfg, pal, volHover, ok, level, muted);
                }
                MediaInfo mi = cfg.MediaBar ? Media.Current : null;
                rr.MediaShown = mi != null;
                if (mi == null) medHover = RingRenderer.MedNone;
                rr.Animate = medHover != RingRenderer.MedNone; // a long title scrolls while the mouse is on the panel
                rr.DrawMedia(g, cfg, pal, medHover, mi, false);
            }
            if (rr.TitleScrolls && Visible && !scrollTimer.Enabled) scrollTimer.Start();
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
                Native.SIZE sz = new Native.SIZE(size, height);
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

        // Renders the ring over a grey background into a PNG (used to check the design).
        public static void SavePreview(Config cfg, string path, int hoverIndex)
        {
            using (RingRenderer r = new RingRenderer(1f, 1f, cfg.VolumeBar, cfg.MediaBar))
            using (Bitmap outp = new Bitmap(r.Size, r.Height))
            {
                using (Graphics g = Graphics.FromImage(outp))
                {
                    g.Clear(Color.FromArgb(120, 130, 150));
                    Palette pal = Palette.Get(cfg);
                    r.Draw(g, cfg, pal, hoverIndex, HitNone, true);
                    float level; bool muted;
                    bool ok = Volume.TryGet(out level, out muted);
                    if (cfg.VolumeBar) r.DrawVolume(g, cfg, pal, RingRenderer.VolNone, ok, level, muted);
                    // Read on a background (MTA) thread, like the ring does: some WinRT calls stall on the UI thread.
                    MediaInfo mi = null;
                    Thread t = new Thread(delegate() { try { mi = MediaWin.Fetch(null); } catch { } });
                    t.SetApartmentState(ApartmentState.MTA);
                    t.Start();
                    t.Join(5000);
                    r.DrawMedia(g, cfg, pal, RingRenderer.MedNone, mi, true);
                }
                outp.Save(path, ImageFormat.Png);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fadeTimer.Dispose();
                holdTimer.Dispose();
                repeatTimer.Dispose();
                mediaTimer.Dispose();
                scrollTimer.Dispose();
                if (ringLayer != null) ringLayer.Dispose();
                Media.Changed -= OnMediaChanged;
                rr.Dispose();
                if (frame != null) frame.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

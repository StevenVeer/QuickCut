using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace QuickCut
{
    // Owner-drawn controls so the settings window looks the same in light and dark mode.
    class ThemedControl : Control
    {
        public static float S = 1; // DPI scale, set by the settings window
        protected Palette Pal;
        protected bool Hot;

        public ThemedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        public virtual void SetPalette(Palette p) { Pal = p; Invalidate(); }

        protected Color Back { get { return Parent != null ? Parent.BackColor : Pal.Card; } }

        // The color around this control (its parent's), for controls that set their own BackColor for
        // their children.
        protected virtual Color BackForParent { get { return Back; } }

        protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected Graphics Prepare(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            return g;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected RectangleF Inner { get { return new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f); } }

        // Measures the text without GDI's built-in padding and places it in the exact middle.
        // (TextRenderer's VerticalCenter sits a pixel or two low for most fonts.)
        public static void DrawCentered(Graphics g, string text, Font f, Rectangle r, Color c)
        {
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            Size ts = TextRenderer.MeasureText(g, text, f, new Size(int.MaxValue, int.MaxValue), flags);
            int x = r.X + Math.Max(0, (r.Width - ts.Width) / 2);
            int y = r.Y + (r.Height - ts.Height) / 2;
            TextRenderer.DrawText(g, text, f, new Rectangle(x, y, Math.Min(ts.Width, r.Width), ts.Height), c, flags | TextFormatFlags.EndEllipsis);
        }
    }

    // Rounded panel with the card color; children sit on top.
    class Card : ThemedControl
    {
        public override void SetPalette(Palette p) { BackColor = p.Card; base.SetPalette(p); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Pal.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(Inner, 12 * S))
            {
                using (SolidBrush b = new SolidBrush(Pal.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Pal.Border)) g.DrawPath(pen, p);
            }
        }
    }

    class FlatButton : ThemedControl
    {
        // Normal: a box. Link: text only. Danger: red text only. DangerBox: a box with red text.
        // Accent: filled with the accent color (the main action).
        public enum Kind { Normal, Link, Danger, DangerBox, Accent }
        public Kind Style;
        public bool AlignRight; // links only
        public string IconName; // optional line icon before the text

        public FlatButton(string text, Kind style) { Text = text; Style = style; Cursor = Cursors.Hand; TabStop = true; }

        bool Boxed { get { return Style == Kind.Normal || Style == Kind.DangerBox || Style == Kind.Accent; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            Color fg = !Enabled ? Pal.Muted : Style == Kind.Link ? Pal.AccentText : Style == Kind.Danger || Style == Kind.DangerBox ? Pal.Danger
                : Style == Kind.Accent ? Theme.TextOn(Pal.Accent) : Pal.Text;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                (Boxed ? TextFormatFlags.HorizontalCenter : AlignRight ? TextFormatFlags.Right : TextFormatFlags.Left);
            if (Boxed)
                using (GraphicsPath p = Round(Inner, 9 * S))
                {
                    Color fill = Style == Kind.Accent ? (Hot ? Theme.Mix(Pal.Accent, Color.White, 0.12f) : Pal.Accent)
                        : Hot && Enabled ? Pal.Hover : Pal.Field;
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Focused ? Pal.Text : Style == Kind.Accent ? Pal.Accent : Pal.Border)) g.DrawPath(pen, p);
                }
            Font baseFont = Style == Kind.Accent ? Ui.Font(13, true) : Font;
            using (Font f = Hot && !Boxed ? new Font(baseFont, FontStyle.Underline) : (Font)baseFont.Clone())
            {
                if (IconName != null)
                {
                    // Icon and text centered together (boxed), or from the left (links).
                    int tw = TextRenderer.MeasureText(g, Text, f, new Size(1000, 100), TextFormatFlags.NoPadding).Width;
                    float iw = 15 * S, gap = 6 * S;
                    float x = Boxed ? (Width - (iw + gap + tw)) / 2f : AlignRight ? Width - (iw + gap + tw) : 0;
                    Icons.Get(IconName).Draw(g, new RectangleF(x, (Height - iw) / 2, iw, iw), fg, Style == Kind.Accent ? 2.4f : 2f, false);
                    TextRenderer.DrawText(g, Text, f, new Rectangle((int)(x + iw + gap), 0, tw + 4, Height), fg, Ui.Left);
                }
                else if (Boxed) DrawCentered(g, Text, f, ClientRectangle, fg);
                else TextRenderer.DrawText(g, Text, f, ClientRectangle, fg, flags);
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    class ToggleSwitch : ThemedControl
    {
        bool on;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        public ToggleSwitch() { Cursor = Cursors.Hand; TabStop = true; }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            float h = 24 * S, w = 42 * S;
            Ui.DrawToggle(g, new RectangleF(Width - w - 1, (Height - h) / 2, w, h), on, Pal);
        }
    }

    // Row of mutually exclusive options.
    class Segmented : ThemedControl
    {
        public string[] Options;
        int index;
        int hotIndex = -1;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get { return index; }
            set { if (index == value) return; index = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
        }

        public Segmented(string[] options) { Options = options; Cursor = Cursors.Hand; }

        int At(int x) { return Math.Max(0, Math.Min(Options.Length - 1, x * Options.Length / Math.Max(1, Width))); }
        protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.X); if (h != hotIndex) { hotIndex = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hotIndex = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { SelectedIndex = At(e.X); base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            using (GraphicsPath p = Round(Inner, 9 * S))
            {
                using (SolidBrush b = new SolidBrush(Pal.Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(Pal.Border)) g.DrawPath(pen, p);
            }
            float w = (Width - 1f - 6 * S) / Options.Length;
            for (int i = 0; i < Options.Length; i++)
            {
                RectangleF r = new RectangleF(3 * S + i * w, 3 * S, w, Height - 7 * S);
                Color fg = Pal.Text;
                if (i == index || i == hotIndex)
                    using (GraphicsPath p = Round(r, 7 * S))
                    using (SolidBrush b = new SolidBrush(i == index ? Pal.Accent : Pal.Hover))
                        g.FillPath(b, p);
                if (i == index) fg = Theme.TextOn(Pal.Accent);
                TextRenderer.DrawText(g, Options[i], i == index ? Ui.Font(13, true) : Ui.Font(13, false), Rectangle.Round(r), fg, Ui.Center);
            }
        }
    }

    // Text box with a themed rounded border.
    class FieldBox : ThemedControl
    {
        public readonly TextBox Box;

        public FieldBox(TextBox box)
        {
            Box = box;
            box.BorderStyle = BorderStyle.None;
            Controls.Add(box);
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            Cursor = Cursors.IBeam;
        }

        public override void SetPalette(Palette p)
        {
            base.SetPalette(p);
            Box.BackColor = p.Field;
            Box.ForeColor = p.Text;
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(9 * S);
            Box.SetBounds(pad, (Height - Box.Height) / 2, Width - 2 * pad, Box.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            using (GraphicsPath p = Round(Inner, 9 * S))
            {
                using (SolidBrush b = new SolidBrush(Pal.Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(Box.Focused ? Pal.Accent : Pal.Border, Box.Focused ? 1.5f * S : 1)) g.DrawPath(pen, p);
            }
        }

        // Grey hint text shown while empty.
        public void SetCue(string text) { Native.SendMessage(Box.Handle, 0x1501, (IntPtr)1, text); }
    }

    // The chosen app: its icon, its name and what kind of thing it is. Click to pick another.
    class AppDisplay : ThemedControl
    {
        public string Path = "", Title = "", Kind = "";
        public AppDisplay() { Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            float isz = 36 * S;
            Bitmap icon = Path.Length > 0 ? Art.PathIcon(Path, true) : null;
            RectangleF ir = new RectangleF(0, (Height - isz) / 2, isz, isz);
            if (icon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(icon, ir); }
            else
            {
                using (GraphicsPath p = Round(ir, 9 * S)) using (SolidBrush b = new SolidBrush(Pal.Field)) g.FillPath(b, p);
                Icons.Draw(g, "apps", RectangleF.Inflate(ir, -9 * S, -9 * S), Pal.Muted);
            }
            int tx = (int)(ir.Right + 12 * S), tw = Width - tx;
            bool sub = Kind.Length > 0;
            int th = (int)(20 * S);
            int ty = sub ? (int)(Height / 2 - th) : (Height - th) / 2;
            TextRenderer.DrawText(g, Title, Ui.Font(14, Path.Length > 0), new Rectangle(tx, ty, tw, th), Path.Length > 0 ? Pal.Text : Pal.Muted, Ui.Left);
            if (sub) TextRenderer.DrawText(g, Kind, Ui.Font(12, false), new Rectangle(tx, Height / 2, tw, (int)(18 * S)), Pal.Muted, Ui.Left);
        }
    }

    // A thin line between parts of a card.
    class Divider : ThemedControl
    {
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Pal.Border); }
    }

    // Live preview of the ring: click a button to select it, drag it to move it.
    class RingPreview : ThemedControl
    {
        public Config Cfg;
        RingRenderer rr;
        int hover = RingRenderer.HitNone;
        int selected = RingRenderer.HitCenter;
        int dragFrom = -1;       // button pressed, -1 = none
        bool dragging;
        Point pressedAt;
        public event EventHandler SelectedChanged;
        public event EventHandler Reordered;
        public int Selected { get { return selected; } set { selected = value; Invalidate(); } }

        int rrSize;

        // The scale to draw at: the ring's real size on this monitor (set by the settings window, which
        // sizes this control to fit). 0 = fit the control.
        public float RealScale;

        // The preview shows the ring as it really is: its real size, with the panels where they go.
        RingRenderer Renderer
        {
            get
            {
                float factor = Cfg != null ? Cfg.SizeFactor : 1f;
                bool vol = Cfg != null && Cfg.VolumeBar, med = Cfg != null && Cfg.MediaBar;
                string pos = Cfg != null ? Cfg.PanelPos : "below";
                // Every mixer app gets a row here, running or not, so you can see where they go.
                int rows = Cfg != null && vol ? Cfg.MixerApps.Count : 0;
                SizeF ext = RingRenderer.Extent(vol, med, pos, rows);
                float fit = Math.Min(Width / (ext.Width * 1.01f), Height / (ext.Height * 1.01f));
                float scale = RealScale > 0 ? Math.Min(RealScale, fit) : fit * factor;
                int want = (int)Math.Round(320 * scale * 100);
                if (rr == null || rrSize != want || rr.VolumeBar != vol || rr.MediaBar != med || rr.PanelPos != pos || rr.AppRows != rows)
                {
                    if (rr != null) rr.Dispose();
                    rr = new RingRenderer(scale, factor, vol, med, 0, 0, pos, rows);
                    rrSize = want;
                }
                return rr;
            }
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); DropStage(); Invalidate(); }

        // The preview is a "stage": a dotted surface like a desktop, with the ring in its upper part and a
        // strip at the bottom for a hint (or the toolbar the window puts there).
        public const float BottomStrip = 64; // unscaled
        public string Hint = "";
        Bitmap stage;     // the surface, drawn once per size and theme
        string stageKey;

        public override void SetPalette(Palette p) { BackColor = p.Stage; DropStage(); base.SetPalette(p); }

        protected override Color BackForParent { get { return Parent != null ? Parent.BackColor : Pal.Window; } }

        void DropStage() { if (stage != null) { stage.Dispose(); stage = null; } }

        int RingAreaHeight { get { return Height - (int)Math.Round(BottomStrip * S); } }

        Point RingOffset(RingRenderer r) { return new Point((Width - r.Width) / 2, Math.Max(0, (RingAreaHeight - r.Height) / 2)); }

        PointF Local(Point p) { RingRenderer r = Renderer; Point o = RingOffset(r); return new PointF(p.X - o.X, p.Y - o.Y); }

        void DrawStage(Graphics g)
        {
            string key = Width + "x" + Height + ":" + Pal.Stage.ToArgb() + ":" + Pal.Dark;
            if (stage == null || stageKey != key)
            {
                DropStage();
                stage = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
                using (Graphics sg = Graphics.FromImage(stage))
                {
                    sg.Clear(BackForParent);
                    sg.SmoothingMode = SmoothingMode.AntiAlias;
                    RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
                    using (GraphicsPath p = Round(r, 14 * S))
                    {
                        using (SolidBrush b = new SolidBrush(Pal.Stage)) sg.FillPath(b, p);
                        sg.SetClip(p);
                        float step = 16 * S, d = 2.4f * S;
                        using (SolidBrush dot = new SolidBrush(Pal.Dot))
                            for (float y = step / 2; y < Height; y += step)
                                for (float x = step / 2; x < Width; x += step)
                                    sg.FillEllipse(dot, x - d / 2, y - d / 2, d, d);
                        sg.ResetClip();
                        using (Pen pen = new Pen(Pal.Border)) sg.DrawPath(pen, p);
                    }
                    // Corner marks in the top corners, like a canvas: this is a picture of the ring, not a
                    // control. (A boxed label here looked like a button.)
                    using (Pen mark = new Pen(MarkColor, 2 * S) { StartCap = LineCap.Square, EndCap = LineCap.Square })
                    {
                        float m = 12 * S, l = 12 * S;
                        sg.DrawLines(mark, new[] { new PointF(m, m + l), new PointF(m, m), new PointF(m + l, m) });
                        sg.DrawLines(mark, new[] { new PointF(Width - 1 - m - l, m), new PointF(Width - 1 - m, m), new PointF(Width - 1 - m, m + l) });
                    }
                }
                stageKey = key;
            }
            g.DrawImageUnscaled(stage, 0, 0);
            // "1:1" next to the left mark, straight onto the window (Windows' text smoothing smudges it in a bitmap).
            TextRenderer.DrawText(g, Str.PreviewOneToOne, Ui.Font(11, false), ScaleLabelRect, MarkColor, Pal.Stage, Ui.Left);
        }

        Color MarkColor { get { return Theme.Mix(Pal.Stage, Pal.Muted, 0.6f); } }
        Rectangle ScaleLabelRect { get { return new Rectangle((int)(30 * S), (int)(9 * S), (int)(40 * S), (int)(16 * S)); } }

        // The "1:1" says what it means on hover.
        readonly ToolTip scaleTip = new ToolTip();
        bool overScale;

        void UpdateScaleTip(Point p)
        {
            bool over = Rectangle.Inflate(new Rectangle(0, 0, (int)(70 * S), (int)(36 * S)), 0, 0).Contains(p);
            if (over == overScale) return;
            overScale = over;
            scaleTip.SetToolTip(this, over ? Str.PreviewChip : null);
        }

        // The mixer apps' audio, re-read at most once a second (painting happens on every hover).
        List<AppVolume.AppSessions> live;
        int liveAt;

        public List<AppVolume.AppSessions> Live()
        {
            if (live == null || unchecked(Environment.TickCount - liveAt) > 1000)
            {
                live = AppVolume.ForApps(Cfg.MixerApps);
                liveAt = Environment.TickCount;
            }
            return live;
        }

        RingRenderer.AppRow[] MixerRows() { return RingForm.PreviewRows(Cfg, Live()); }

        // The mixer list changed: read the apps' audio again on the next paint.
        public void RefreshMixer() { live = null; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int h = Renderer.HitTest(Local(e.Location), Cfg.Items.Count, false);
            if (h == RingRenderer.HitNone) return;
            selected = h;
            dragFrom = h; // the center button can't move, but it can still be selected
            pressedAt = e.Location;
            Invalidate();
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateScaleTip(e.Location);
            if (dragFrom >= 0 && e.Button == MouseButtons.Left)
            {
                if (!dragging && (Math.Abs(e.X - pressedAt.X) > 5 * S || Math.Abs(e.Y - pressedAt.Y) > 5 * S)) dragging = true;
                if (dragging)
                {
                    Cursor = Cursors.SizeAll;
                    // Only the direction counts, so the button follows the mouse around the ring.
                    int target = Renderer.HitTest(Local(e.Location), Cfg.Items.Count, true);
                    if (target >= 0 && target != selected)
                    {
                        RingItem it = Cfg.Items[selected];
                        Cfg.Items.RemoveAt(selected);
                        Cfg.Items.Insert(target, it);
                        selected = hover = target;
                        Invalidate();
                        if (Reordered != null) Reordered(this, EventArgs.Empty);
                    }
                    return;
                }
            }
            int h = Renderer.HitTest(Local(e.Location), Cfg.Items.Count, false);
            Cursor = h == RingRenderer.HitNone ? Cursors.Default : Cursors.Hand;
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragFrom = -1;
            dragging = false;
        }

        protected override void OnMouseLeave(EventArgs e) { if (!dragging) hover = RingRenderer.HitNone; base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            DrawStage(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Cfg == null) return;
            if (!string.IsNullOrEmpty(Hint) && !HasVisibleChild())
                TextRenderer.DrawText(g, Hint, Ui.Font(12, false), new Rectangle(0, RingAreaHeight, Width, (int)Math.Round(BottomStrip * S)), Pal.Muted, Ui.Center);
            RingRenderer r = Renderer;
            // Whole pixels: a half-pixel offset smears every letter and line over two pixels.
            Point o = RingOffset(r);
            g.TranslateTransform(o.X, o.Y);
            r.Draw(g, Cfg, Pal, hover, selected, false);
            if (r.VolumeBar)
            {
                float level; bool muted;
                bool ok = Volume.TryGet(out level, out muted);
                r.DrawVolume(g, Cfg, Pal, RingRenderer.VolNone, ok, level, muted);
            }
            if (r.AppRows > 0) r.DrawApps(g, Cfg, Pal, MixerRows(), -1, RingRenderer.VolNone);
            // Always drawn here (a placeholder when nothing plays), so you can see where it goes.
            r.DrawMedia(g, Cfg, Pal, RingRenderer.MedNone, Media.Current, true);
        }

        bool HasVisibleChild()
        {
            foreach (Control c in Controls) if (c.Visible) return true;
            return false;
        }

        // Shows the song that's playing, and follows it while the settings are open.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Media.Changed += OnMediaChanged;
            Media.Refresh();
        }

        void OnMediaChanged()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke((Action)Invalidate); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Media.Changed -= OnMediaChanged;
                if (rr != null) rr.Dispose();
                scaleTip.Dispose();
                DropStage();
            }
            base.Dispose(disposing);
        }
    }

    // Read-only text box that records a key combination instead of text.
    class HotkeyBox : TextBox
    {
        readonly bool requireModifier;
        string combo = "";
        public event EventHandler ComboChanged;

        public HotkeyBox(bool requireModifier)
        {
            this.requireModifier = requireModifier;
            ReadOnly = true;
            ShortcutsEnabled = false;
            Cursor = Cursors.Hand;
            Combo = "";
        }

        public string Combo
        {
            get { return combo; }
            set { combo = value ?? ""; Text = combo.Length == 0 ? Str.PressKeys : combo; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool noMods = (keyData & Keys.Modifiers) == 0;
            if (noMods && (key == Keys.Tab || key == Keys.Escape)) return false;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;

            uint mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= KeyCombo.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) mods |= KeyCombo.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) mods |= KeyCombo.MOD_SHIFT;
            if (Native.GetKeyState(0x5B) < 0 || Native.GetKeyState(0x5C) < 0) mods |= KeyCombo.MOD_WIN;

            bool fkey = key >= Keys.F1 && key <= Keys.F24;
            if (requireModifier && mods == 0 && !fkey)
            {
                System.Media.SystemSounds.Beep.Play();
                return true;
            }
            Combo = KeyCombo.Format(mods, key);
            if (ComboChanged != null) ComboChanged(this, EventArgs.Empty);
            return true;
        }
    }
}

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
            using (GraphicsPath p = Round(Inner, 10 * S))
            {
                using (SolidBrush b = new SolidBrush(Pal.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Pal.Border)) g.DrawPath(pen, p);
            }
        }
    }

    class FlatButton : ThemedControl
    {
        public enum Kind { Normal, Link, Danger }
        public Kind Style;

        public FlatButton(string text, Kind style) { Text = text; Style = style; Cursor = Cursors.Hand; TabStop = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            Color fg = !Enabled ? Pal.Muted : Style == Kind.Link ? Pal.AccentText : Style == Kind.Danger ? Pal.Danger : Pal.Text;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                (Style == Kind.Normal ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            if (Style == Kind.Normal)
                using (GraphicsPath p = Round(Inner, 6 * S))
                {
                    using (SolidBrush b = new SolidBrush(Hot && Enabled ? Pal.Hover : Back)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Focused ? Pal.Accent : Pal.Border)) g.DrawPath(pen, p);
                }
            using (Font f = Hot && Style != Kind.Normal ? new Font(Font, FontStyle.Underline) : (Font)Font.Clone())
            {
                if (Style == Kind.Normal) DrawCentered(g, Text, f, ClientRectangle, fg);
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

    // Big choice tile with a glyph above the text.
    class Tile : ThemedControl
    {
        public Action<Graphics, RectangleF, Color> Glyph;
        bool selected;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public Tile(string text, Action<Graphics, RectangleF, Color> glyph) { Text = text; Glyph = glyph; Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            RectangleF r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (GraphicsPath p = Round(r, 8 * S))
            {
                Color fill = selected ? Theme.Mix(Pal.Card, Pal.Accent, Pal.Dark ? 0.22f : 0.12f) : Hot ? Pal.Hover : Pal.Card;
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(selected ? Pal.Accent : Pal.Border, selected ? 2 * S : 1)) g.DrawPath(pen, p);
            }
            // Icon and text are centered together as one block.
            Color fg = Pal.Text;
            float gs = Math.Min(22 * S, Height * 0.36f);
            bool hasText = !string.IsNullOrEmpty(Text);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int th = hasText ? TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue), flags).Height : 0;
            float gap = hasText ? 5 * S : 0;
            float top = (Height - (gs + gap + th)) / 2f;
            if (Glyph != null) Glyph(g, new RectangleF((Width - gs) / 2f, top, gs, gs), fg);
            if (hasText) DrawCentered(g, Text, Font, new Rectangle(3, (int)Math.Round(top + gs + gap), Width - 6, th), fg);
        }
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
            float h = 20 * S, w = 40 * S;
            RectangleF r = new RectangleF(Width - w - 1, (Height - h) / 2, w, h);
            using (GraphicsPath p = Round(r, h / 2))
            {
                using (SolidBrush b = new SolidBrush(on ? Pal.Accent : Pal.Field)) g.FillPath(b, p);
                if (!on) using (Pen pen = new Pen(Pal.Muted)) g.DrawPath(pen, p);
            }
            float k = h - 8 * S;
            float kx = on ? r.Right - k - 4 * S : r.X + 4 * S;
            using (SolidBrush b = new SolidBrush(on ? Theme.TextOn(Pal.Accent) : Pal.Muted))
                g.FillEllipse(b, kx, r.Y + 4 * S, k, k);
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
            using (GraphicsPath p = Round(Inner, 6 * S))
            {
                using (SolidBrush b = new SolidBrush(Pal.Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(Pal.Border)) g.DrawPath(pen, p);
            }
            float w = (Width - 1f) / Options.Length;
            for (int i = 0; i < Options.Length; i++)
            {
                RectangleF r = new RectangleF(i * w + 3 * S, 3 * S, w - 6 * S, Height - 7 * S);
                Color fg = Pal.Text;
                if (i == index || i == hotIndex)
                    using (GraphicsPath p = Round(r, 4 * S))
                    using (SolidBrush b = new SolidBrush(i == index ? Pal.Accent : Pal.Hover))
                        g.FillPath(b, p);
                if (i == index) fg = Theme.TextOn(Pal.Accent);
                TextRenderer.DrawText(g, Options[i], Font, Rectangle.Round(r), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // Color picker row: a "default" swatch, preset colors and a "+" for any color.
    class SwatchRow : ThemedControl
    {
        public string[] Colors;              // hex values or "windows"; index 0 must be "" (default)
        public Func<Color> DefaultColor;     // what "default" currently looks like
        string selected = "";
        public event EventHandler Changed;
        public string Selected { get { return selected; } set { selected = value ?? ""; Invalidate(); } }

        public SwatchRow(string[] colors) { Colors = colors; Cursor = Cursors.Hand; }

        float D { get { return 22 * S; } }
        float Step { get { return 30 * S; } }
        float Pad { get { return 6 * S; } } // room for the selection ring

        bool IsCustom { get { return selected.Length > 0 && Array.IndexOf(Colors, selected) < 0; } }
        int Count { get { return Colors.Length + (IsCustom ? 1 : 0) + 1; } } // + custom + "+"

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = (int)((e.X - Pad + (Step - D) / 2) / Step);
            if (i < 0 || i >= Count) return;
            if (i < Colors.Length) Pick(Colors[i]);
            else if (IsCustom && i == Colors.Length) Pick(selected);
            else
                using (ColorDialog d = new ColorDialog())
                {
                    d.FullOpen = true;
                    Color? cur = Theme.ParseHex(selected);
                    d.Color = cur.HasValue ? cur.Value : DefaultColor();
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) Pick(Theme.ToHex(d.Color));
                }
        }

        void Pick(string hex)
        {
            selected = hex;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            float y = (Height - D) / 2;
            int slot = 0;
            for (int i = 0; i < Colors.Length; i++, slot++)
            {
                bool windows = Colors[i] == "windows"; // the Windows accent color
                Color? c = windows ? Art.Accent() : Theme.ParseHex(Colors[i]);
                Dot(g, slot, y, c.HasValue ? c.Value : DefaultColor(), Colors[i] == selected, i == 0 ? "A" : windows ? "W" : null);
            }
            if (IsCustom) { Dot(g, slot, y, Theme.ParseHex(selected).Value, true, null); slot++; }
            RectangleF r = new RectangleF(slot * Step + Pad, y, D, D);
            using (Pen p = new Pen(Pal.Muted, S) { DashStyle = DashStyle.Dash }) g.DrawEllipse(p, r);
            using (Pen p = new Pen(Pal.Muted, 1.5f * S))
            {
                float cx = r.X + D / 2, cy = r.Y + D / 2, a = D * 0.22f;
                g.DrawLine(p, cx - a, cy, cx + a, cy);
                g.DrawLine(p, cx, cy - a, cx, cy + a);
            }
        }

        // letter: "A" marks the default swatch, "W" the Windows accent color.
        void Dot(Graphics g, int slot, float y, Color c, bool sel, string letter)
        {
            RectangleF r = new RectangleF(slot * Step + Pad, y, D, D);
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, r);
            using (Pen p = new Pen(Pal.Border)) g.DrawEllipse(p, r);
            if (letter != null)
                using (Font f = new Font(Font.FontFamily, Font.Size * 0.75f, FontStyle.Bold, Font.Unit))
                    DrawCentered(g, letter, f, Rectangle.Round(r), Theme.TextOn(c));
            if (sel)
            {
                RectangleF o = RectangleF.Inflate(r, 3 * S, 3 * S);
                using (Pen p = new Pen(Pal.Text, 2 * S)) g.DrawEllipse(p, o);
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
            using (GraphicsPath p = Round(Inner, 6 * S))
            {
                using (SolidBrush b = new SolidBrush(Pal.Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(Box.Focused ? Pal.Accent : Pal.Border, Box.Focused ? 1.5f * S : 1)) g.DrawPath(pen, p);
            }
        }

        // Grey hint text shown while empty.
        public void SetCue(string text) { Native.SendMessage(Box.Handle, 0x1501, (IntPtr)1, text); }
    }

    // Shows the chosen app with its icon; click to pick another.
    class AppDisplay : ThemedControl
    {
        public string Path = "", Title = "";
        public AppDisplay() { Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            using (GraphicsPath p = Round(Inner, 6 * S))
            {
                using (SolidBrush b = new SolidBrush(Hot ? Pal.Hover : Pal.Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(Pal.Border)) g.DrawPath(pen, p);
            }
            float isz = 22 * S;
            Bitmap icon = Path.Length > 0 ? Art.PathIcon(Path, false) : null;
            RectangleF ir = new RectangleF(9 * S, (Height - isz) / 2, isz, isz);
            if (icon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(icon, ir); }
            else Art.DrawApps(g, ir, Pal.Muted);
            Rectangle tr = new Rectangle((int)(ir.Right + 8 * S), 0, (int)(Width - ir.Right - 14 * S), Height);
            TextRenderer.DrawText(g, Title, Font, tr, Path.Length > 0 ? Pal.Text : Pal.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
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

        // The preview shows the chosen ring size: Large fills the control, the others are smaller.
        RingRenderer Renderer
        {
            get
            {
                float factor = Cfg != null ? Cfg.SizeFactor : 1f;
                float scale = Math.Min(Width, Height) / (320f * 1.25f) * factor;
                int want = (int)Math.Round(320 * scale);
                if (rr == null || rrSize != want)
                {
                    if (rr != null) rr.Dispose();
                    rr = new RingRenderer(scale, factor);
                    rrSize = want;
                }
                return rr;
            }
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Invalidate(); }

        PointF Local(Point p) { RingRenderer r = Renderer; return new PointF(p.X - (Width - r.Size) / 2f, p.Y - (Height - r.Size) / 2f); }

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
            Graphics g = Prepare(e);
            if (Cfg == null) return;
            RingRenderer r = Renderer;
            g.TranslateTransform((Width - r.Size) / 2f, (Height - r.Size) / 2f);
            r.Draw(g, Cfg, Pal, hover, selected, false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && rr != null) rr.Dispose();
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

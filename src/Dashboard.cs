using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickCut
{
    // The controls of the settings window's dashboard look: the side rail, tiles, swatches, the icon
    // picker, key caps and the preview toolbar. All owner-drawn like the rest (ThemedControl).
    static class Ui
    {
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        // Segoe UI at a pixel size (already scaled by ThemedControl.S), cached.
        public static Font Font(float px, bool semibold)
        {
            float size = (float)Math.Round(px * ThemedControl.S, 1);
            string key = size + (semibold ? "b" : "");
            Font f;
            if (!fonts.TryGetValue(key, out f))
                fonts[key] = f = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, GraphicsUnit.Pixel);
            return f;
        }

        // No padding: text is measured with NoPadding everywhere, so it's drawn the same way (with GDI's
        // padding the measured width was a few pixels short, and the text got an ellipsis).
        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

        // The on/off switch: an accent track with a dark knob when on.
        public static void DrawToggle(Graphics g, RectangleF r, bool on, Palette pal)
        {
            float s = ThemedControl.S;
            using (GraphicsPath p = ThemedControl.Round(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(on ? pal.Accent : pal.Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(on ? pal.Accent : pal.Border, s)) g.DrawPath(pen, p);
            }
            float k = r.Height - 10 * s;
            float kx = on ? r.Right - k - 5 * s : r.X + 5 * s;
            using (SolidBrush b = new SolidBrush(on ? Theme.TextOn(pal.Accent) : pal.Muted))
                g.FillEllipse(b, kx, r.Y + 5 * s, k, k);
        }

        // A rounded box: fill and a 1 px border.
        public static void Box(Graphics g, RectangleF r, float radius, Color fill, Color border)
        {
            using (GraphicsPath p = ThemedControl.Round(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
        }

        // A key on a keyboard: a slightly thicker bottom edge.
        public static void KeyCap(Graphics g, RectangleF r, string text, Palette pal)
        {
            float s = ThemedControl.S;
            using (GraphicsPath p = ThemedControl.Round(r, 7 * s))
            using (SolidBrush b = new SolidBrush(pal.Border)) g.FillPath(b, p);
            RectangleF top = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 3.5f * s);
            using (GraphicsPath p = ThemedControl.Round(top, 6 * s))
            using (SolidBrush b = new SolidBrush(pal.Field)) g.FillPath(b, p);
            TextRenderer.DrawText(g, text, Font(13, false), Rectangle.Round(top), pal.Text, Center);
        }

        public static int KeyCapWidth(Graphics g, string text)
        {
            float s = ThemedControl.S;
            int w = TextRenderer.MeasureText(g, text, Font(13, false), new Size(1000, 100), TextFormatFlags.NoPadding).Width;
            return (int)Math.Max(40 * s, w + 22 * s);
        }

        // "Ctrl+Shift+Esc" -> Ctrl, Shift, Esc ("Ctrl++" ends in the plus key).
        public static List<string> KeyParts(string combo)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrEmpty(combo)) return parts;
            string c = combo;
            bool plusKey = c == "+" || c.EndsWith("++");
            if (plusKey) c = c.Substring(0, c.Length - 1);
            foreach (string p in c.Split('+')) if (p.Trim().Length > 0) parts.Add(p.Trim());
            if (plusKey) parts.Add("+");
            return parts;
        }
    }

    // The side rail: the page buttons from the top, and at the bottom the app icon over the version,
    // as a small signature.
    class NavRail : ThemedControl
    {
        Bitmap icon;

        public override void SetPalette(Palette p) { BackColor = p.Rail; base.SetPalette(p); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Pal.Rail);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen line = new Pen(Pal.Border)) g.DrawLine(line, Width - 1, 0, Width - 1, Height);
            int isz = (int)Math.Round(22 * S);
            if (icon == null || icon.Width != isz) { if (icon != null) icon.Dispose(); icon = Art.RenderAppIcon(isz); }
            g.DrawImageUnscaled(icon, (Width - isz) / 2, Height - (int)(56 * S));
            TextRenderer.DrawText(g, Text, Ui.Font(11, false), new Rectangle(0, Height - (int)(30 * S), Width, (int)(18 * S)), Pal.Muted, Ui.Center);
        }

        protected override void Dispose(bool disposing) { if (disposing && icon != null) icon.Dispose(); base.Dispose(disposing); }
    }

    // A page button in the rail: an icon over a short name.
    class NavButton : ThemedControl
    {
        public readonly string IconName;
        bool selected;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public NavButton(string icon, string text) { IconName = icon; Text = text; Cursor = Cursors.Hand; TabStop = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (selected || Hot || Focused)
                using (GraphicsPath p = Round(r, 12 * S))
                using (SolidBrush b = new SolidBrush(selected ? Pal.AccentSoft : Pal.Hover)) g.FillPath(b, p);
            Color fg = selected ? (Pal.Dark ? Pal.Accent : Pal.AccentText) : Hot ? Pal.Text : Pal.Muted;
            float isz = 22 * S;
            Icons.Draw(g, IconName, new RectangleF((Width - isz) / 2, 9 * S, isz, isz), fg);
            TextRenderer.DrawText(g, Text, Ui.Font(11, false), new Rectangle(0, (int)(34 * S), Width, (int)(18 * S)), fg, Ui.Center);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // "✓ All changes saved", as a small pill on the right of the header.
    class StatusPill : ThemedControl
    {
        public bool Danger, Busy;

        public void Set(string text, bool danger, bool busy) { Text = text; Danger = danger; Busy = busy; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            if (string.IsNullOrEmpty(Text)) return;
            Font f = Ui.Font(12, false);
            int tw = TextRenderer.MeasureText(g, Text, f, new Size(1000, 100), TextFormatFlags.NoPadding).Width;
            bool icon = !Danger && !Busy;
            float h = 28 * S, w = tw + (icon ? 44 : 26) * S;
            RectangleF r = new RectangleF(Width - w - 1, (Height - h) / 2, w, h);
            Ui.Box(g, r, h / 2, Pal.Card, Pal.Border);
            float x = r.X + 12 * S;
            if (icon)
            {
                Icons.Get("check").Draw(g, new RectangleF(x, r.Y + 7 * S, 14 * S, 14 * S), Pal.Success, 2.6f, false);
                x += 20 * S;
            }
            TextRenderer.DrawText(g, Text, f, new Rectangle((int)x, (int)r.Y, tw + 4, (int)h), Danger ? Pal.Danger : Pal.Muted, Ui.Left);
        }
    }

    // A tile with an icon, an on/off switch, a title and a line of explanation. The whole tile toggles.
    class ToggleTile : ThemedControl
    {
        public string IconName, Title, Desc;
        bool on;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        public ToggleTile(string icon, string title, string desc) { IconName = icon; Title = title; Desc = desc; Cursor = Cursors.Hand; TabStop = true; }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            Ui.Box(g, Inner, 12 * S, Hot ? Theme.Mix(Pal.Card, Pal.Text, 0.03f) : Pal.Card, Focused ? Pal.Accent : Pal.Border);
            RectangleF sq = new RectangleF(16 * S, 14 * S, 34 * S, 34 * S);
            using (GraphicsPath p = Round(sq, 10 * S))
            using (SolidBrush b = new SolidBrush(Pal.AccentSoft)) g.FillPath(b, p);
            Icons.Get(IconName).Draw(g, RectangleF.Inflate(sq, -8 * S, -8 * S), Pal.Dark ? Pal.Accent : Pal.AccentText, 2f, false);
            Ui.DrawToggle(g, new RectangleF(Width - 58 * S, 19 * S, 42 * S, 24 * S), on, Pal);
            TextRenderer.DrawText(g, Title, Ui.Font(14, true), new Rectangle((int)(16 * S), (int)(56 * S), Width - (int)(32 * S), (int)(22 * S)), Pal.Text, Ui.Left);
            TextRenderer.DrawText(g, Desc, Ui.Font(12, false), new Rectangle((int)(16 * S), (int)(78 * S), Width - (int)(32 * S), (int)(20 * S)), Pal.Muted, Ui.Left);
        }
    }

    // A selectable tile: a picture (or icon) over a short name. Plain tiles aren't selectable (actions).
    class ChoiceTile : ThemedControl
    {
        public Action<Graphics, RectangleF, Color> Picture; // draws in the given area with the given ink
        public float PictureHeight = 24;                   // unscaled
        public bool Plain;                                 // an action: card colors, no selected state
        public Color? Ink;                                 // fixed ink (a red "Restore" tile)
        bool selected;
        public bool Selected { get { return selected; } set { if (selected == value) return; selected = value; Invalidate(); } }

        public ChoiceTile(string text, string icon) : this(text, null as Action<Graphics, RectangleF, Color>)
        {
            Picture = delegate(Graphics g, RectangleF r, Color c) { Icons.Draw(g, icon, r, c); };
            PictureHeight = 22;
        }

        public ChoiceTile(string text, Action<Graphics, RectangleF, Color> picture) { Text = text; Picture = picture; Cursor = Cursors.Hand; TabStop = true; }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            bool sel = selected && !Plain;
            Color fill = sel ? Theme.Mix(Pal.Field, Pal.Accent, Pal.Dark ? 0.13f : 0.12f) : Plain ? (Hot ? Pal.Hover : Pal.Card) : Hot ? Pal.Hover : Pal.Field;
            Color border = sel || Focused ? Pal.Accent : Hot && !Plain ? Pal.Muted : Pal.Border;
            Ui.Box(g, Inner, 11 * S, fill, border);
            Color ink = Ink.HasValue ? Ink.Value : sel ? (Pal.Dark ? Pal.Accent : Pal.AccentText) : Plain || Hot ? Pal.Text : Pal.Muted;
            Color fg = Ink.HasValue ? Ink.Value : sel || Plain || Hot ? Pal.Text : Pal.Muted;
            float ph = PictureHeight * S, gap = 6 * S, th = 16 * S;
            float top = (Height - (ph + gap + th)) / 2;
            float pw = Math.Min(Width - 12 * S, ph * 1.6f);
            if (Picture != null) Picture(g, new RectangleF((Width - pw) / 2, top, pw, ph), ink);
            TextRenderer.DrawText(g, Text, Ui.Font(12, false), new Rectangle(2, (int)(top + ph + gap), Width - 4, (int)th + 2), fg, Ui.Center);
        }
    }

    // A row of round color swatches that wraps. Value: "" = automatic, "windows", or #RRGGBB.
    class Swatches : ThemedControl
    {
        public class Entry { public string Value, Name; public Func<Color> Color; }

        public readonly List<Entry> Entries = new List<Entry>();
        public event EventHandler Changed;
        public Func<Color> PickerStart; // where the color picker starts
        string value = "";
        int hot = -1;
        readonly ToolTip tip = new ToolTip();

        public string Value { get { return value; } set { this.value = value ?? ""; Invalidate(); } }

        float D { get { return 30 * S; } }

        // The color in the list for this value, its name, or null when it's a custom color.
        public string NameOf(string v)
        {
            foreach (Entry en in Entries) if (string.Equals(en.Value, v, StringComparison.OrdinalIgnoreCase)) return en.Name;
            return v.StartsWith("#") ? string.Format(Str.CustomColor, v.ToUpperInvariant()) : "";
        }

        bool IsCustom
        {
            get
            {
                if (!value.StartsWith("#")) return false;
                foreach (Entry en in Entries) if (string.Equals(en.Value, value, StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
        }

        int Count { get { return Entries.Count + (IsCustom ? 1 : 0) + 1; } } // + the custom one, + "more colors"

        // Room around the grid for the selection ring, which sits outside the swatch: without it the ring
        // of a swatch on the first or last row or column was cut off.
        float M { get { return 5 * S; } }

        static int Cols(float inner, float d, float s) { return Math.Max(1, (int)((inner + 6 * s) / (d + 6 * s))); }

        void Grid(out int cols, out float step)
        {
            float inner = Width - 2 * M;
            cols = Cols(inner, D, S);
            step = cols > 1 ? Math.Min(D + 10 * S, (inner - D) / (cols - 1)) : D;
        }

        RectangleF Cell(int i)
        {
            int cols; float step;
            Grid(out cols, out step);
            return new RectangleF(M + (i % cols) * step, M + (i / cols) * (D + 8 * S), D, D);
        }

        public int PreferredHeight(int width)
        {
            int cols = Cols(width - 2 * M, D, S);
            int rows = (Count + cols - 1) / cols;
            return (int)Math.Ceiling(rows * D + (rows - 1) * 8 * S + 2 * M);
        }

        int At(Point p)
        {
            for (int i = 0; i < Count; i++)
                if (RectangleF.Inflate(Cell(i), 3 * S, 3 * S).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = At(e.Location);
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            if (i == hot) return;
            hot = i;
            string name = i < 0 ? null : i < Entries.Count ? Entries[i].Name : IsCustom && i == Entries.Count ? NameOf(value) : Str.MoreColors;
            tip.SetToolTip(this, name);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = At(e.Location);
            if (i < 0) return;
            if (i < Entries.Count) Pick(Entries[i].Value);
            else if (IsCustom && i == Entries.Count) return; // already chosen
            else
            {
                Color? cur = Theme.ParseHex(value);
                using (ColorPickerForm d = new ColorPickerForm(Pal, cur.HasValue ? cur.Value : PickerStart != null ? PickerStart() : Pal.Accent))
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) Pick(Theme.ToHex(d.Result));
            }
        }

        void Pick(string v)
        {
            Value = v;
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            for (int i = 0; i < Count; i++)
            {
                RectangleF r = Cell(i);
                bool more = i == Count - 1;
                if (more)
                {
                    using (Pen pen = new Pen(i == hot ? Pal.Text : Pal.Muted, S) { DashStyle = DashStyle.Dash }) g.DrawEllipse(pen, RectangleF.Inflate(r, -1, -1));
                    Icons.Get("plus").Draw(g, RectangleF.Inflate(r, -8 * S, -8 * S), i == hot ? Pal.Text : Pal.Muted, 2f, false);
                    continue;
                }
                string v = i < Entries.Count ? Entries[i].Value : value;
                Color c = i < Entries.Count ? Entries[i].Color() : Theme.ParseHex(value).Value;
                bool sel = string.Equals(v, value, StringComparison.OrdinalIgnoreCase);
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, r);
                using (Pen pen = new Pen(i == hot ? Pal.Muted : Pal.Border, S)) g.DrawEllipse(pen, r);
                if (v == "") // automatic: a small "A"
                    TextRenderer.DrawText(g, "A", Ui.Font(11, true), Rectangle.Round(r), Theme.TextOn(c), Ui.Center);
                if (v == "windows") // the Windows accent: four small squares
                {
                    Color ink = Theme.TextOn(c);
                    float q = 4 * S, cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                    using (SolidBrush b = new SolidBrush(ink))
                        for (int k = 0; k < 4; k++) g.FillRectangle(b, cx - q - 0.5f * S + (k % 2) * (q + S), cy - q - 0.5f * S + (k / 2) * (q + S), q, q);
                }
                if (sel)
                    using (Pen ring = new Pen(Pal.Accent, 2 * S)) g.DrawEllipse(ring, RectangleF.Inflate(r, 2.5f * S, 2.5f * S));
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    // Pills to pick one of a few options (media actions); they wrap.
    class ChipGroup : ThemedControl
    {
        public string[] Options = new string[0];
        int index = -1, hot = -1;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex { get { return index; } set { index = value; Invalidate(); } }

        public ChipGroup() { Cursor = Cursors.Hand; }

        List<RectangleF> Cells(Graphics g, int width)
        {
            List<RectangleF> list = new List<RectangleF>();
            float x = 0, y = 0, h = 30 * S, gap = 6 * S;
            foreach (string o in Options)
            {
                float w = TextRenderer.MeasureText(g, o, Ui.Font(12, false), new Size(1000, 100), TextFormatFlags.NoPadding).Width + 26 * S;
                if (x > 0 && x + w > width) { x = 0; y += h + gap; }
                list.Add(new RectangleF(x, y, w, h));
                x += w + gap;
            }
            return list;
        }

        public int PreferredHeight(int width)
        {
            using (Graphics g = CreateGraphics())
            {
                List<RectangleF> c = Cells(g, width);
                return c.Count == 0 ? 0 : (int)Math.Ceiling(c[c.Count - 1].Bottom + 1);
            }
        }

        int At(Point p)
        {
            using (Graphics g = CreateGraphics())
            {
                List<RectangleF> c = Cells(g, Width);
                for (int i = 0; i < c.Count; i++) if (c[i].Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int i = At(e.Location); if (i != hot) { hot = i; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = At(e.Location);
            if (i < 0) return;
            index = i;
            Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            List<RectangleF> c = Cells(g, Width);
            for (int i = 0; i < c.Count; i++)
            {
                bool sel = i == index;
                RectangleF r = new RectangleF(c[i].X + 0.5f, c[i].Y + 0.5f, c[i].Width - 1, c[i].Height - 1);
                Ui.Box(g, r, r.Height / 2, sel ? Pal.Accent : i == hot ? Pal.Hover : Pal.Field, sel ? Pal.Accent : Pal.Border);
                TextRenderer.DrawText(g, Options[i], Ui.Font(12, sel), Rectangle.Round(r), sel ? Theme.TextOn(Pal.Accent) : Pal.Text, Ui.Center);
            }
        }
    }

    // A row with an icon, a title and an explanation, and a switch on the right (Toggle) or room for a
    // control the window puts there.
    class SettingRow : ThemedControl
    {
        public string IconName, Title, Desc;
        public bool Toggle;
        public float RightReserve = 150; // unscaled room on the right for a control the window adds
        bool on;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        public SettingRow(string icon, string title, string desc, bool toggle)
        {
            IconName = icon; Title = title; Desc = desc; Toggle = toggle;
            if (toggle) { Cursor = Cursors.Hand; TabStop = true; }
        }

        protected override void OnClick(EventArgs e) { if (Toggle) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (Toggle && e.KeyCode == Keys.Space) Checked = !Checked; }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            RectangleF sq = new RectangleF(16 * S, (Height - 34 * S) / 2, 34 * S, 34 * S);
            using (GraphicsPath p = Round(sq, 10 * S))
            using (SolidBrush b = new SolidBrush(Pal.Field)) g.FillPath(b, p);
            Icons.Get(IconName).Draw(g, RectangleF.Inflate(sq, -8 * S, -8 * S), Pal.Muted, 1.8f, false);
            int tx = (int)(62 * S), tw = Width - tx - (int)((Toggle ? 74 : RightReserve) * S);
            TextRenderer.DrawText(g, Title, Ui.Font(13, false), new Rectangle(tx, (int)(Height / 2 - 19 * S), tw, (int)(20 * S)), Pal.Text, Ui.Left);
            TextRenderer.DrawText(g, Desc, Ui.Font(12, false), new Rectangle(tx, (int)(Height / 2 + 1 * S), tw, (int)(18 * S)), Pal.Muted, Ui.Left);
            if (Toggle)
            {
                RectangleF t = new RectangleF(Width - 58 * S, (Height - 24 * S) / 2, 42 * S, 24 * S);
                Ui.DrawToggle(g, t, on, Pal);
                if (Focused) using (GraphicsPath p = Round(RectangleF.Inflate(t, 3 * S, 3 * S), 15 * S)) using (Pen pen = new Pen(Pal.Accent, S)) g.DrawPath(pen, p);
            }
        }
    }

    // Shows a key combination as key caps, with a button to record a new one. Recording happens in a
    // hidden HotkeyBox: clicking focuses it, and the caps show "Press keys…" until a combination comes.
    class KeyRecorder : ThemedControl
    {
        public readonly HotkeyBox Box;
        public string ButtonText = "";
        public string Empty = "";
        bool hotButton;

        public KeyRecorder(bool requireModifier)
        {
            Box = new HotkeyBox(requireModifier);
            Box.SetBounds(-200, -200, 40, 20); // out of sight; it only takes the keys
            Controls.Add(Box);
            // Not focusable itself: a click on it must not take the focus from the box before OnMouseDown
            // can tell whether it was recording.
            SetStyle(ControlStyles.Selectable, false);
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            // A combination came in: recording is done. After the window has handled it (it may apply the
            // new shortcut), so the box's Leave (which brings the global hotkey back) comes last.
            Box.ComboChanged += delegate
            {
                Invalidate();
                if (IsHandleCreated) BeginInvoke((Action)Stop);
            };
            Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Stop(); e.Handled = e.SuppressKeyPress = true; }
            };
            Cursor = Cursors.Hand;
        }

        public bool Recording { get { return Box.Focused; } }

        // Ends recording without changing the combination.
        public void Stop()
        {
            Form f = FindForm();
            if (Box.Focused && f != null) f.ActiveControl = null; // the window keeps the focus, the box loses it
            Invalidate();
        }

        public string Combo { get { return Box.Combo; } set { Box.Combo = value; Invalidate(); } }

        RectangleF ButtonRect(Graphics g)
        {
            int w = TextRenderer.MeasureText(g, ButtonText, Ui.Font(13, false), new Size(1000, 100), TextFormatFlags.NoPadding).Width + (int)(28 * S);
            return new RectangleF(Width - w - 1, (Height - 32 * S) / 2, w, 32 * S);
        }

        // A click starts recording; a click while recording stops it (the button then says "Recording…").
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Box.Focused) Stop();
            else Box.Focus();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h;
            using (Graphics g = CreateGraphics()) h = ButtonRect(g).Contains(e.Location);
            if (h != hotButton) { hotButton = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hotButton = false; base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            RectangleF b = ButtonRect(g);
            bool rec = Box.Focused;
            Ui.Box(g, b, 9 * S, rec ? Pal.AccentSoft : hotButton ? Pal.Hover : Pal.Field, rec ? Pal.Accent : Pal.Border);
            TextRenderer.DrawText(g, rec ? Str.Recording : ButtonText, Ui.Font(13, false), Rectangle.Round(b), Pal.Text, Ui.Center);
            float h = 32 * S, y = (Height - h) / 2, x = 0, maxX = b.X - 10 * S;
            if (rec)
            {
                TextRenderer.DrawText(g, Str.PressKeysNow, Ui.Font(13, false), new Rectangle(0, (int)y, (int)maxX, (int)h), Pal.Dark ? Pal.Accent : Pal.AccentText, Ui.Left);
                return;
            }
            List<string> parts = Ui.KeyParts(Combo);
            if (parts.Count == 0)
            {
                TextRenderer.DrawText(g, Empty, Ui.Font(13, false), new Rectangle(0, (int)y, (int)maxX, (int)h), Pal.Muted, Ui.Left);
                return;
            }
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    TextRenderer.DrawText(g, "+", Ui.Font(13, false), new Rectangle((int)x, (int)y, (int)(14 * S), (int)h), Pal.Muted, Ui.Center);
                    x += 14 * S;
                }
                float w = Ui.KeyCapWidth(g, parts[i]);
                if (x + w > maxX) break;
                Ui.KeyCap(g, new RectangleF(x, y, w, h), parts[i], Pal);
                x += w;
            }
        }
    }

    // The floating toolbar under the preview on the Buttons page: icon buttons, then the main action.
    class PillToolbar : ThemedControl
    {
        public class Item { public string Icon, Text, Tip; public bool Primary, Enabled = true; }
        public readonly List<Item> Items = new List<Item>();
        public event Action<int> ItemClicked;
        int hot = -1;
        readonly ToolTip tip = new ToolTip();

        List<RectangleF> Cells(Graphics g)
        {
            List<RectangleF> list = new List<RectangleF>();
            float x = 5 * S, h = 36 * S, y = (Height - h) / 2;
            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                if (it.Primary) x += 9 * S; // room for the divider
                float w = string.IsNullOrEmpty(it.Text) ? 38 * S
                    : TextRenderer.MeasureText(g, it.Text, Ui.Font(13, it.Primary), new Size(1000, 100), TextFormatFlags.NoPadding).Width + (it.Icon != null ? 46 : 28) * S;
                if (it.Primary) w = Math.Max(w, 84 * S);
                list.Add(new RectangleF(x, y, w, h));
                x += w + 4 * S;
            }
            return list;
        }

        public int PreferredWidth()
        {
            using (Graphics g = CreateGraphics())
            {
                List<RectangleF> c = Cells(g);
                return c.Count == 0 ? 0 : (int)Math.Ceiling(c[c.Count - 1].Right + 5 * S);
            }
        }

        int At(Point p)
        {
            using (Graphics g = CreateGraphics())
            {
                List<RectangleF> c = Cells(g);
                for (int i = 0; i < c.Count; i++) if (c[i].Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = At(e.Location);
            Cursor = i >= 0 && Items[i].Enabled ? Cursors.Hand : Cursors.Default;
            if (i == hot) return;
            hot = i;
            tip.SetToolTip(this, i >= 0 ? Items[i].Tip : null);
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = At(e.Location);
            if (i >= 0 && Items[i].Enabled && ItemClicked != null) ItemClicked(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Ui.Box(g, Inner, 14 * S, Pal.Card, Pal.Border);
            List<RectangleF> c = Cells(g);
            for (int i = 0; i < c.Count; i++)
            {
                Item it = Items[i];
                RectangleF r = c[i];
                Color fg = !it.Enabled ? Theme.Mix(Pal.Muted, Pal.Card, 0.4f) : it.Primary ? Theme.TextOn(Pal.Accent) : Pal.Text;
                if (it.Primary)
                {
                    using (Pen line = new Pen(Pal.Border)) g.DrawLine(line, r.X - 7 * S, r.Y + 7 * S, r.X - 7 * S, r.Bottom - 7 * S);
                    using (GraphicsPath p = Round(r, 10 * S))
                    using (SolidBrush b = new SolidBrush(i == hot ? Theme.Mix(Pal.Accent, Color.White, 0.12f) : Pal.Accent)) g.FillPath(b, p);
                }
                else if (i == hot && it.Enabled)
                    using (GraphicsPath p = Round(r, 10 * S))
                    using (SolidBrush b = new SolidBrush(Pal.Hover)) g.FillPath(b, p);
                if (string.IsNullOrEmpty(it.Text))
                {
                    Icons.Get(it.Icon).Draw(g, new RectangleF(r.X + (r.Width - 18 * S) / 2, r.Y + 9 * S, 18 * S, 18 * S), fg, 2f, false);
                    continue;
                }
                Font f = Ui.Font(13, it.Primary);
                int tw = TextRenderer.MeasureText(g, it.Text, f, new Size(1000, 100), TextFormatFlags.NoPadding).Width;
                float iw = it.Icon != null ? (it.Primary ? 12 : 16) * S : 0, gap = it.Icon != null ? 7 * S : 0;
                float x = r.X + (r.Width - (iw + gap + tw)) / 2;
                if (it.Icon != null)
                    Icons.Get(it.Icon).Draw(g, new RectangleF(x, r.Y + (r.Height - iw) / 2, iw, iw), fg, 2f, it.Primary);
                TextRenderer.DrawText(g, it.Text, f, new Rectangle((int)Math.Round(x + iw + gap), (int)r.Y, tw + 2, (int)r.Height), fg, Ui.Left);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    // A link with an icon in front ("Report a bug").
    class IconLink : ThemedControl
    {
        public string IconName;
        public IconLink(string icon, string text) { IconName = icon; Text = text; Cursor = Cursors.Hand; TabStop = true; }

        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            float isz = 16 * S;
            Icons.Draw(g, IconName, new RectangleF(0, (Height - isz) / 2, isz, isz), Pal.AccentText);
            using (Font f = Hot ? new Font(Ui.Font(13, false), FontStyle.Underline) : null)
                TextRenderer.DrawText(g, Text, f ?? Ui.Font(13, false), new Rectangle((int)(isz + 6 * S), 0, Width - (int)(isz + 6 * S), Height), Pal.AccentText, Ui.Left);
        }
    }
}

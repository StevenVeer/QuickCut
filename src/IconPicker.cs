using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickCut
{
    // The Icon card of Shortcut and Website buttons: automatic, the label's first letter or a line icon,
    // and below them your own images with an Upload button. Value is RingItem.Icon.
    class IconPicker : ThemedControl
    {
        const int Cols = 9;
        public bool Website;              // the automatic icon is the site's own icon, else the keyboard
        public Func<string> LabelText;    // the button's label: its first letter, and the name shown for an image
        public event EventHandler Changed;
        string value = "";
        List<string> images = new List<string>();
        readonly ToolTip tip = new ToolTip();
        string hotKey; // the hovered cell: an icon value, or "upload"

        public IconPicker() { Cursor = Cursors.Hand; }

        public string Value { get { return value; } set { this.value = value ?? ""; Invalidate(); } }

        public void Reload() { images = IconStore.All(); Invalidate(); }

        public static int PreferredHeight { get { return (int)Math.Round(214 * S); } }

        // The tiles: automatic, the letter, then the line icons (the keyboard is the automatic one).
        List<string> Tiles()
        {
            List<string> t = new List<string> { "", "letter" };
            foreach (string n in Icons.ButtonIcons) if (n != "keyboard") t.Add(n);
            return t;
        }

        string NameOf(string v)
        {
            if (v == "") return Website ? Str.IconSite : Str.IconKeyboard;
            if (v == "letter") return Str.IconLetter;
            if (v.StartsWith(IconStore.Prefix)) return LabelText != null ? LabelText() : "";
            int i = Array.IndexOf(Icons.ButtonIcons, v);
            return i >= 0 ? Str.IconNames[i] : "";
        }

        // Every clickable area with what it is.
        List<KeyValuePair<RectangleF, string>> Cells(Graphics g)
        {
            List<KeyValuePair<RectangleF, string>> list = new List<KeyValuePair<RectangleF, string>>();
            float pad = 16 * S, top = 14 * S + 30 * S, gap = 6 * S, size = 34 * S;
            float cw = (Width - 2 * pad - gap * (Cols - 1)) / Cols;
            List<string> tiles = Tiles();
            for (int i = 0; i < tiles.Count; i++)
                list.Add(new KeyValuePair<RectangleF, string>(new RectangleF(pad + (i % Cols) * (cw + gap), top + (i / Cols) * (size + gap), cw, size), tiles[i]));

            // Your images: as many as fit next to the Upload button, newest last; the chosen one always shows.
            float y = top + 2 * size + gap + 25 * S;
            float lw = TextRenderer.MeasureText(g, Str.YourImages, Ui.Font(12, false), new Size(1000, 100), TextFormatFlags.NoPadding).Width + 10 * S;
            float x = pad + lw, uploadMin = 130 * S;
            int fit = Math.Max(0, (int)((Width - pad - uploadMin - x) / (size + gap)));
            List<string> shown = new List<string>(images);
            if (value.StartsWith(IconStore.Prefix) && !shown.Contains(value)) shown.Add(value);
            while (shown.Count > fit)
            {
                int drop = shown[0] == value ? 1 : 0;
                if (drop >= shown.Count) break;
                shown.RemoveAt(drop);
            }
            foreach (string img in shown)
            {
                list.Add(new KeyValuePair<RectangleF, string>(new RectangleF(x, y, size, size), img));
                x += size + gap;
            }
            list.Add(new KeyValuePair<RectangleF, string>(new RectangleF(x, y, Width - pad - x, size), "upload"));
            return list;
        }

        string At(Point p)
        {
            using (Graphics g = CreateGraphics())
                foreach (KeyValuePair<RectangleF, string> c in Cells(g)) if (c.Key.Contains(p)) return c.Value;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string k = At(e.Location);
            Cursor = k != null ? Cursors.Hand : Cursors.Default;
            if (k == hotKey) return;
            hotKey = k;
            tip.SetToolTip(this, k == null ? null : k == "upload" ? Str.UploadImageTip : NameOf(k));
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { hotKey = null; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            string k = At(e.Location);
            if (k == null) return;
            if (k == "upload") { Upload(); return; }
            Pick(k);
        }

        void Upload()
        {
            string file;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Str.UploadImageTitle;
                d.Filter = Str.ImageFilter;
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                file = d.FileName;
            }
            try
            {
                string v = IconStore.Import(file);
                Reload();
                Pick(v);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), ex.Message, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void Pick(string v)
        {
            value = v;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            Ui.Box(g, Inner, 12 * S, Pal.Card, Pal.Border);
            float pad = 16 * S;
            Rectangle head = new Rectangle((int)pad, (int)(14 * S), Width - (int)(2 * pad), (int)(20 * S));
            TextRenderer.DrawText(g, Str.IconTitle, Ui.Font(12, false), head, Pal.Muted, Ui.Left);
            TextRenderer.DrawText(g, NameOf(value), Ui.Font(12, false), head, Pal.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            List<KeyValuePair<RectangleF, string>> cells = Cells(g);
            string label = LabelText != null ? LabelText() : "";
            string letter = label.Length > 0 ? label.Substring(0, 1).ToUpperInvariant() : "T";
            float divider = 0;
            foreach (KeyValuePair<RectangleF, string> c in cells)
            {
                RectangleF r = c.Key;
                string k = c.Value;
                bool hot = k == hotKey;
                if (k == "upload")
                {
                    divider = r.Y - 13 * S;
                    using (GraphicsPath p = Round(r, 9 * S))
                    {
                        if (hot) using (SolidBrush b = new SolidBrush(Pal.Hover)) g.FillPath(b, p);
                        using (Pen pen = new Pen(Pal.Muted, S) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, p);
                    }
                    Font f = Ui.Font(12, false);
                    int tw = TextRenderer.MeasureText(g, Str.UploadImage, f, new Size(1000, 100), TextFormatFlags.NoPadding).Width;
                    float iw = 15 * S, x = r.X + (r.Width - (iw + 6 * S + tw)) / 2;
                    Icons.Get("upload").Draw(g, new RectangleF(x, r.Y + (r.Height - iw) / 2, iw, iw), Pal.Text, 2f, false);
                    TextRenderer.DrawText(g, Str.UploadImage, f, new Rectangle((int)(x + iw + 6 * S), (int)r.Y, tw + 4, (int)r.Height), Pal.Text, Ui.Left);
                    continue;
                }
                bool sel = k == value;
                Color fill = sel ? Theme.Mix(Pal.Field, Pal.Accent, Pal.Dark ? 0.13f : 0.12f) : hot ? Pal.Hover : Pal.Field;
                Ui.Box(g, r, 9 * S, fill, sel ? Pal.Accent : hot ? Pal.Muted : Pal.Border);
                Color ink = sel ? (Pal.Dark ? Pal.Accent : Pal.AccentText) : Pal.Text;
                RectangleF ir = new RectangleF(r.X + (r.Width - 18 * S) / 2, r.Y + (r.Height - 18 * S) / 2, 18 * S, 18 * S);
                if (k.StartsWith(IconStore.Prefix))
                {
                    Bitmap b = IconStore.Load(k);
                    if (b != null)
                    {
                        RectangleF big = RectangleF.Inflate(ir, 1 * S, 1 * S);
                        float q = Math.Min(big.Width / b.Width, big.Height / b.Height);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(b, big.X + (big.Width - b.Width * q) / 2, big.Y + (big.Height - b.Height * q) / 2, b.Width * q, b.Height * q);
                    }
                }
                else if (k == "letter")
                    TextRenderer.DrawText(g, letter, Ui.Font(14, true), Rectangle.Round(r), ink, Ui.Center);
                else
                    Icons.Draw(g, k == "" ? (Website ? "globe" : "keyboard") : k, ir, ink);
            }
            if (divider > 0)
            {
                using (Pen line = new Pen(Pal.Border)) g.DrawLine(line, pad, divider, Width - pad, divider);
                TextRenderer.DrawText(g, Str.YourImages, Ui.Font(12, false),
                    new Rectangle((int)pad, (int)(divider + 13 * S), 200, (int)(34 * S)), Pal.Muted, Ui.Left);
                TextRenderer.DrawText(g, Str.ImageHint, Ui.Font(11, false),
                    new Rectangle((int)pad, (int)(divider + 53 * S), Width - (int)(2 * pad), (int)(18 * S)), Pal.Muted, Ui.Left);
            }
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Reload(); }

        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
}

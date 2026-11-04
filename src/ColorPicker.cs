using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickCut
{
    // Pick any color: a saturation/brightness square, a hue bar and a hex field,
    // with the current and the new color side by side. Drawn in the app's theme.
    class ColorPickerForm : Form
    {
        public Color Result;

        readonly Palette pal;
        readonly float sc;
        readonly Color original;
        readonly SvSquare square;
        readonly HueBar hueBar;
        readonly FieldBox hex;
        readonly Compare compare;
        float h, s, v;
        bool syncing;

        public ColorPickerForm(Palette pal, Color start)
        {
            this.pal = pal;
            sc = ThemedControl.S;
            original = start;
            Theme.ToHsv(start, out h, out s, out v);

            Text = Str.PickColorTitle;
            Font = new Font("Segoe UI", 9.5f * 96f / 72f * sc, GraphicsUnit.Pixel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(320), S(338));
            BackColor = pal.Window;
            KeyPreview = true;

            square = Add(new SvSquare(this), 16, 16, 288, 180);
            hueBar = Add(new HueBar(this), 16, 206, 288, 20);
            hex = Add(new FieldBox(new TextBox()), 16, 238, 136, 34);
            hex.Box.Font = new Font("Consolas", 10f * 96f / 72f * sc, GraphicsUnit.Pixel);
            hex.Box.MaxLength = 7;
            hex.Box.TextChanged += delegate
            {
                if (syncing) return;
                Color? c = Theme.ParseHex(hex.Box.Text);
                if (c.HasValue) SetColor(c.Value, false);
            };
            hex.Box.Leave += delegate { Sync(true); };
            compare = Add(new Compare(this), 164, 238, 140, 34);

            FlatButton ok = Add(new FlatButton(Str.Done, FlatButton.Kind.Normal), 112, 288, 92, 34);
            ok.Click += delegate { Accept(); };
            FlatButton cancel = Add(new FlatButton(Str.Cancel, FlatButton.Kind.Normal), 212, 288, 92, 34);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };

            Sync(true);
        }

        int S(float x) { return (int)Math.Round(x * sc); }

        T Add<T>(T c, float x, float y, float w, float hh) where T : ThemedControl
        {
            c.Bounds = new Rectangle(S(x), S(y), S(w), S(hh));
            Controls.Add(c);
            c.SetPalette(pal);
            return c;
        }

        Color Current { get { return Theme.FromHsv(h, s, v); } }

        void SetColor(Color c, bool writeHex)
        {
            float nh, ns, nv;
            Theme.ToHsv(c, out nh, out ns, out nv);
            if (ns > 0) h = nh; // greys have no hue: keep the one on the bar
            s = ns;
            v = nv;
            Sync(writeHex);
        }

        void Accept()
        {
            Result = Current;
            DialogResult = DialogResult.OK;
        }

        // writeHex: false while the user is typing in the hex field, so their text isn't replaced.
        void Sync(bool writeHex)
        {
            if (writeHex)
            {
                syncing = true;
                hex.Box.Text = Theme.ToHex(Current);
                syncing = false;
            }
            square.Invalidate();
            hueBar.Invalidate();
            compare.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(Handle, pal.Dark);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            else if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = e.SuppressKeyPress = true; }
        }

        static void Knob(Graphics g, PointF c, float r, Color fill, float sc)
        {
            RectangleF k = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
            using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, k);
            using (Pen p = new Pen(Color.White, 2 * sc)) g.DrawEllipse(p, k);
            using (Pen p = new Pen(Color.FromArgb(140, 0, 0, 0), 1)) g.DrawEllipse(p, RectangleF.Inflate(k, 1.5f * sc, 1.5f * sc));
        }

        // Saturation left to right, brightness bottom to top, for the current hue.
        class SvSquare : ThemedControl
        {
            readonly ColorPickerForm f;
            public SvSquare(ColorPickerForm f) { this.f = f; Cursor = Cursors.Cross; }

            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Track(e); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Track(e); }

            void Track(MouseEventArgs e)
            {
                f.s = Math.Max(0f, Math.Min(1f, e.X / (float)(Width - 1)));
                f.v = 1f - Math.Max(0f, Math.Min(1f, e.Y / (float)(Height - 1)));
                f.Sync(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = Prepare(e);
                RectangleF r = Inner;
                using (GraphicsPath p = Round(r, 6 * S))
                {
                    g.SetClip(p);
                    using (SolidBrush b = new SolidBrush(Theme.FromHsv(f.h, 1, 1))) g.FillRectangle(b, r);
                    RectangleF big = RectangleF.Inflate(r, 1, 1); // avoids a seam at the gradient edge
                    using (LinearGradientBrush b = new LinearGradientBrush(big, Color.White, Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Horizontal))
                        g.FillRectangle(b, r);
                    using (LinearGradientBrush b = new LinearGradientBrush(big, Color.FromArgb(0, 0, 0, 0), Color.Black, LinearGradientMode.Vertical))
                        g.FillRectangle(b, r);
                    g.ResetClip();
                    using (Pen pen = new Pen(Pal.Border)) g.DrawPath(pen, p);
                }
                PointF c = new PointF(f.s * (Width - 1), (1 - f.v) * (Height - 1));
                Knob(g, c, 7 * S, f.Current, S);
            }
        }

        // The hue, red through the rainbow back to red.
        class HueBar : ThemedControl
        {
            readonly ColorPickerForm f;
            public HueBar(ColorPickerForm f) { this.f = f; Cursor = Cursors.Hand; }

            float R { get { return Height / 2f - 1; } } // knob radius; the bar runs between the knob centers' limits

            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Track(e); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Track(e); }

            void Track(MouseEventArgs e)
            {
                float t = (e.X - R) / (Width - 2 * R);
                f.h = Math.Max(0f, Math.Min(1f, t)) * 359.9f;
                f.Sync(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = Prepare(e);
                float bh = 12 * S;
                RectangleF bar = new RectangleF(R, (Height - bh) / 2, Width - 2 * R, bh);
                using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(bar, 1, 0), Color.Red, Color.Red, LinearGradientMode.Horizontal))
                {
                    ColorBlend cb = new ColorBlend();
                    cb.Colors = new Color[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };
                    cb.Positions = new float[] { 0, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1 };
                    b.InterpolationColors = cb;
                    using (GraphicsPath p = Round(bar, bh / 2)) g.FillPath(b, p);
                }
                PointF c = new PointF(bar.X + f.h / 360f * bar.Width, Height / 2f);
                Knob(g, c, R - 2 * S, Theme.FromHsv(f.h, 1, 1), S);
            }
        }

        // The color it was and the color it will be, side by side. Click "Current" to go back to it.
        class Compare : ThemedControl
        {
            readonly ColorPickerForm f;
            public Compare(ColorPickerForm f) { this.f = f; Cursor = Cursors.Hand; }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (e.X < Width / 2) f.SetColor(f.original, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = Prepare(e);
                RectangleF r = Inner;
                Color now = f.Current;
                using (GraphicsPath p = Round(r, 6 * S))
                {
                    g.SetClip(p);
                    RectangleF left = new RectangleF(r.X, r.Y, r.Width / 2, r.Height);
                    RectangleF right = new RectangleF(r.X + r.Width / 2, r.Y, r.Width / 2 + 1, r.Height);
                    using (SolidBrush b = new SolidBrush(f.original)) g.FillRectangle(b, left);
                    using (SolidBrush b = new SolidBrush(now)) g.FillRectangle(b, right);
                    g.ResetClip();
                    using (Pen pen = new Pen(Pal.Border)) g.DrawPath(pen, p);
                    using (Font small = new Font(Font.FontFamily, Font.Size * 0.85f, Font.Style, Font.Unit))
                    {
                        DrawCentered(g, Str.CurrentColor, small, Rectangle.Round(left), Theme.TextOn(f.original));
                        DrawCentered(g, Str.NewColor, small, Rectangle.Round(right), Theme.TextOn(now));
                    }
                }
            }
        }
    }
}

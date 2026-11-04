using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace QuickCut
{
    // Geometry, hit testing and drawing of the ring. Shared by the real ring and the settings preview.
    class RingRenderer : IDisposable
    {
        public const int HitNone = -1, HitCenter = -2;

        public readonly float Sc;
        public readonly int Size;
        public readonly float Cx, Cy, ROuter, RInner, RCenter;
        readonly Font labelFont;

        public RingRenderer(float scale) : this(scale, 1f) { }

        // sizeFactor: the chosen ring size (Small = 0.8), already included in scale.
        public RingRenderer(float scale, float sizeFactor)
        {
            Sc = scale;
            Size = (int)Math.Round(320 * scale);
            Cx = Cy = Size / 2f;
            ROuter = 148 * scale;
            bool small = sizeFactor < 1f;
            // On Small: a narrower gap around the center, and text that shrinks less than the ring
            // so labels stay readable.
            RInner = (small ? 63 : 66) * scale;
            RCenter = (small ? 59 : 57) * scale;
            float textScale = small ? scale / sizeFactor * 0.92f : scale;
            labelFont = new Font("Segoe UI", 11.5f * textScale, GraphicsUnit.Pixel);
        }

        // directional: only the angle matters (used while holding the trigger).
        public int HitTest(PointF p, int count, bool directional)
        {
            float dx = p.X - Cx, dy = p.Y - Cy;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= RCenter) return HitCenter;
            if (count == 0 || d < RInner - 4 * Sc || (d > ROuter && !directional)) return HitNone;
            double sweep = 360.0 / count;
            double rel = Math.Atan2(dy, dx) * 180 / Math.PI + 90 + sweep / 2;
            rel = ((rel % 360) + 360) % 360;
            return (int)(rel / sweep) % count;
        }

        public bool Contains(PointF p)
        {
            float dx = p.X - Cx, dy = p.Y - Cy;
            return dx * dx + dy * dy <= ROuter * ROuter;
        }

        public static Color ButtonColor(Config cfg, RingItem it, Palette pal)
        {
            Color? c = Theme.ParseHex(it.ColorHex);
            if (c.HasValue) return c.Value;
            c = Theme.ParseHex(cfg.ButtonColor);
            return c.HasValue ? c.Value : pal.Button;
        }

        struct Look { public Color Fill, Fg; public float Grow; public bool Outline; }

        Look GetLook(Config cfg, RingItem it, Palette pal, bool hot)
        {
            Look l = new Look();
            l.Fill = ButtonColor(cfg, it, pal);
            if (hot)
            {
                switch (cfg.HoverStyle)
                {
                    case "invert":
                        l.Fill = Theme.Lum(l.Fill) > 0.55f ? Color.FromArgb(32, 32, 36) : Color.FromArgb(245, 245, 247);
                        break;
                    case "outline":
                        l.Outline = true;
                        break;
                    case "grow":
                        l.Fill = pal.Accent;
                        l.Grow = 7 * Sc;
                        break;
                    default:
                        l.Fill = pal.Accent;
                        break;
                }
            }
            l.Fg = Theme.TextOn(l.Fill);
            return l;
        }

        // hover / selected: HitNone, HitCenter or a segment index. The selected button (settings preview)
        // gets the hover look plus a dashed outline, so it stands out from one that's merely hovered.
        public void Draw(Graphics g, Config cfg, Palette pal, int hover, int selected, bool shadow)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int n = cfg.Items.Count;
            float sweep = n > 0 ? 360f / n : 360f;

            if (shadow)
                using (SolidBrush sh = new SolidBrush(Color.FromArgb(pal.Dark ? 16 : 10, 0, 0, 0)))
                    for (int i = 1; i <= 5; i++)
                    {
                        float r = ROuter + i * 1.8f * Sc;
                        g.FillEllipse(sh, Cx - r, Cy - r + 2 * Sc, 2 * r, 2 * r);
                    }

            using (GraphicsPath ring = Art.Donut(Cx, Cy, ROuter, RInner))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(245, pal.Ring)))
                g.FillPath(b, ring);
            using (Pen rim = new Pen(pal.Dark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), Sc))
                g.DrawEllipse(rim, Cx - ROuter, Cy - ROuter, 2 * ROuter, 2 * ROuter);

            float ro = ROuter - 4 * Sc, ri = RInner + 4 * Sc;
            float gapDeg = n > 1 ? (float)(5 * Sc / ((ro + ri) / 2) * 180 / Math.PI) : 0;
            float rMid = (ROuter + RInner) / 2;
            float iconSz = (n > 8 ? 24 : 30) * Sc;
            float labelW = Math.Min(86 * Sc, n > 1 ? (float)(2 * rMid * Math.Sin(Math.PI / n)) - 6 * Sc : 86 * Sc);
            float lh = labelFont.GetHeight(g) + 1;  // label box follows the font, so nothing gets clipped
            float block = iconSz + 3 * Sc + lh;      // icon + label, centered on the segment

            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.Alignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;

                for (int i = 0; i < n; i++)
                {
                    RingItem it = cfg.Items[i];
                    Look l = GetLook(cfg, it, pal, i == hover || i == selected);
                    float start = -90 + i * sweep - sweep / 2 + gapDeg / 2;
                    using (GraphicsPath p = n == 1 ? Art.Donut(Cx, Cy, ro + l.Grow, ri) : Art.Sector(Cx, Cy, ro + l.Grow, ri, start, sweep - gapDeg))
                    {
                        using (SolidBrush b = new SolidBrush(l.Fill)) g.FillPath(b, p);
                        Outline(g, p, pal, l.Outline, i == selected);
                    }
                    double a = (-90 + i * sweep) * Math.PI / 180;
                    float px = Cx + (float)Math.Cos(a) * (rMid + l.Grow / 2), py = Cy + (float)Math.Sin(a) * (rMid + l.Grow / 2);
                    RectangleF ir = new RectangleF(px - iconSz / 2, py - block / 2, iconSz, iconSz);
                    Art.DrawItem(g, it, ir, l.Fg);
                    RectangleF lr = new RectangleF(px - labelW / 2, ir.Bottom + 3 * Sc, labelW, lh);
                    using (SolidBrush tb = new SolidBrush(l.Fg)) g.DrawString(it.Label, labelFont, tb, lr, sf);
                }

                // Center button
                Look c = GetLook(cfg, cfg.Center, pal, hover == HitCenter || selected == HitCenter);
                float rc = RCenter + c.Grow / 2;
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddEllipse(Cx - rc, Cy - rc, 2 * rc, 2 * rc);
                    using (SolidBrush b = new SolidBrush(c.Fill)) g.FillPath(b, p);
                    using (Pen rim = new Pen(pal.Dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(35, 0, 0, 0), Sc))
                        g.DrawPath(rim, p);
                    Outline(g, p, pal, c.Outline, selected == HitCenter);
                }
                float gs = 34 * Sc;
                RectangleF gr = new RectangleF(Cx - gs / 2, Cy - (gs + 3 * Sc + lh) / 2, gs, gs);
                Art.DrawItem(g, cfg.Center, gr, c.Fg);
                RectangleF clr = new RectangleF(Cx - RCenter + 6 * Sc, gr.Bottom + 3 * Sc, 2 * RCenter - 12 * Sc, lh);
                using (SolidBrush tb = new SolidBrush(c.Fg)) g.DrawString(cfg.Center.Label, labelFont, tb, clr, sf);
            }
        }

        void Outline(Graphics g, GraphicsPath p, Palette pal, bool hoverOutline, bool selected)
        {
            if (hoverOutline)
                using (Pen pen = new Pen(pal.Accent, 3 * Sc)) g.DrawPath(pen, p);
            if (selected)
                using (Pen pen = new Pen(pal.Text, 2 * Sc) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, p);
        }

        public void Dispose() { labelFont.Dispose(); }
    }
}

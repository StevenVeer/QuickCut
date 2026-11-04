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

        // Volume panel hits (HitVolume); a value >= 0 is the index of a block.
        public const int VolNone = -1, VolDown = -2, VolUp = -3, VolPanel = -4;
        public const float PanelExtra = 56; // unscaled height the volume panel adds below the ring

        public readonly float Sc;
        public readonly int Size;   // width, and the height of the ring part
        public readonly int Height; // Size plus the volume panel, when shown
        public readonly bool VolumeBar;
        public readonly float Cx, Cy, ROuter, RInner, RCenter;
        readonly Font labelFont;

        public RingRenderer(float scale) : this(scale, 1f, false) { }

        // sizeFactor: the chosen ring size (Small = 0.8), already included in scale.
        public RingRenderer(float scale, float sizeFactor, bool volumeBar)
        {
            Sc = scale;
            Size = (int)Math.Round(320 * scale);
            VolumeBar = volumeBar;
            Height = volumeBar ? (int)Math.Round((320 + PanelExtra) * scale) : Size;
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
            return dx * dx + dy * dy <= ROuter * ROuter || (VolumeBar && PanelRect.Contains(p));
        }

        // ---------- volume panel ----------

        RectangleF PanelRect { get { return new RectangleF(Cx - 134 * Sc, Size + 2 * Sc, 268 * Sc, 50 * Sc); } }
        PointF MinusAt { get { return new PointF(Cx - 110 * Sc, PanelRect.Top + 25 * Sc); } }
        PointF PlusAt { get { return new PointF(Cx + 110 * Sc, PanelRect.Top + 25 * Sc); } }

        // One block per step: 20 blocks for 5%, 10 for 10%.
        void BlockLayout(int step, out int count, out float x0, out float bw, out float gap)
        {
            count = 100 / step;
            gap = (count > 10 ? 2.5f : 4f) * Sc;
            float area = 172 * Sc;
            x0 = Cx - area / 2;
            bw = (area - gap * (count - 1)) / count;
        }

        public int HitVolume(PointF p, int step)
        {
            if (!VolumeBar) return VolNone;
            RectangleF pr = PanelRect;
            if (!pr.Contains(p)) return VolNone;
            float r = 17 * Sc;
            if (Dist(p, MinusAt) <= r) return VolDown;
            if (Dist(p, PlusAt) <= r) return VolUp;
            int count; float x0, bw, gap;
            BlockLayout(step, out count, out x0, out bw, out gap);
            if (p.Y >= pr.Top + 20 * Sc && p.X >= x0 - gap / 2 && p.X < x0 + count * (bw + gap) - gap / 2)
                return Math.Max(0, Math.Min(count - 1, (int)((p.X - x0 + gap / 2) / (bw + gap))));
            return VolPanel;
        }

        static float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        // available: false when Windows has no output device. level: 0..1.
        public void DrawVolume(Graphics g, Config cfg, Palette pal, int hot, bool available, float level, bool muted)
        {
            if (!VolumeBar) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF pr = PanelRect;
            using (GraphicsPath p = ThemedControl.Round(pr, 12 * Sc))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(245, pal.Ring))) g.FillPath(b, p);
                using (Pen rim = new Pen(pal.Dark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), Sc)) g.DrawPath(rim, p);
            }

            // - and + buttons, in the button color, with the hover color when hot
            Color? custom = Theme.ParseHex(cfg.ButtonColor);
            Color btn = custom.HasValue ? custom.Value : pal.Button;
            for (int i = 0; i < 2; i++)
            {
                PointF c = i == 0 ? MinusAt : PlusAt;
                bool isHot = hot == (i == 0 ? VolDown : VolUp);
                Color fill = isHot ? pal.Accent : btn;
                float r = 15 * Sc;
                using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
                using (Pen rim = new Pen(pal.Dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(35, 0, 0, 0), Sc))
                    g.DrawEllipse(rim, c.X - r, c.Y - r, 2 * r, 2 * r);
                using (Pen pen = new Pen(Theme.TextOn(fill), 1.8f * Sc) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float a = 5.5f * Sc;
                    g.DrawLine(pen, c.X - a, c.Y, c.X + a, c.Y);
                    if (i == 1) g.DrawLine(pen, c.X, c.Y - a, c.X, c.Y + a);
                }
            }

            // Blocks, growing a little in height; a block can be part lit when the level sits between steps.
            int count; float x0, bw, gap;
            BlockLayout(cfg.VolumeStep, out count, out x0, out bw, out gap);
            Color track = Theme.Mix(pal.Ring, pal.Text, pal.Dark ? 0.16f : 0.14f);
            float v = available && !muted ? level : 0, bottom = pr.Top + 42 * Sc, rad = (count > 10 ? 1.5f : 2.5f) * Sc;
            for (int i = 0; i < count; i++)
            {
                float h = (8 + i * 12f / (count - 1)) * Sc, x = x0 + i * (bw + gap);
                RectangleF br = new RectangleF(x, bottom - h, bw, h);
                using (GraphicsPath p = ThemedControl.Round(br, rad))
                using (SolidBrush b = new SolidBrush(track)) g.FillPath(b, p);
                float lit = Math.Max(0, Math.Min(1, v * count - i));
                if (lit > 0.02f)
                    using (GraphicsPath p = ThemedControl.Round(new RectangleF(x, br.Y, bw * lit, h), Math.Min(rad, bw * lit / 2)))
                    using (SolidBrush b = new SolidBrush(pal.Accent)) g.FillPath(b, p);
            }

            string text = !available ? Str.VolumeUnavailable : muted ? Str.VolumeMuted
                : string.Format(Str.VolumeLevel, (int)Math.Round(level * 100));
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center })
            using (SolidBrush tb = new SolidBrush(muted || !available ? pal.Muted : pal.Text))
                g.DrawString(text, labelFont, tb, new RectangleF(x0, pr.Top + 4 * Sc, 172 * Sc, labelFont.GetHeight(g) + 1), sf);
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

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickCut
{
    // Geometry, hit testing and drawing of the ring. Shared by the real ring and the settings preview.
    class RingRenderer : IDisposable
    {
        public const int HitNone = -1, HitCenter = -2;

        // Volume panel hits (HitVolume); a value >= 0 is the index of a block.
        public const int VolNone = -1, VolDown = -2, VolUp = -3, VolPanel = -4;
        public const float PanelExtra = 56; // unscaled height each panel (volume, media) adds below the ring

        // Media panel hits (HitMedia).
        public const int MedNone = -1, MedPanel = -2, MedPrev = -3, MedPlay = -4, MedNext = -5;

        public readonly float Sc;
        public readonly int Size;   // width, and the height of the ring part
        public readonly int Height; // Size plus the panels that are turned on
        public readonly bool VolumeBar, MediaBar;
        public readonly float Cx, Cy, ROuter, RInner, RCenter;
        readonly Font labelFont, panelFont, smallFont;

        // The media panel only shows (and takes clicks) while something is playing.
        public bool MediaShown;

        public RingRenderer(float scale) : this(scale, 1f, false, false, 0) { }

        public RingRenderer(float scale, float sizeFactor, bool volumeBar, bool mediaBar) : this(scale, sizeFactor, volumeBar, mediaBar, 0) { }

        // sizeFactor: the chosen ring size (Small = 0.8), already included in scale.
        // minPanelText: the smallest scale for the panels' text (the settings preview draws the ring small,
        // but its panel text should stay readable); 0 = no minimum.
        // minLabelText: the same for the button labels.
        public RingRenderer(float scale, float sizeFactor, bool volumeBar, bool mediaBar, float minPanelText)
            : this(scale, sizeFactor, volumeBar, mediaBar, minPanelText, 0) { }

        public RingRenderer(float scale, float sizeFactor, bool volumeBar, bool mediaBar, float minPanelText, float minLabelText)
        {
            Sc = scale;
            Size = (int)Math.Round(320 * scale);
            VolumeBar = volumeBar;
            MediaBar = mediaBar;
            Height = (int)Math.Round((320 + (volumeBar ? PanelExtra : 0) + (mediaBar ? PanelExtra : 0)) * scale);
            Cx = Cy = Size / 2f;
            ROuter = 148 * scale;
            bool small = sizeFactor < 1f;
            // On Small: a narrower gap around the center, and text that shrinks less than the ring
            // so labels stay readable.
            RInner = (small ? 63 : 66) * scale;
            RCenter = (small ? 59 : 57) * scale;
            float textScale = small ? scale / sizeFactor * 0.92f : scale;
            labelFont = new Font("Segoe UI", 11.5f * Math.Max(textScale, minLabelText), GraphicsUnit.Pixel);
            float panelScale = Math.Max(textScale, minPanelText);
            panelFont = new Font("Segoe UI Semibold", 12f * panelScale, GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", 11.5f * panelScale, GraphicsUnit.Pixel);
        }

        // Unscaled panel height + gap, for sizing the settings preview.
        public static float PanelsExtra(bool volumeBar, bool mediaBar)
        {
            return (volumeBar ? PanelExtra : 0) + (mediaBar ? PanelExtra : 0);
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
            return dx * dx + dy * dy <= ROuter * ROuter || (VolumeBar && PanelRect.Contains(p))
                || (MediaBar && MediaShown && MediaRect.Contains(p));
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
                // Fully opaque, so the text on it can use sharp ClearType (see PanelText).
                using (SolidBrush b = new SolidBrush(pal.Ring)) g.FillPath(b, p);
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
            g.TextRenderingHint = PanelText;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center })
            using (SolidBrush tb = new SolidBrush(muted || !available ? pal.Muted : pal.Text))
            {
                // Centered in the space above the blocks; the font may be larger than Sc says (preview).
                float fh = panelFont.GetHeight(g) + 1, top = Math.Max(pr.Top + 1 * Sc, pr.Top + 12 * Sc - fh / 2);
                g.DrawString(text, panelFont, tb, new RectangleF(x0 - 20 * Sc, top, 212 * Sc, fh), sf);
            }
        }

        // Panel text uses ClearType, like normal Windows text: much sharper than the grey smoothing a
        // see-through window gets. That only works on opaque pixels, so the panels are drawn fully opaque.
        const TextRenderingHint PanelText = TextRenderingHint.ClearTypeGridFit;

        // ---------- media panel ----------

        // Right under the volume panel, or in its place when volume is off.
        RectangleF MediaRect
        {
            get { return new RectangleF(Cx - 134 * Sc, Size + 2 * Sc + (VolumeBar ? PanelExtra * Sc : 0), 268 * Sc, 50 * Sc); }
        }

        PointF MediaBtnAt(int which) // 0 prev, 1 play/pause, 2 next
        {
            RectangleF r = MediaRect;
            return new PointF(r.Right - (86 - which * 30) * Sc, r.Top + 25 * Sc);
        }

        public int HitMedia(PointF p)
        {
            if (!MediaBar || !MediaShown || !MediaRect.Contains(p)) return MedNone;
            for (int i = 0; i < 3; i++)
                if (Dist(p, MediaBtnAt(i)) <= 15 * Sc) return i == 0 ? MedPrev : i == 1 ? MedPlay : MedNext;
            return MedPanel;
        }

        // info: null draws nothing (in the ring) or a placeholder (placeholder = true, settings preview).
        public void DrawMedia(Graphics g, Config cfg, Palette pal, int hot, MediaInfo info, bool placeholder)
        {
            TitleScrolls = false;
            if (!MediaBar || (info == null && !placeholder)) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = PanelText;
            RectangleF pr = MediaRect;
            using (GraphicsPath p = ThemedControl.Round(pr, 12 * Sc))
            {
                using (SolidBrush b = new SolidBrush(pal.Ring)) g.FillPath(b, p); // opaque: see PanelText
                using (Pen rim = new Pen(pal.Dark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), Sc)) g.DrawPath(rim, p);
            }

            // Cover art, or a note on a plain tile
            RectangleF tr = new RectangleF(pr.Left + 7 * Sc, pr.Top + 7 * Sc, 36 * Sc, 36 * Sc);
            using (GraphicsPath tp = ThemedControl.Round(tr, 7 * Sc))
            {
                if (info != null && info.Thumb != null)
                {
                    GraphicsState st = g.Save();
                    g.SetClip(tp);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(info.Thumb, tr);
                    g.Restore(st);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Mix(pal.Ring, pal.Text, pal.Dark ? 0.16f : 0.12f))) g.FillPath(b, tp);
                    DrawNote(g, tr, pal.Muted);
                }
            }

            // Title and artist (or the app's name)
            // Whole pixels, so the scrolling title and its fade line up exactly at every ring size.
            float tx = (float)Math.Round(tr.Right + 9 * Sc), tw = (float)Math.Floor(MediaBtnAt(0).X - 19 * Sc - tx);
            string title = info != null ? info.Title : Str.NothingPlaying;
            string sub = info == null ? "" : info.Artist.Length > 0 ? info.Artist : info.App;
            // Typographic like the title: the default format adds a small gap before the first letter,
            // which made the second line start a pixel or two right of the title.
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                float lh = panelFont.GetHeight(g), sh = smallFont.GetHeight(g);
                float ty = sub.Length > 0 ? pr.Top + 25 * Sc - (lh + sh) / 2 : pr.Top + 25 * Sc - lh / 2;
                using (SolidBrush tb = new SolidBrush(info != null ? pal.Text : pal.Muted))
                {
                    float full = g.MeasureString(title, panelFont, PointF.Empty, StringFormat.GenericTypographic).Width;
                    TitleScrolls = Animate && info != null && full > tw;
                    // A long title is drawn the same way scrolling or not, so it doesn't jump when it starts.
                    if (full > tw) DrawLongTitle(g, title, tb, pal, new RectangleF(tx, ty, tw, lh + 1), full, TitleScrolls);
                    else g.DrawString(title, panelFont, tb, tx, ty, StringFormat.GenericTypographic);
                }
                // The second line in a softer tone, but stronger than Muted so it stays readable.
                if (sub.Length > 0)
                    using (SolidBrush mb = new SolidBrush(Theme.Mix(pal.Text, pal.Ring, 0.3f)))
                        g.DrawString(sub, smallFont, mb, new RectangleF(tx, ty + lh, tw, sh + 1), sf);
            }

            // Previous, play/pause, next
            Color? custom = Theme.ParseHex(cfg.ButtonColor);
            Color btn = custom.HasValue ? custom.Value : pal.Button;
            for (int i = 0; i < 3; i++)
            {
                PointF c = MediaBtnAt(i);
                int code = i == 0 ? MedPrev : i == 1 ? MedPlay : MedNext;
                Color fill = hot == code ? pal.Accent : btn;
                float r = (i == 1 ? 14 : 12.5f) * Sc;
                using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
                using (Pen rim = new Pen(pal.Dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(35, 0, 0, 0), Sc))
                    g.DrawEllipse(rim, c.X - r, c.Y - r, 2 * r, 2 * r);
                Color fg = info == null ? Color.FromArgb(120, Theme.TextOn(fill)) : Theme.TextOn(fill);
                if (i == 1) DrawPlayPause(g, c, info != null && info.Playing, fg);
                else DrawSkip(g, c, i == 2, fg);
            }
        }

        // ---------- scrolling title ----------

        // Animate: scroll a title that doesn't fit (the ring sets it while the mouse is over the panel);
        // otherwise it's cut off with "…".
        public bool Animate;
        // True after DrawMedia when the title is scrolling, so the window keeps redrawing.
        public bool TitleScrolls;
        string scrollTitle;
        int scrollFrame;

        // Starts the title from the beginning (the mouse came onto the panel).
        public void ResetScroll() { scrollTitle = null; scrollFrame = 0; }

        // One animation frame passed (the ring's scroll timer, ~64 per second). Counting frames instead
        // of reading the clock makes every step the same size, so the motion is even.
        public void ScrollTick() { scrollFrame++; }

        // A title too long for its space. Still: the start, with "…". Scrolling: a short rest, slide to the
        // end, rest, slide back (faster), repeat. The "…" stays on the right while letters are hidden there.
        void DrawLongTitle(Graphics g, string title, Brush tb, Palette pal, RectangleF r, float full, bool scrolling)
        {
            const string dots = "…";
            float dotsW = g.MeasureString(dots, panelFont, PointF.Empty, StringFormat.GenericTypographic).Width + 1 * Sc;
            float maxOff = full - r.Width;   // here the last letter reaches the right edge
            float off = 0;
            if (scrolling)
            {
                if (title != scrollTitle) { scrollTitle = title; scrollFrame = 0; }
                // In frames (~64 per second): rest, forward one even step every 2 frames, rest, back 4x as fast.
                const int restStart = 38, restEnd = 77;
                int step = Math.Max(1, (int)Math.Round(Sc)); // whole pixels: no shimmer
                int dist = (int)Math.Ceiling(maxOff);
                int fwd = 2 * ((dist + step - 1) / step), back = (dist + 2 * step - 1) / (2 * step);
                int f = scrollFrame % (restStart + fwd + restEnd + back);
                if (f < restStart) off = 0;
                else if ((f -= restStart) < fwd) off = (f / 2) * step;
                else if ((f -= fwd) < restEnd) off = dist;
                else off = dist - (f - restEnd) * 2 * step;
                off = Math.Max(0, Math.Min(dist, off));
            }
            // On the right only whole letters show, then "…" right after the last one, like normal cut-off
            // text. A letter appears once it fits completely; at the end the dots go and every letter shows.
            float[] ends = LetterEnds(g, title);
            int shown = title.Length;
            if (off < maxOff)
            {
                float room = r.Width - dotsW + off;
                while (shown > 0 && ends[shown - 1] > room) shown--;
                while (shown > 0 && char.IsWhiteSpace(title[shown - 1])) shown--; // no "word …"
            }

            GraphicsState st = g.Save();
            g.SetClip(r);
            g.DrawString(title.Substring(0, shown), panelFont, tb, r.X - off, r.Y, StringFormat.GenericTypographic);
            if (shown < title.Length)
                g.DrawString(dots, panelFont, tb, r.X - off + (shown > 0 ? ends[shown - 1] : 0), r.Y, StringFormat.GenericTypographic);
            // On the left, letters fade out as they scroll away.
            if (off > 0)
            {
                // Whole pixels, and the gradient must not repeat past its ends (that showed as a flickering line).
                int fx = (int)Math.Round(r.X), fw = Math.Max(4, (int)Math.Round(8 * Sc));
                int fy = (int)Math.Floor(r.Y), fh = (int)Math.Ceiling(r.Bottom) - fy;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (LinearGradientBrush lb = new LinearGradientBrush(new Rectangle(fx, fy, fw, fh), pal.Ring, Color.FromArgb(0, pal.Ring), 0f))
                {
                    lb.WrapMode = WrapMode.TileFlipX;
                    g.FillRectangle(lb, fx, fy, fw, fh);
                }
            }
            g.Restore(st);
        }

        // Where each letter of the title ends, measured once per title.
        string endsTitle;
        float[] endsCache;

        float[] LetterEnds(Graphics g, string title)
        {
            if (title == endsTitle) return endsCache;
            float[] ends = new float[title.Length];
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                for (int i = 0; i < title.Length; i++)
                    ends[i] = g.MeasureString(title.Substring(0, i + 1), panelFont, PointF.Empty, sf).Width;
            }
            endsTitle = title;
            endsCache = ends;
            return ends;
        }

        void DrawPlayPause(Graphics g, PointF c, bool playing, Color fg)
        {
            using (SolidBrush b = new SolidBrush(fg))
            {
                if (playing)
                {
                    float w = 2.8f * Sc, h = 10 * Sc, sp = 2.2f * Sc;
                    g.FillRectangle(b, c.X - sp - w, c.Y - h / 2, w, h);
                    g.FillRectangle(b, c.X + sp, c.Y - h / 2, w, h);
                }
                else
                {
                    float h = 11 * Sc, w = 9.5f * Sc, x = c.X - w / 2 + 1.2f * Sc;
                    g.FillPolygon(b, new[] { new PointF(x, c.Y - h / 2), new PointF(x + w, c.Y), new PointF(x, c.Y + h / 2) });
                }
            }
        }

        // A triangle and a bar: |◀ or ▶|
        void DrawSkip(Graphics g, PointF c, bool next, Color fg)
        {
            float h = 9 * Sc, w = 7 * Sc, bar = 2 * Sc;
            float d = next ? 1 : -1;
            float tipX = c.X + d * (w / 2), baseX = c.X - d * (w / 2);
            using (SolidBrush b = new SolidBrush(fg))
            {
                g.FillPolygon(b, new[] { new PointF(baseX - d * Sc, c.Y - h / 2), new PointF(tipX - d * Sc, c.Y), new PointF(baseX - d * Sc, c.Y + h / 2) });
                float bx = next ? tipX - Sc : tipX + Sc - bar;
                g.FillRectangle(b, bx, c.Y - h / 2, bar, h);
            }
        }

        void DrawNote(Graphics g, RectangleF r, Color fg)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (SolidBrush b = new SolidBrush(fg))
            using (Pen pen = new Pen(fg, 1.8f * Sc))
            {
                float hr = 3.6f * Sc;
                g.FillEllipse(b, cx - 4 * Sc - hr, cy + 5 * Sc - hr * 0.8f, 2 * hr, 1.6f * hr);
                float stemX = cx - 4 * Sc + hr - 0.9f * Sc;
                g.DrawLine(pen, stemX, cy + 5 * Sc, stemX, cy - 8 * Sc);
                g.DrawLine(pen, stemX, cy - 8 * Sc, cx + 6 * Sc, cy - 5 * Sc);
            }
        }

        public static Color ButtonColor(Config cfg, RingItem it, Palette pal)
        {
            Color? c = Theme.ParseHex(it.ColorHex);
            if (c.HasValue) return c.Value;
            c = Theme.ParseHex(cfg.ButtonColor);
            return c.HasValue ? c.Value : pal.Button;
        }

        // Outline: how strongly the accent outline shows (the "outline" hover style), 0..1.
        struct Look { public Color Fill, Fg; public float Grow, Outline; }

        // t: 0 = the normal look, 1 = fully hovered; in between while the hover fades in or out.
        // The label keeps its normal color on the hover color when it's still readable there, so the
        // text doesn't flip from white to black; only on a very light hover color does it turn dark.
        Look GetLook(Config cfg, RingItem it, Palette pal, float t)
        {
            Look n = BaseLook(cfg, it, pal, false);
            if (t <= 0) return n;
            Look h = BaseLook(cfg, it, pal, true);
            if (Theme.Contrast(n.Fg, h.Fill) >= KeepTextContrast) h.Fg = n.Fg;
            if (t >= 1) return h;
            t = t * t * (3 - 2 * t); // ease in and out
            Look l = new Look();
            l.Fill = Theme.Mix(n.Fill, h.Fill, t);
            l.Fg = Theme.Mix(n.Fg, h.Fg, t);
            l.Grow = h.Grow * t;
            l.Outline = h.Outline * t;
            return l;
        }

        // Lenient on purpose: white on the default orange (about 2.2:1) stays white, yellow turns dark.
        const float KeepTextContrast = 2f;

        Look BaseLook(Config cfg, RingItem it, Palette pal, bool hot)
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
                        l.Outline = 1;
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
            DrawBackground(g, pal, shadow);
            int n = cfg.Items.Count;
            Layout L = GetLayout(g, n);
            using (StringFormat sf = LabelFormat())
            {
                for (int i = 0; i < n; i++)
                    DrawSegment(g, cfg, pal, L, sf, i, i == hover || i == selected ? 1 : 0, i == selected);
                DrawCenter(g, cfg, pal, L, sf, hover == HitCenter || selected == HitCenter ? 1 : 0, selected == HitCenter);
            }
        }

        public void DrawHover(Graphics g, Config cfg, Palette pal, int hover, bool shadow)
        {
            DrawHover(g, cfg, pal, new[] { hover }, new[] { 1f }, shadow);
        }

        // Turns a frame that holds the ring with nothing hovered (Draw with hover = HitNone) into the
        // ring with buttons `ids` lit, each `ts` of the way into its hover look (a hover fading in and
        // the one fading out), by redrawing only those buttons instead of all of them.
        // Everything they drew in their normal look is wiped back to the bare ring first. Labels can
        // poke a little into a neighbour's wedge, so the neighbours are redrawn inside that area, in the
        // same order as Draw.
        public void DrawHover(Graphics g, Config cfg, Palette pal, int[] ids, float[] ts, bool shadow)
        {
            int n = cfg.Items.Count;
            float[] segT = new float[n];
            bool[] lit = new bool[n];
            float centerT = -1; // < 0: the center isn't lit
            Layout L = GetLayout(g, n);
            Region area = null;
            for (int k = 0; k < ids.Length; k++)
            {
                int id = ids[k];
                if (id == HitCenter) centerT = Math.Max(centerT, ts[k]);
                else if (id >= 0 && id < n) { lit[id] = true; segT[id] = Math.Max(segT[id], ts[k]); }
                else continue;
                Region a = HoverArea(L, id);
                if (area == null) area = a;
                else { area.Union(a); a.Dispose(); }
            }
            if (area == null) return;
            using (StringFormat sf = LabelFormat())
            using (area)
            {
                // Same modes for every step: the pixel offset mode shifts which pixels a clip covers.
                SetModes(g);
                g.SetClip(area, CombineMode.Replace);
                g.SmoothingMode = SmoothingMode.None;
                g.CompositingMode = CompositingMode.SourceCopy;
                // A rectangle through the clip, not FillRegion: that can disagree with the clip on edge pixels.
                using (SolidBrush clear = new SolidBrush(Color.Transparent)) g.FillRectangle(clear, -1, -1, Size + 2, Height + 2);
                g.CompositingMode = CompositingMode.SourceOver;
                DrawBackground(g, pal, shadow);
                // Lit buttons are drawn whole; their neighbours only inside the wiped area.
                bool[] redraw = new bool[n];
                for (int i = 0; i < n; i++)
                    if (lit[i]) { redraw[i] = true; redraw[(i + n - 1) % n] = true; redraw[(i + 1) % n] = true; }
                for (int i = 0; i < n; i++)
                {
                    if (!redraw[i]) continue;
                    if (lit[i]) g.ResetClip(); else g.SetClip(area, CombineMode.Replace);
                    DrawSegment(g, cfg, pal, L, sf, i, lit[i] ? segT[i] : 0, false);
                }
                g.ResetClip();
                if (centerT >= 0) DrawCenter(g, cfg, pal, L, sf, centerT, false);
            }
        }

        // Every pixel a button can touch in its normal look: the center circle, or the button's wedge
        // (from the gap around the center out to the edge of the image) plus its icon and label boxes.
        Region HoverArea(Layout L, int hover)
        {
            float rs = (RCenter + RInner) / 2;
            using (GraphicsPath inner = new GraphicsPath())
            {
                inner.AddEllipse(Cx - rs, Cy - rs, 2 * rs, 2 * rs);
                if (hover == HitCenter) return new Region(inner);
                Region r;
                if (L.N == 1) r = new Region(new RectangleF(0, 0, Size, Size));
                else
                    using (GraphicsPath wedge = new GraphicsPath())
                    {
                        float big = Size; // a radius that reaches past every corner
                        wedge.AddPie(Cx - big, Cy - big, 2 * big, 2 * big, -90 + hover * L.Sweep - L.Sweep / 2, L.Sweep);
                        r = new Region(wedge);
                    }
                RectangleF ir, lr;
                SegmentBoxes(L, hover, 0, out ir, out lr);
                ir.Inflate(2, 2); lr.Inflate(2, 2);
                r.Union(ir);
                r.Union(lr);
                r.Exclude(inner);
                return r;
            }
        }

        // Icon and label boxes of a button; grow moves them outwards with the "grow" hover style.
        void SegmentBoxes(Layout L, int i, float grow, out RectangleF ir, out RectangleF lr)
        {
            double a = (-90 + i * L.Sweep) * Math.PI / 180;
            float px = Cx + (float)Math.Cos(a) * (L.RMid + grow / 2), py = Cy + (float)Math.Sin(a) * (L.RMid + grow / 2);
            ir = new RectangleF(px - L.IconSz / 2, py - L.Block / 2, L.IconSz, L.IconSz);
            lr = new RectangleF(px - L.LabelW / 2, ir.Bottom + 3 * Sc, L.LabelW, L.Lh);
        }

        // Shadow and the empty ring the buttons sit on.
        static void SetModes(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        void DrawBackground(Graphics g, Palette pal, bool shadow)
        {
            SetModes(g);

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
        }

        // Sizes shared by all buttons; they only depend on the number of buttons.
        struct Layout { public int N; public float Sweep, GapDeg, Ro, Ri, RMid, IconSz, LabelW, Lh, Block; }

        Layout GetLayout(Graphics g, int n)
        {
            Layout L = new Layout();
            L.N = n;
            L.Sweep = n > 0 ? 360f / n : 360f;
            L.Ro = ROuter - 4 * Sc;
            L.Ri = RInner + 4 * Sc;
            L.GapDeg = n > 1 ? (float)(5 * Sc / ((L.Ro + L.Ri) / 2) * 180 / Math.PI) : 0;
            L.RMid = (ROuter + RInner) / 2;
            L.IconSz = (n > 8 ? 24 : 30) * Sc;
            L.LabelW = Math.Min(86 * Sc, n > 1 ? (float)(2 * L.RMid * Math.Sin(Math.PI / n)) - 6 * Sc : 86 * Sc);
            L.Lh = labelFont.GetHeight(g) + 1;   // label box follows the font, so nothing gets clipped
            L.Block = L.IconSz + 3 * Sc + L.Lh;  // icon + label, centered on the segment
            return L;
        }

        static StringFormat LabelFormat()
        {
            StringFormat sf = new StringFormat(StringFormatFlags.NoWrap);
            sf.Alignment = StringAlignment.Center;
            sf.Trimming = StringTrimming.EllipsisCharacter;
            return sf;
        }

        void DrawSegment(Graphics g, Config cfg, Palette pal, Layout L, StringFormat sf, int i, float hot, bool selected)
        {
            RingItem it = cfg.Items[i];
            Look l = GetLook(cfg, it, pal, hot);
            float start = -90 + i * L.Sweep - L.Sweep / 2 + L.GapDeg / 2;
            using (GraphicsPath p = L.N == 1 ? Art.Donut(Cx, Cy, L.Ro + l.Grow, L.Ri) : Art.Sector(Cx, Cy, L.Ro + l.Grow, L.Ri, start, L.Sweep - L.GapDeg))
            {
                using (SolidBrush b = new SolidBrush(l.Fill)) g.FillPath(b, p);
                Outline(g, p, pal, l.Outline, selected);
            }
            RectangleF ir, lr;
            SegmentBoxes(L, i, l.Grow, out ir, out lr);
            Art.DrawItem(g, it, ir, l.Fg);
            DrawLabel(g, it.Label, l.Fg, l.Fill, lr);
        }

        void DrawCenter(Graphics g, Config cfg, Palette pal, Layout L, StringFormat sf, float hot, bool selected)
        {
            Look c = GetLook(cfg, cfg.Center, pal, hot);
            float rc = RCenter + c.Grow / 2;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(Cx - rc, Cy - rc, 2 * rc, 2 * rc);
                using (SolidBrush b = new SolidBrush(c.Fill)) g.FillPath(b, p);
                using (Pen rim = new Pen(pal.Dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(35, 0, 0, 0), Sc))
                    g.DrawPath(rim, p);
                Outline(g, p, pal, c.Outline, selected);
            }
            float gs = 34 * Sc;
            RectangleF gr = new RectangleF(Cx - gs / 2, Cy - (gs + 3 * Sc + L.Lh) / 2, gs, gs);
            Art.DrawItem(g, cfg.Center, gr, c.Fg);
            RectangleF clr = new RectangleF(Cx - RCenter + 6 * Sc, gr.Bottom + 3 * Sc, 2 * RCenter - 12 * Sc, L.Lh);
            DrawLabel(g, cfg.Center.Label, c.Fg, c.Fill, clr);
        }

        // Labels are drawn by Windows' own text engine (GDI, like the settings window), which is much
        // crisper at this size than GDI+ even with ClearType. GDI can't draw on see-through pixels, so the
        // label goes onto a small tile in the button's color first, and only the text pixels are copied
        // over (the tile's plain color is left out). That works because a label always sits on its button.
        void DrawLabel(Graphics g, string text, Color fg, Color fill, RectangleF r)
        {
            if (string.IsNullOrEmpty(text)) return;
            int w = Math.Max(1, (int)Math.Round(r.Width)), h = Math.Max(1, (int)Math.Ceiling(r.Height));
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
                | TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis;
            using (Bitmap tile = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics tg = Graphics.FromImage(tile))
                {
                    tg.Clear(fill);
                    TextRenderer.DrawText(tg, text, labelFont, new Rectangle(0, 0, w, h), fg, fill, flags);
                }
                // GDI leaves the alpha of the pixels it drew at 0: make the tile opaque again.
                BitmapData d = tile.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                int[] px = new int[w * h];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                for (int i = 0; i < px.Length; i++) px[i] |= unchecked((int)0xFF000000);
                Marshal.Copy(px, 0, d.Scan0, px.Length);
                tile.UnlockBits(d);

                GraphicsState st = g.Save();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetColorKey(fill, fill); // the tile's background: leave the ring as it is there
                    // Whole pixels, so the text isn't resampled.
                    int x = (int)Math.Round(r.X + (r.Width - w) / 2), y = (int)Math.Round(r.Y);
                    g.DrawImage(tile, new Rectangle(x, y, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                }
                g.Restore(st);
            }
        }

        // hoverOutline: how strongly the accent outline shows, 0..1.
        void Outline(Graphics g, GraphicsPath p, Palette pal, float hoverOutline, bool selected)
        {
            if (hoverOutline > 0)
                using (Pen pen = new Pen(Color.FromArgb((int)Math.Round(255 * Math.Min(1, hoverOutline)), pal.Accent), 3 * Sc)) g.DrawPath(pen, p);
            if (selected)
                using (Pen pen = new Pen(pal.Text, 2 * Sc) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, p);
        }

        public void Dispose() { labelFont.Dispose(); panelFont.Dispose(); smallFont.Dispose(); }
    }
}

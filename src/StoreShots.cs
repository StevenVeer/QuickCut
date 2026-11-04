using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace QuickCut
{
    // Build helper: QuickCut.exe --store-shots <folder> draws the Microsoft Store screenshots
    // (1920x1080) from your own buttons, on a plain background with a caption. No desktop, no
    // wallpaper, and no real song or cover art in the media panel.
    static class StoreShots
    {
        const int W = 1920, H = 1080;

        public static void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            bool firstRun;
            Config cfg = Config.Load(out firstRun);
            cfg.VolumeBar = true;
            cfg.MediaBar = true;
            Media.Demo = new MediaInfo { Title = "Your favorite song", Artist = "Any music app", Playing = true };

            Ring(cfg, "dark", 0, Path.Combine(dir, "1-ring-dark.png"),
                "Your shortcuts,\nright at your mouse",
                "Press Ctrl+Space or double right-click anywhere.\nMove toward a button and let go.");
            Ring(cfg, "light", -2, Path.Combine(dir, "2-ring-light.png"),
                "Music and volume\nin one place",
                "Play, pause and skip in Spotify, YouTube and more.\nSee what's playing and change the volume.");
            Settings(cfg, "dark", 0, Path.Combine(dir, "3-settings.png"),
                "Make it yours",
                "Up to 12 buttons: apps, websites,\nmedia controls or key combinations.");
            Settings(cfg, "light", 1, Path.Combine(dir, "4-settings-look.png"),
                "Light, dark and\nyour own colors",
                "Tiny and fast: no accounts,\nno ads, no tracking.");

            // Store logos: 9:16 poster art and 1:1 box art.
            Logo(Path.Combine(dir, "poster-1440x2160.png"), 1440, 2160);
            Logo(Path.Combine(dir, "boxart-2160x2160.png"), 2160, 2160);
            Hero(cfg, Path.Combine(dir, "hero-3840x2160.png"));
        }

        // 16:9 Super hero art: the banner at the top of the Store page. No app name allowed,
        // and the Store puts its own title and buttons over the left side, so the ring goes right.
        static void Hero(Config baseCfg, string path)
        {
            const int w = 3840, h = 2160;
            Config cfg = baseCfg.Clone();
            cfg.ThemeMode = "dark";
            using (RingRenderer r = new RingRenderer(4.0f, 1f, true, true))
            using (Bitmap ring = new Bitmap(r.Size, r.Height, PixelFormat.Format32bppArgb))
            using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(ring))
                {
                    Palette pal = Palette.Get(cfg);
                    r.Draw(g, cfg, pal, 0, RingRenderer.HitNone, true);
                    r.DrawVolume(g, cfg, pal, RingRenderer.VolNone, true, 0.4f, false);
                    r.DrawMedia(g, cfg, pal, RingRenderer.MedNone, Media.Demo, true);
                }
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                        Color.FromArgb(16, 24, 44), Color.FromArgb(38, 72, 140), 25f))
                        g.FillRectangle(bg, 0, 0, w, h);
                    int px = w - 520 - ring.Width, py = (h - ring.Height) / 2;
                    using (GraphicsPath glow = new GraphicsPath())
                    {
                        glow.AddEllipse(px - 600, py - 400, ring.Width + 1200, ring.Height + 800);
                        using (PathGradientBrush pb = new PathGradientBrush(glow))
                        {
                            pb.CenterColor = Color.FromArgb(80, Art.Brand);
                            pb.SurroundColors = new[] { Color.FromArgb(0, Art.Brand) };
                            g.FillPath(pb, glow);
                        }
                    }
                    g.DrawImage(ring, px, py, ring.Width, ring.Height);
                }
                b.Save(path, ImageFormat.Png);
            }
        }

        // The app icon and name on the brand background.
        static void Logo(string path, int w, int h)
        {
            using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                    Color.FromArgb(20, 30, 52), Color.FromArgb(38, 72, 140), 70f))
                    g.FillRectangle(bg, 0, 0, w, h);

                int s = (int)(Math.Min(w, h) * 0.5f);
                float cy = h * (h > w ? 0.42f : 0.44f);
                int ix = (w - s) / 2, iy = (int)(cy - s / 2f);
                using (GraphicsPath glow = new GraphicsPath())
                {
                    glow.AddEllipse(ix - s * 0.45f, iy - s * 0.45f, s * 1.9f, s * 1.9f);
                    using (PathGradientBrush pb = new PathGradientBrush(glow))
                    {
                        pb.CenterColor = Color.FromArgb(90, Art.Brand);
                        pb.SurroundColors = new[] { Color.FromArgb(0, Art.Brand) };
                        g.FillPath(pb, glow);
                    }
                }
                using (Bitmap icon = Art.RenderAppIcon(s)) g.DrawImage(icon, ix, iy, s, s);

                using (Font f = new Font("Segoe UI Semibold", Math.Min(w, h) * 0.11f, GraphicsUnit.Pixel))
                using (SolidBrush tb = new SolidBrush(Color.White))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString("QuickCut", f, tb, new RectangleF(0, iy + s + Math.Min(w, h) * 0.06f, w, f.Height * 1.4f), sf);
                b.Save(path, ImageFormat.Png);
            }
        }

        static void Ring(Config baseCfg, string theme, int hover, string path, string title, string sub)
        {
            Config cfg = baseCfg.Clone();
            cfg.ThemeMode = theme;
            float scale = 2.1f;
            using (RingRenderer r = new RingRenderer(scale, 1f, true, true))
            using (Bitmap ring = new Bitmap(r.Size, r.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(ring))
                {
                    Palette pal = Palette.Get(cfg);
                    r.Draw(g, cfg, pal, hover, RingRenderer.HitNone, true);
                    r.DrawVolume(g, cfg, pal, RingRenderer.VolNone, true, 0.4f, false);
                    r.DrawMedia(g, cfg, pal, RingRenderer.MedNone, Media.Demo, true);
                }
                Compose(path, theme == "dark", ring, title, sub);
            }
        }

        static void Settings(Config baseCfg, string theme, int tab, string path, string title, string sub)
        {
            Config cfg = baseCfg.Clone();
            cfg.ThemeMode = theme;
            using (Bitmap win = SettingsForm.Render(cfg, tab, true))
            {
                // Scale up to a comfortable height, with rounded corners and a thin rim like Windows 11.
                float s = Math.Min(1.5f, 820f / win.Height);
                using (Bitmap big = new Bitmap((int)(win.Width * s), (int)(win.Height * s), PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(big))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (GraphicsPath p = ThemedControl.Round(new RectangleF(0, 0, big.Width - 1, big.Height - 1), 14))
                        {
                            using (TextureBrush tb = new TextureBrush(win, WrapMode.Clamp))
                            {
                                tb.ScaleTransform(s, s);
                                g.FillPath(tb, p);
                            }
                            using (Pen rim = new Pen(theme == "dark" ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0)))
                                g.DrawPath(rim, p);
                        }
                    }
                    Compose(path, theme == "dark", big, title, sub, true);
                }
            }
        }

        // Background, caption on the left, picture on the right.
        static void Compose(string path, bool dark, Bitmap pic, string title, string sub, bool windowShadow = false)
        {
            using (Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Color top = dark ? Color.FromArgb(20, 30, 52) : Color.FromArgb(222, 233, 252);
                Color bottom = dark ? Color.FromArgb(38, 72, 140) : Color.FromArgb(160, 196, 250);
                using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), top, bottom, 60f))
                    g.FillRectangle(bg, 0, 0, W, H);
                // A soft glow behind the picture
                int px = W - 160 - pic.Width, py = (H - pic.Height) / 2;
                using (GraphicsPath glow = new GraphicsPath())
                {
                    glow.AddEllipse(px - 220, py - 160, pic.Width + 440, pic.Height + 320);
                    using (PathGradientBrush pb = new PathGradientBrush(glow))
                    {
                        pb.CenterColor = Color.FromArgb(dark ? 70 : 110, Art.Brand);
                        pb.SurroundColors = new[] { Color.FromArgb(0, Art.Brand) };
                        g.FillPath(pb, glow);
                    }
                }
                if (windowShadow)
                    for (int i = 1; i <= 18; i++)
                        using (GraphicsPath sp = ThemedControl.Round(new RectangleF(px - i, py - i + 12, pic.Width + 2 * i, pic.Height + 2 * i), 14 + i))
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb(5, 0, 0, 0)))
                            g.FillPath(sh, sp);
                g.DrawImage(pic, px, py, pic.Width, pic.Height);

                Color fg = dark ? Color.White : Color.FromArgb(18, 28, 48);
                Color fg2 = dark ? Color.FromArgb(200, 214, 240) : Color.FromArgb(60, 74, 100);
                float left = 140, textW = px - left - 80;
                using (Font tf = new Font("Segoe UI Semibold", 76, GraphicsUnit.Pixel))
                using (Font sf = new Font("Segoe UI", 34, GraphicsUnit.Pixel))
                using (SolidBrush tb = new SolidBrush(fg))
                using (SolidBrush sb = new SolidBrush(fg2))
                {
                    SizeF ts = g.MeasureString(title, tf, (int)textW);
                    SizeF ss = g.MeasureString(sub, sf, (int)textW);
                    float y = (H - ts.Height - 36 - ss.Height) / 2;
                    g.DrawString(title, tf, tb, new RectangleF(left, y, textW, ts.Height + 4));
                    using (SolidBrush ab = new SolidBrush(Art.Brand))
                        g.FillRectangle(ab, left + 4, y + ts.Height + 8, 90, 6);
                    g.DrawString(sub, sf, sb, new RectangleF(left, y + ts.Height + 36, textW, ss.Height + 4));
                }
                b.Save(path, ImageFormat.Png);
            }
        }
    }
}

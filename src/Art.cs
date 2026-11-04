using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QuickCut
{
    // All drawing: ring geometry, vector glyphs, program icons and the app icon.
    static class Art
    {
        public static readonly Color Brand = Color.FromArgb(79, 142, 247);
        static readonly Color BrandLight = Color.FromArgb(150, 190, 255);

        public static Color Accent()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v is int)
                {
                    int c = (int)v; // ABGR
                    Color col = Color.FromArgb(255, c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                    if (col.R + col.G + col.B > 180) return col;
                }
            }
            catch { }
            return Brand;
        }

        public static GraphicsPath Donut(float cx, float cy, float ro, float ri)
        {
            GraphicsPath p = new GraphicsPath(FillMode.Alternate);
            p.AddEllipse(cx - ro, cy - ro, 2 * ro, 2 * ro);
            p.AddEllipse(cx - ri, cy - ri, 2 * ri, 2 * ri);
            return p;
        }

        public static GraphicsPath Sector(float cx, float cy, float ro, float ri, float start, float sweep)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(cx - ro, cy - ro, 2 * ro, 2 * ro, start, sweep);
            p.AddArc(cx - ri, cy - ri, 2 * ri, 2 * ri, start + sweep, -sweep);
            p.CloseFigure();
            return p;
        }

        // ---------- item visuals ----------

        public static void DrawItem(Graphics g, RingItem it, RectangleF r, Color col)
        {
            if (it.Type == ActionType.Media) { DrawMedia(g, it.Target, r, col); return; }
            if (it.Type == ActionType.Keys) { DrawKeyboard(g, r, col); return; }
            Bitmap bmp = GetIcon(it, (int)r.Width);
            if (bmp != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, r);
                return;
            }
            if (it.Type == ActionType.Url && Actions.IsWebUrl(it.Target)) DrawGlobe(g, r, col);
            else DrawLetter(g, it.Label, r, col);
        }

        public static void DrawMedia(Graphics g, string id, RectangleF r, Color col)
        {
            float x = r.X, y = r.Y, s = Math.Min(r.Width, r.Height);
            bool mirror = id == "prev";
            Func<float, float, PointF> P = delegate(float px, float py)
            {
                return new PointF(x + (mirror ? 1 - px : px) * s, y + py * s);
            };
            Func<float, float, float, float, PointF[]> Rect = delegate(float l, float t, float rr, float b)
            {
                return new[] { P(l, t), P(rr, t), P(rr, b), P(l, b) };
            };
            using (SolidBrush b = new SolidBrush(col))
            using (Pen p = new Pen(col, Math.Max(1.5f, s * 0.085f)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                switch (id)
                {
                    case "playpause":
                        g.FillPolygon(b, new[] { P(.06f, .2f), P(.06f, .8f), P(.5f, .5f) });
                        g.FillPolygon(b, Rect(.6f, .22f, .72f, .78f));
                        g.FillPolygon(b, Rect(.8f, .22f, .92f, .78f));
                        break;
                    case "next":
                    case "prev":
                        g.FillPolygon(b, new[] { P(.06f, .22f), P(.06f, .78f), P(.42f, .5f) });
                        g.FillPolygon(b, new[] { P(.4f, .22f), P(.4f, .78f), P(.76f, .5f) });
                        g.FillPolygon(b, Rect(.78f, .22f, .9f, .78f));
                        break;
                    case "stop":
                        g.FillPolygon(b, Rect(.22f, .22f, .78f, .78f));
                        break;
                    case "volup":
                    case "voldown":
                    case "mute":
                        g.FillPolygon(b, new[] { P(.06f, .37f), P(.24f, .37f), P(.46f, .17f), P(.46f, .83f), P(.24f, .63f), P(.06f, .63f) });
                        if (id == "mute")
                        {
                            g.DrawLine(p, P(.62f, .37f), P(.88f, .63f));
                            g.DrawLine(p, P(.88f, .37f), P(.62f, .63f));
                        }
                        else
                        {
                            g.DrawLine(p, P(.6f, .5f), P(.92f, .5f));
                            if (id == "volup") g.DrawLine(p, P(.76f, .34f), P(.76f, .66f));
                        }
                        break;
                    default:
                        g.DrawEllipse(p, x + .15f * s, y + .15f * s, .7f * s, .7f * s);
                        break;
                }
            }
        }

        public static void DrawGlobe(Graphics g, RectangleF r, Color col)
        {
            float x = r.X, y = r.Y, s = Math.Min(r.Width, r.Height);
            using (Pen p = new Pen(col, Math.Max(1.2f, s * 0.06f)))
            {
                g.DrawEllipse(p, x + .1f * s, y + .1f * s, .8f * s, .8f * s);
                g.DrawEllipse(p, x + .32f * s, y + .1f * s, .36f * s, .8f * s);
                g.DrawLine(p, x + .1f * s, y + .5f * s, x + .9f * s, y + .5f * s);
            }
        }

        public static void DrawKeyboard(Graphics g, RectangleF r, Color col)
        {
            float x = r.X, y = r.Y, s = Math.Min(r.Width, r.Height);
            using (Pen p = new Pen(col, Math.Max(1.2f, s * 0.06f)))
            using (SolidBrush b = new SolidBrush(col))
            {
                g.DrawRectangle(p, x + .05f * s, y + .25f * s, .9f * s, .5f * s);
                for (int i = 0; i < 4; i++) g.FillRectangle(b, x + (.17f + i * .18f) * s, y + .35f * s, .1f * s, .1f * s);
                g.DrawLine(p, x + .3f * s, y + .6f * s, x + .7f * s, y + .6f * s);
            }
        }

        static void DrawLetter(Graphics g, string label, RectangleF r, Color col)
        {
            string ch = string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1).ToUpperInvariant();
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(60, col)))
                g.FillEllipse(bg, r);
            using (Font f = new Font("Segoe UI Semibold", r.Height * 0.5f, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(col))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(ch, f, b, r, sf);
        }

        // ---------- program / file icons (via the Windows shell, cached) ----------

        static readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static void ClearIconCache()
        {
            foreach (Bitmap b in iconCache.Values) if (b != null) b.Dispose();
            iconCache.Clear();
        }

        static Bitmap GetIcon(RingItem it, int px)
        {
            bool large = px > 34;
            string key = it.Type + ":" + it.Target + ":" + large;
            Bitmap bmp;
            if (iconCache.TryGetValue(key, out bmp)) return bmp;
            bmp = null;
            try
            {
                string path = it.Type == ActionType.Program ? Actions.ExpandPath(it.Target) : ProtocolHandler(it.Target);
                if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path) || AppCatalog.IsAppsFolder(path)))
                    bmp = ShellIcon(path, large);
            }
            catch { }
            iconCache[key] = bmp;
            return bmp;
        }

        // Icon for any file or folder (used by the app picker and the settings window).
        public static Bitmap PathIcon(string path, bool large)
        {
            string key = "path:" + path + ":" + large;
            Bitmap bmp;
            if (iconCache.TryGetValue(key, out bmp)) return bmp;
            bmp = LoadPathIcon(path, large);
            iconCache[key] = bmp;
            return bmp;
        }

        // Uncached load; safe on a background STA thread (the app picker preloads its icons with it).
        public static Bitmap LoadPathIcon(string path, bool large)
        {
            try
            {
                string p = Actions.ExpandPath(path);
                if (p.Length > 0 && (File.Exists(p) || Directory.Exists(p) || AppCatalog.IsAppsFolder(p))) return ShellIcon(p, large);
            }
            catch { }
            return null;
        }

        public static void DrawApps(Graphics g, RectangleF r, Color col)
        {
            float x = r.X, y = r.Y, s = Math.Min(r.Width, r.Height), q = s * 0.36f, gap = s * 0.12f;
            using (Pen p = new Pen(col, Math.Max(1.2f, s * 0.06f)))
                for (int i = 0; i < 4; i++)
                    g.DrawRectangle(p, x + (i % 2) * (q + gap) + s * 0.08f, y + (i / 2) * (q + gap) + s * 0.08f, q, q);
        }

        static Bitmap ShellIcon(string path, bool large)
        {
            const uint SHGFI_SYSICONINDEX = 0x4000, SHGFI_PIDL = 0x8;
            Native.SHFILEINFO info = new Native.SHFILEINFO();
            uint size = (uint)Marshal.SizeOf(info);
            if (AppCatalog.IsAppsFolder(path))
            {
                // Store apps have no file path: resolve the shell item and ask for its icon.
                IntPtr pidl; uint attrs;
                if (Native.SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out attrs) != 0) return null;
                try { if (Native.SHGetFileInfoPidl(pidl, 0, ref info, size, SHGFI_SYSICONINDEX | SHGFI_PIDL) == IntPtr.Zero) return null; }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            else if (Native.SHGetFileInfo(path, 0, ref info, size, SHGFI_SYSICONINDEX) == IntPtr.Zero) return null;
            Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
            IntPtr list;
            if (Native.SHGetImageList(large ? 2 : 0, ref iid, out list) != 0) return null; // 2 = 48px, 0 = 32px
            IntPtr hIcon = Native.ImageList_GetIcon(list, info.iIcon, 1);
            if (hIcon == IntPtr.Zero) return null;
            try { using (Icon ic = Icon.FromHandle(hIcon)) return ic.ToBitmap(); }
            finally { Native.DestroyIcon(hIcon); }
        }

        // For links like "spotify:" find the program that handles them, so we can show its icon.
        static string ProtocolHandler(string url)
        {
            url = (url ?? "").Trim();
            int c = url.IndexOf(':');
            if (c <= 1 || Actions.IsWebUrl(url)) return null;
            string scheme = url.Substring(0, c);
            using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(scheme + @"\shell\open\command"))
                return AppCatalog.ExeFromCommand(k == null ? null : k.GetValue("") as string);
        }

        // ---------- app icon ----------

        public static Bitmap RenderAppIcon(int s)
        {
            Bitmap b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float c = s / 2f, ro = s * 0.48f, ri = s * 0.3f, rc = s * 0.2f;
                using (GraphicsPath d = Donut(c, c, ro, ri))
                using (SolidBrush br = new SolidBrush(Brand)) g.FillPath(br, d);
                using (GraphicsPath top = Sector(c, c, ro, ri, -135, 90))
                using (SolidBrush br = new SolidBrush(BrandLight)) g.FillPath(br, top);

                // Cut transparent gaps between the four segments.
                g.CompositingMode = CompositingMode.SourceCopy;
                using (Pen gap = new Pen(Color.Transparent, Math.Max(1f, s * 0.06f)))
                    for (int k = 0; k < 4; k++)
                    {
                        double a = (-135 + 90 * k) * Math.PI / 180;
                        float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                        g.DrawLine(gap, c + cos * (ri - 2), c + sin * (ri - 2), c + cos * (ro + 2), c + sin * (ro + 2));
                    }
                g.CompositingMode = CompositingMode.SourceOver;

                using (SolidBrush br = new SolidBrush(Brand)) g.FillEllipse(br, c - rc, c - rc, 2 * rc, 2 * rc);
                if (s >= 32)
                    using (SolidBrush w = new SolidBrush(Color.White))
                        g.FillPolygon(w, new[] {
                            new PointF(c - rc * 0.35f, c - rc * 0.5f),
                            new PointF(c - rc * 0.35f, c + rc * 0.5f),
                            new PointF(c + rc * 0.55f, c) });
            }
            return b;
        }

        public static Icon CreateIcon(int size)
        {
            using (Bitmap b = RenderAppIcon(size)) return Icon.FromHandle(b.GetHicon());
        }

        // Writes the logo PNGs for the Store package (see build-store.cmd). File names use MSIX
        // resource qualifiers, so Windows picks the sharpest one for each size and screen scale.
        public static void WriteStoreLogos(string dir)
        {
            Directory.CreateDirectory(dir);
            int[] scales = { 100, 125, 150, 200, 400 };
            foreach (int sc in scales)
            {
                // name, base width, base height, share of the height the icon fills
                SaveLogo(dir, "Square44x44Logo", 44, 44, 0.92f, sc);
                SaveLogo(dir, "Square150x150Logo", 150, 150, 0.6f, sc);
                SaveLogo(dir, "Wide310x150Logo", 310, 150, 0.6f, sc);
                SaveLogo(dir, "StoreLogo", 50, 50, 0.92f, sc);
            }
            // Taskbar, Start list and Explorer: exact pixel sizes, drawn without a tile behind them.
            foreach (int t in new[] { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256 })
            {
                using (Bitmap b = RenderAppIcon(t))
                {
                    b.Save(Path.Combine(dir, "Square44x44Logo.targetsize-" + t + ".png"), ImageFormat.Png);
                    b.Save(Path.Combine(dir, "Square44x44Logo.targetsize-" + t + "_altform-unplated.png"), ImageFormat.Png);
                    b.Save(Path.Combine(dir, "Square44x44Logo.targetsize-" + t + "_altform-lightunplated.png"), ImageFormat.Png);
                }
            }
        }

        static void SaveLogo(string dir, string name, int w, int h, float fill, int scale)
        {
            int pw = w * scale / 100, ph = h * scale / 100, s = (int)Math.Round(ph * fill);
            using (Bitmap b = new Bitmap(pw, ph, PixelFormat.Format32bppArgb))
            using (Bitmap icon = RenderAppIcon(s))
            {
                using (Graphics g = Graphics.FromImage(b)) g.DrawImage(icon, (pw - s) / 2, (ph - s) / 2, s, s);
                b.Save(Path.Combine(dir, name + ".scale-" + scale + ".png"), ImageFormat.Png);
            }
        }

        // Writes a multi-size .ico (PNG frames) for the exe.
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            List<byte[]> frames = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap b = RenderAppIcon(s))
                using (MemoryStream ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
        }
    }
}

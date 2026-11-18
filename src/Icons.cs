using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace QuickCut
{
    // Line icons, drawn as strokes on a 24 x 24 grid (written like SVG path data), so they stay sharp at
    // any size. Used by the settings window and as button icons (Shortcut and Website buttons).
    class LineIcon
    {
        readonly GraphicsPath path = new GraphicsPath();
        readonly List<PointF> dots = new List<PointF>(); // zero-length strokes ("h.01"): drawn as round dots

        static readonly Regex Token = new Regex(@"[MmLlHhVvCcAaZz]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?");

        // Path data with M L H V C A Z (and their relative forms). Arcs must be circular (rx = ry).
        public LineIcon(string d)
        {
            List<string> t = new List<string>();
            foreach (Match m in Token.Matches(d)) t.Add(m.Value);
            int i = 0;
            // A command without all its numbers ends the path (rather than throwing while painting).
            Func<float> num = delegate
            {
                if (i >= t.Count || char.IsLetter(t[i][0])) throw new FormatException("icon path: missing number");
                return float.Parse(t[i++], CultureInfo.InvariantCulture);
            };
            try { Parse(t, ref i, num); }
            catch (FormatException) { }
        }

        void Parse(List<string> t, ref int i, Func<float> num)
        {
            char cmd = 'M';
            PointF cur = PointF.Empty, start = PointF.Empty;
            while (i < t.Count)
            {
                if (char.IsLetter(t[i][0])) cmd = t[i++][0];
                bool rel = char.IsLower(cmd);
                PointF o = rel ? cur : PointF.Empty;
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        cur = new PointF(o.X + num(), o.Y + num());
                        start = cur;
                        path.StartFigure();
                        cmd = rel ? 'l' : 'L'; // more pairs after a move are lines
                        break;
                    case 'L': Line(ref cur, new PointF(o.X + num(), o.Y + num())); break;
                    case 'H': Line(ref cur, new PointF((rel ? cur.X : 0) + num(), cur.Y)); break;
                    case 'V': Line(ref cur, new PointF(cur.X, (rel ? cur.Y : 0) + num())); break;
                    case 'C':
                    {
                        PointF c1 = new PointF(o.X + num(), o.Y + num()), c2 = new PointF(o.X + num(), o.Y + num());
                        PointF e = new PointF(o.X + num(), o.Y + num());
                        path.AddBezier(cur, c1, c2, e);
                        cur = e;
                        break;
                    }
                    case 'A':
                    {
                        float rx = num(), ry = num();
                        num(); // x-axis rotation: unused (circles only)
                        bool large = num() != 0, sweep = num() != 0;
                        PointF e = new PointF(o.X + num(), o.Y + num());
                        Arc(cur, e, (rx + ry) / 2, large, sweep);
                        cur = e;
                        break;
                    }
                    case 'Z':
                        path.CloseFigure();
                        cur = start;
                        break;
                    default:
                        i++; // unknown: skip
                        break;
                }
            }
        }

        void Line(ref PointF cur, PointF to)
        {
            float dx = to.X - cur.X, dy = to.Y - cur.Y;
            if (dx * dx + dy * dy < 0.01f) dots.Add(cur);
            else path.AddLine(cur, to);
            cur = to;
        }

        // An SVG arc on a circle, from its end points (SVG spec F.6.5 with rx = ry and no rotation).
        void Arc(PointF a, PointF b, float r, bool large, bool sweep)
        {
            float dx = (a.X - b.X) / 2, dy = (a.Y - b.Y) / 2;
            float d2 = dx * dx + dy * dy;
            if (d2 == 0) return;
            if (r * r < d2) r = (float)Math.Sqrt(d2); // too small to reach: SVG scales it up
            float f = (float)Math.Sqrt(Math.Max(0, (r * r - d2) / d2)) * (large == sweep ? -1 : 1);
            float cx = f * dy + (a.X + b.X) / 2, cy = -f * dx + (a.Y + b.Y) / 2;
            double a1 = Math.Atan2(a.Y - cy, a.X - cx), a2 = Math.Atan2(b.Y - cy, b.X - cx);
            double sw = a2 - a1;
            if (sweep && sw < 0) sw += 2 * Math.PI;
            if (!sweep && sw > 0) sw -= 2 * Math.PI;
            path.AddArc(cx - r, cy - r, 2 * r, 2 * r, (float)(a1 * 180 / Math.PI), (float)(sw * 180 / Math.PI));
        }

        // stroke: line width on the 24 grid (1.8 by default, like the design).
        public void Draw(Graphics g, RectangleF r, Color c, float stroke, bool fill)
        {
            float s = Math.Min(r.Width, r.Height) / 24f;
            float ox = r.X + (r.Width - 24 * s) / 2, oy = r.Y + (r.Height - 24 * s) / 2;
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (GraphicsPath p = (GraphicsPath)path.Clone())
            using (Matrix m = new Matrix(s, 0, 0, s, ox, oy))
            {
                p.Transform(m);
                if (fill) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
                else
                    using (Pen pen = new Pen(c, stroke * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawPath(pen, p);
                using (SolidBrush b = new SolidBrush(c))
                    foreach (PointF d in dots)
                    {
                        float w = stroke * s * 1.15f;
                        g.FillEllipse(b, ox + d.X * s - w / 2, oy + d.Y * s - w / 2, w, w);
                    }
            }
            g.Restore(st);
        }

        public void Draw(Graphics g, RectangleF r, Color c) { Draw(g, r, c, 1.8f, false); }
    }

    static class Icons
    {
        static readonly Dictionary<string, string> data = new Dictionary<string, string>
        {
            // settings window
            { "nav-buttons", "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18zM12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z" },
            { "nav-look", "M12 3a9 9 0 0 0 0 18c1.1 0 1.6-.9 1.2-1.8-.5-1.1.2-2.2 1.4-2.2H17a4 4 0 0 0 4-4c0-5-4-10-9-10zM7.5 11.5h.01M10 7.5h.01M15 7.5h.01" },
            { "nav-volume", "M4 9v6h4l5 4V5L8 9H4zM16.5 8.5a5 5 0 0 1 0 7M19 6a8.5 8.5 0 0 1 0 12" },
            { "nav-general", "M4 7h10M18 7h2M4 17h4M12 17h8M14 5v4M8 15v4" },
            { "check", "M5 12l5 5L20 7" },
            { "plus", "M12 5v14M5 12h14" },
            { "x", "M6 6l12 12M18 6L6 18" },
            { "ccw", "M3 12a9 9 0 1 0 3-6.7M3 4v5h5" },
            { "cw", "M21 12a9 9 0 1 1-3-6.7M21 4v5h-5" },
            { "play", "M6 3.5l15 8.5-15 8.5z" },
            { "apps", "M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z" },
            { "globe", "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18zM3 12h18M12 3c3 3.5 3 14.5 0 18M12 3c-3 3.5-3 14.5 0 18" },
            { "media", "M4 6l8 6-8 6zM15 6v12M19 6v12" },
            { "mouse", "M12 3a6 6 0 0 0-6 6v6a6 6 0 0 0 12 0V9a6 6 0 0 0-6-6zM12 7v3" },
            { "power", "M12 3v8M6.4 6.4a8 8 0 1 0 11.2 0" },
            { "bell", "M6 16V11a6 6 0 0 1 12 0v5l2 2H4zM10 21h4" },
            { "download", "M12 4v11M7 10l5 5 5-5M5 20h14" },
            { "upload", "M12 20V9M7 14l5-5 5 5M5 4h14" },
            { "bug", "M8 9a4 4 0 0 1 8 0v6a4 4 0 0 1-8 0zM4 13h4M16 13h4M5 7l3 2M19 7l-3 2M5 19l3-2M19 19l-3-2" },
            { "bulb", "M9 18h6M10 21h4M12 3a6 6 0 0 0-3.5 10.9c.6.5 1 1.2 1 2.1h5c0-.9.4-1.6 1-2.1A6 6 0 0 0 12 3z" },
            { "eye", "M2 12C4.5 7.3 8 5 12 5C16 5 19.5 7.3 22 12C19.5 16.7 16 19 12 19C8 19 4.5 16.7 2 12zM12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z" },
            { "music", "M9 18V5l11-2v13M9 18a3 3 0 1 1-6 0a3 3 0 1 1 6 0zM20 16a3 3 0 1 1-6 0a3 3 0 1 1 6 0z" },
            { "trash", "M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" },
            // button icons (also offered in the icon picker)
            { "activity", "M3 12h4l3-8 4 16 3-8h4" },
            { "keyboard", "M3 7h18v10H3zM7 11h.01M11 11h.01M15 11h.01M7 14h10" },
            { "camera", "M4 8h3l2-3h6l2 3h3v11H4zM12 10a3 3 0 1 0 0 6a3 3 0 1 0 0-6z" },
            { "scissors", "M6 4a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5zM6 15a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5zM8 7.5L20 18M8 16.5L20 6" },
            { "lock", "M6 11h12v9H6zM8.5 11V8a3.5 3.5 0 0 1 7 0v3" },
            { "copy", "M8 8h11v12H8zM5 16V4h11" },
            { "paste", "M8 5H6v15h12V5h-2M9 3h6v4H9z" },
            { "undo", "M9 14L4 9l5-5M4 9h10a6 6 0 0 1 0 12h-3" },
            { "search", "M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14zM20 20l-4-4" },
            { "micoff", "M12 3a3 3 0 0 0-3 3v5a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3zM5 11a7 7 0 0 0 14 0M12 18v3M4 4l16 16" },
            { "desktop", "M3 5h18v12H3zM8 21h8M12 17v4" },
            { "windows", "M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z" },
            { "save", "M5 4h11l3 3v13H5zM8 4v5h7M8 20v-6h8v6" },
            { "refresh", "M20 12a8 8 0 1 1-2.3-5.7M20 4v5h-5" },
            { "emoji", "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18zM8.5 14a4 4 0 0 0 7 0M9 9.5h.01M15 9.5h.01" },
            { "terminal", "M4 5h16v14H4zM7 9l3 3-3 3M12 15h5" },
            { "star", "M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1-5.4-2.9-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z" },
        };

        // The icons a Shortcut or Website button can use, in picker order, after "letter".
        public static readonly string[] ButtonIcons = {
            "activity", "keyboard", "camera", "scissors", "lock", "copy", "paste", "undo", "search",
            "micoff", "desktop", "windows", "save", "refresh", "emoji", "terminal", "star" };

        static readonly Dictionary<string, LineIcon> cache = new Dictionary<string, LineIcon>();

        public static bool Has(string name) { return name != null && data.ContainsKey(name); }

        public static LineIcon Get(string name)
        {
            LineIcon ic;
            if (cache.TryGetValue(name, out ic)) return ic;
            string d;
            if (!data.TryGetValue(name, out d)) return null;
            ic = new LineIcon(d);
            cache[name] = ic;
            return ic;
        }

        public static void Draw(Graphics g, string name, RectangleF r, Color c)
        {
            LineIcon ic = Get(name);
            if (ic != null) ic.Draw(g, r, c);
        }
    }

    // Images people chose as button icons. Each is copied into QuickCut's settings folder (as a PNG, at
    // most 128 px), so the button keeps its icon when the original file moves or is deleted. A button
    // refers to one as "file:<name>".
    static class IconStore
    {
        public const string Prefix = "file:";
        public const int MaxSize = 128;
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static string Folder { get { return Path.Combine(Config.Folder, "icons"); } }

        // Copies the image in and returns the button's icon value ("file:img-....png"). Throws when the
        // file isn't an image Windows can read.
        public static string Import(string file)
        {
            byte[] bytes = File.ReadAllBytes(file);
            Bitmap src = null;
            try
            {
                if (file.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    using (Icon ic = new Icon(file, 256, 256)) src = ic.ToBitmap(); // the largest size in the file
                else
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (Image img = Image.FromStream(ms)) src = new Bitmap(img);
            }
            catch (Exception) { throw new InvalidOperationException(Str.ImageUnreadable); }
            using (src)
            using (Bitmap fit = Fit(src))
            {
                string name;
                using (SHA1 sha = SHA1.Create())
                    name = "img-" + BitConverter.ToString(sha.ComputeHash(bytes), 0, 6).Replace("-", "").ToLowerInvariant() + ".png";
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, name);
                if (!File.Exists(path)) fit.Save(path, ImageFormat.Png);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow); // newest last in the picker
                return Prefix + name;
            }
        }

        // Scaled down to fit MaxSize, keeping its shape.
        static Bitmap Fit(Bitmap src)
        {
            float k = Math.Min(1f, (float)MaxSize / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)Math.Round(src.Width * k)), h = Math.Max(1, (int)Math.Round(src.Height * k));
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, w, h);
            }
            return b;
        }

        // The image for "file:<name>", or null when it's missing.
        public static Bitmap Load(string value)
        {
            if (value == null || !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string name = Path.GetFileName(value.Substring(Prefix.Length)); // never outside the folder
            Bitmap b;
            if (cache.TryGetValue(name, out b)) return b;
            b = null;
            try
            {
                string path = Path.Combine(Folder, name);
                if (File.Exists(path))
                    using (FileStream fs = File.OpenRead(path))
                    using (Image img = Image.FromStream(fs)) b = new Bitmap(img);
            }
            catch { }
            cache[name] = b;
            return b;
        }

        // Every stored image as a button icon value, oldest first.
        public static List<string> All()
        {
            List<string> list = new List<string>();
            try
            {
                if (!Directory.Exists(Folder)) return list;
                List<FileInfo> files = new List<FileInfo>(new DirectoryInfo(Folder).GetFiles("*.png"));
                files.Sort(delegate(FileInfo a, FileInfo b) { return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc); });
                foreach (FileInfo f in files) list.Add(Prefix + f.Name);
            }
            catch { }
            return list;
        }
    }
}

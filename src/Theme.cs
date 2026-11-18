using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QuickCut
{
    // Resolved colors for the current theme (light/dark + accent), used by the ring and the settings window.
    class Palette
    {
        public bool Dark;
        public Color Window, Card, Field, Text, Muted, Border, Hover, Ring, Button, Accent, AccentText, Danger;
        // Settings window: the side rail, the preview stage and its dots, level bar tracks, the soft accent
        // behind selected items, and the green of "saved".
        public Color Rail, Stage, Dot, Track, AccentSoft, Success;

        public static Palette Get(Config cfg)
        {
            bool dark = cfg.ThemeMode == "dark" || (cfg.ThemeMode != "light" && Theme.WindowsIsDark());
            Palette p = dark ? DarkBase() : LightBase();
            p.Accent = AccentFor(cfg.Accent);
            // Links in the accent color: light colors like orange are too faint on white, so darken them.
            p.AccentText = !dark && Theme.Lum(p.Accent) > 0.45f ? Theme.Mix(p.Accent, Color.Black, 0.35f) : p.Accent;
            p.AccentSoft = Theme.Mix(p.Card, p.Accent, dark ? 0.16f : 0.18f);
            return p;
        }

        public static readonly Color DefaultAccent = Color.FromArgb(0xEF, 0x9F, 0x27); // orange

        // Preset colors offered in Settings; names are in Str.ButtonPresetNames / Str.HoverPresetNames.
        // Button colors are muted so labels stay readable; hover colors are bright so they stand out on the ring.
        public static readonly string[] ButtonPresets = {
            "#000000", "#2E2F37", "#5A5A63", "#44546A", "#1F3A5F", "#26215C", "#0F4C4A", "#2D4A2B",
            "#4A2545", "#5C1F2A", "#3D2B1F", "#7A2E12", "#D6DCE5", "#E8E1D3", "#FFFFFF" };
        public static readonly string[] HoverPresets = {
            "#378ADD", "#4FC3F7", "#22B8CF", "#1D9E75", "#3DDC97", "#4CAF50", "#8BC34A", "#F2C230",
            "#FFB300", "#D85A30", "#E5484D", "#D4537E", "#C2185B", "#9C27B0", "#7F77DD" };

        // "" = default orange, "windows" = the Windows accent color, otherwise a hex color.
        public static Color AccentFor(string setting)
        {
            if (setting == "windows") return Art.Accent();
            Color? a = Theme.ParseHex(setting);
            return a.HasValue ? a.Value : DefaultAccent;
        }

        static Palette DarkBase()
        {
            Palette p = new Palette();
            p.Dark = true;
            p.Window = Rgb(0x1B, 0x1B, 0x1E); p.Card = Rgb(0x24, 0x24, 0x28); p.Field = Rgb(0x1C, 0x1C, 0x1F);
            p.Text = Rgb(0xF2, 0xF2, 0xF4); p.Muted = Rgb(0xA6, 0xA6, 0xB0); p.Border = Rgb(0x34, 0x34, 0x3A);
            p.Hover = Rgb(0x2E, 0x2E, 0x33); p.Ring = Rgb(0x1E, 0x1F, 0x26); p.Button = Rgb(0x2E, 0x2F, 0x37);
            p.Danger = Rgb(0xF0, 0x95, 0x95);
            p.Rail = Rgb(0x16, 0x16, 0x18); p.Stage = Rgb(0x12, 0x12, 0x14); p.Dot = Rgb(0x26, 0x26, 0x2B);
            p.Track = Rgb(0x3A, 0x3A, 0x41); p.Success = Rgb(0x3D, 0xBE, 0x7A);
            return p;
        }

        static Palette LightBase()
        {
            Palette p = new Palette();
            p.Window = Rgb(0xF6, 0xF6, 0xF8); p.Card = Rgb(0xFF, 0xFF, 0xFF); p.Field = Rgb(0xF4, 0xF4, 0xF6);
            p.Text = Rgb(0x1C, 0x1C, 0x20); p.Muted = Rgb(0x5C, 0x5C, 0x66); p.Border = Rgb(0xE1, 0xE1, 0xE7);
            p.Hover = Rgb(0xED, 0xED, 0xF1); p.Ring = Rgb(0xE2, 0xE2, 0xE8); p.Button = Rgb(0xFF, 0xFF, 0xFF);
            p.Danger = Rgb(0xB4, 0x23, 0x18);
            p.Rail = Rgb(0xED, 0xED, 0xF1); p.Stage = Rgb(0xE5, 0xE6, 0xEB); p.Dot = Rgb(0xCF, 0xD1, 0xD8);
            p.Track = Rgb(0xE1, 0xE1, 0xE6); p.Success = Rgb(0x1E, 0x8E, 0x55);
            return p;
        }

        static Color Rgb(int r, int g, int b) { return Color.FromArgb(r, g, b); }
    }

    static class Theme
    {
        public static bool WindowsIsDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch { return false; }
        }

        public static float Lum(Color c) { return (c.R * 0.299f + c.G * 0.587f + c.B * 0.114f) / 255f; }

        // Dark text on light colors, white text on dark colors.
        public static Color TextOn(Color bg) { return Lum(bg) > 0.6f ? Color.FromArgb(28, 28, 32) : Color.White; }

        // WCAG contrast ratio between two colors: 1 (same) to 21 (black on white).
        public static float Contrast(Color a, Color b)
        {
            double la = RelLum(a), lb = RelLum(b);
            return (float)((Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05));
        }

        static double RelLum(Color c)
        {
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        static double Lin(int v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color? ParseHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim().TrimStart('#');
            int v;
            if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                return Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
            return null;
        }

        public static string ToHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        // h in degrees (0-360), s and v from 0 to 1.
        public static Color FromHsv(float h, float s, float v)
        {
            Func<float, int> f = delegate(float n)
            {
                float k = (n + h / 60f) % 6f;
                return (int)Math.Round((v - v * s * Math.Max(0f, Math.Min(Math.Min(k, 4f - k), 1f))) * 255f);
            };
            return Color.FromArgb(f(5), f(3), f(1));
        }

        public static void ToHsv(Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            h = 0;
            if (d > 0)
            {
                if (max == r) h = ((g - b) / d) % 6f;
                else if (max == g) h = (b - r) / d + 2f;
                else h = (r - g) / d + 4f;
                h *= 60f;
                if (h < 0) h += 360f;
            }
            s = max > 0 ? d / max : 0;
            v = max;
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void TitleBar(IntPtr hwnd, bool dark)
        {
            int v = dark ? 1 : 0;
            try { DwmSetWindowAttribute(hwnd, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref v, 4); } catch { }
        }
    }
}

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

        public static Palette Get(Config cfg)
        {
            bool dark = cfg.ThemeMode == "dark" || (cfg.ThemeMode != "light" && Theme.WindowsIsDark());
            Palette p = dark ? DarkBase() : LightBase();
            p.Accent = AccentFor(cfg.Accent);
            // Links in the accent color: light colors like orange are too faint on white, so darken them.
            p.AccentText = !dark && Theme.Lum(p.Accent) > 0.45f ? Theme.Mix(p.Accent, Color.Black, 0.35f) : p.Accent;
            return p;
        }

        public static readonly Color DefaultAccent = Color.FromArgb(0xEF, 0x9F, 0x27); // orange

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
            p.Window = Rgb(0x20, 0x20, 0x22); p.Card = Rgb(0x2B, 0x2B, 0x2E); p.Field = Rgb(0x1F, 0x1F, 0x22);
            p.Text = Rgb(0xF1, 0xF1, 0xF3); p.Muted = Rgb(0xA6, 0xA6, 0xAE); p.Border = Rgb(0x48, 0x48, 0x4E);
            p.Hover = Rgb(0x37, 0x37, 0x3C); p.Ring = Rgb(0x1E, 0x1F, 0x26); p.Button = Rgb(0x2E, 0x2F, 0x37);
            p.Danger = Rgb(0xF0, 0x95, 0x95);
            return p;
        }

        static Palette LightBase()
        {
            Palette p = new Palette();
            p.Window = Rgb(0xF3, 0xF3, 0xF3); p.Card = Rgb(0xFF, 0xFF, 0xFF); p.Field = Rgb(0xFF, 0xFF, 0xFF);
            p.Text = Rgb(0x1F, 0x1F, 0x22); p.Muted = Rgb(0x5F, 0x5F, 0x66); p.Border = Rgb(0xD4, 0xD4, 0xD9);
            p.Hover = Rgb(0xEE, 0xEE, 0xF1); p.Ring = Rgb(0xE2, 0xE2, 0xE8); p.Button = Rgb(0xFF, 0xFF, 0xFF);
            p.Danger = Rgb(0xC4, 0x2B, 0x1C);
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

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void TitleBar(IntPtr hwnd, bool dark)
        {
            int v = dark ? 1 : 0;
            try { DwmSetWindowAttribute(hwnd, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref v, 4); } catch { }
        }
    }
}

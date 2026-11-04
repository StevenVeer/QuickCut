using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace QuickCut
{
    static class Actions
    {
        public static readonly string[] MediaIds = { "playpause", "prev", "next", "stop", "volup", "voldown", "mute" };
        public static string[] MediaNames { get { return Str.MediaNames; } }
        static string[] MediaShort { get { return Str.MediaShort; } }
        static readonly byte[] MediaVk = { 0xB3, 0xB1, 0xB0, 0xB2, 0xAF, 0xAE, 0xAD };

        const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;

        public static string MediaShortName(string id)
        {
            int i = Array.IndexOf(MediaIds, id);
            return i < 0 ? "" : MediaShort[i];
        }

        public static string ExpandPath(string s)
        {
            return Environment.ExpandEnvironmentVariables((s ?? "").Trim().Trim('"'));
        }

        public static bool IsWebUrl(string s)
        {
            s = (s ?? "").Trim();
            return s.IndexOf(':') < 0 || s.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
        }

        public static void Execute(RingItem it)
        {
            switch (it.Type)
            {
                case ActionType.Program:
                {
                    string path = ExpandPath(it.Target);
                    if (path.Length == 0) throw new InvalidOperationException(Str.NoProgram);
                    // Already open: go to its window instead of starting another copy (browsers would open a new window).
                    if (WindowSwitcher.TrySwitch(path)) break;
                    ProcessStartInfo psi = new ProcessStartInfo(path);
                    psi.UseShellExecute = true;
                    if (!string.IsNullOrEmpty(it.Args)) psi.Arguments = Environment.ExpandEnvironmentVariables(it.Args);
                    try { if (File.Exists(path)) psi.WorkingDirectory = Path.GetDirectoryName(path); } catch { }
                    Start(psi);
                    break;
                }
                case ActionType.Url:
                {
                    string url = (it.Target ?? "").Trim();
                    if (url.Length == 0) throw new InvalidOperationException(Str.NoAddress);
                    if (url.IndexOf(':') < 0) url = "https://" + url;
                    ProcessStartInfo psi = new ProcessStartInfo(url);
                    psi.UseShellExecute = true;
                    Start(psi);
                    break;
                }
                case ActionType.Media:
                {
                    int i = Array.IndexOf(MediaIds, it.Target);
                    if (i < 0) throw new InvalidOperationException(string.Format(Str.UnknownMedia, it.Target));
                    Native.keybd_event(MediaVk[i], 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    Native.keybd_event(MediaVk[i], 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;
                }
                case ActionType.Keys:
                    SendCombo(it.Target);
                    break;
            }
        }

        static void Start(ProcessStartInfo psi)
        {
            Process p = Process.Start(psi);
            if (p != null) p.Dispose();
        }

        static void SendCombo(string combo)
        {
            uint mods; Keys key;
            if (!KeyCombo.TryParse(combo, out mods, out key))
                throw new InvalidOperationException(string.Format(Str.InvalidCombo, combo));
            List<byte> down = new List<byte>();
            if ((mods & KeyCombo.MOD_CONTROL) != 0) down.Add(0x11);
            if ((mods & KeyCombo.MOD_ALT) != 0) down.Add(0x12);
            if ((mods & KeyCombo.MOD_SHIFT) != 0) down.Add(0x10);
            if ((mods & KeyCombo.MOD_WIN) != 0) down.Add(0x5B);
            down.Add((byte)key);
            foreach (byte vk in down) Native.keybd_event(vk, 0, Ext(vk), UIntPtr.Zero);
            for (int i = down.Count - 1; i >= 0; i--) Native.keybd_event(down[i], 0, Ext(down[i]) | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        static uint Ext(byte vk)
        {
            // Navigation keys, Win and media/browser keys are "extended" keys.
            bool ext = (vk >= 0x21 && vk <= 0x2E) || vk == 0x5B || vk == 0x5C || (vk >= 0xA6 && vk <= 0xB7) || vk == 0x6F;
            return ext ? KEYEVENTF_EXTENDEDKEY : 0;
        }
    }

    static class KeyCombo
    {
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

        public static bool TryParse(string s, out uint mods, out Keys key)
        {
            mods = 0; key = Keys.None;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (string raw in s.Split('+'))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string l = p.ToLowerInvariant();
                if (l == "ctrl" || l == "control") mods |= MOD_CONTROL;
                else if (l == "alt") mods |= MOD_ALT;
                else if (l == "shift") mods |= MOD_SHIFT;
                else if (l == "win" || l == "windows") mods |= MOD_WIN;
                else
                {
                    if (key != Keys.None) return false;
                    int sym = Array.IndexOf(Symbols, p);
                    if (sym >= 0) { key = SymbolKeys[sym]; continue; }
                    if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
                    else if (char.IsDigit(p[0])) return false;
                    Keys k;
                    if (!Enum.TryParse(p, true, out k)) return false;
                    key = k & Keys.KeyCode;
                }
            }
            return key != Keys.None;
        }

        public static string Format(uint mods, Keys key)
        {
            StringBuilder sb = new StringBuilder();
            if ((mods & MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((mods & MOD_ALT) != 0) sb.Append("Alt+");
            if ((mods & MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((mods & MOD_WIN) != 0) sb.Append("Win+");
            int sym = Array.IndexOf(SymbolKeys, key);
            if (sym >= 0) sb.Append(Symbols[sym]);
            else if (key >= Keys.D0 && key <= Keys.D9) sb.Append((int)(key - Keys.D0));
            else sb.Append(key);
            return sb.ToString();
        }

        // Punctuation keys are shown as the character instead of names like "Oemtilde".
        static readonly string[] Symbols = { "`", "-", "=", "[", "]", "\\", ";", "'", ",", ".", "/" };
        static readonly Keys[] SymbolKeys = { Keys.Oemtilde, Keys.OemMinus, Keys.Oemplus, Keys.OemOpenBrackets, Keys.Oem6,
                                              Keys.Oem5, Keys.Oem1, Keys.Oem7, Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion };
    }
}

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
            return !HasScheme(s) || s.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
        }

        // True when the text starts with a link type like "https:", "spotify:" or "ms-settings:".
        // "localhost:3000" or "example.com:8080/x" only look like one: that's a host with a port number.
        // A host is told apart by being localhost or having a dot (a domain or IP address) and being
        // followed by up to 5 digits, so "tel:112" and reverse-DNS links like "com.epicgames.launcher://"
        // still count as links.
        static bool HasScheme(string s)
        {
            int c = s.IndexOf(':');
            if (c <= 0 || s[0] == '[') return false; // no ':', or an IPv6 address like [::1]:8080
            string head = s.Substring(0, c);
            if (!char.IsLetter(head[0])) return false;
            foreach (char ch in head)
                if (!char.IsLetterOrDigit(ch) && ch != '+' && ch != '-' && ch != '.') return false;
            bool hostLike = head.Equals("localhost", StringComparison.OrdinalIgnoreCase) || head.IndexOf('.') >= 0;
            if (!hostLike) return true;
            int d = c + 1;
            while (d < s.Length && char.IsDigit(s[d])) d++;
            bool port = d > c + 1 && d - c - 1 <= 5 && (d == s.Length || s[d] == '/' || s[d] == '?' || s[d] == '#');
            return !port;
        }

        // The address a web button opens: "youtube.com" gets https://. A local address (localhost or an
        // IP number, like a router page or a dev server) gets http://, since those rarely have https.
        public static string WebAddress(string s)
        {
            string url = (s ?? "").Trim();
            if (url.Length == 0 || HasScheme(url)) return url;
            string host = url;
            int end = host.IndexOfAny(new[] { '/', '?', '#' });
            if (end >= 0) host = host.Substring(0, end);
            if (host.StartsWith("[")) return "http://" + url; // IPv6
            int colon = host.IndexOf(':');
            if (colon >= 0) host = host.Substring(0, colon);
            System.Net.IPAddress ip;
            bool local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || (host.Split('.').Length == 4 && System.Net.IPAddress.TryParse(host, out ip));
            return (local ? "http://" : "https://") + url;
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
                    string url = WebAddress(it.Target);
                    if (url.Length == 0) throw new InvalidOperationException(Str.NoAddress);
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
            s = s.Trim();
            // "Ctrl++" (or a lone "+") means the plus key: the last '+' is the key, not a separator.
            if (s == "+" || s.EndsWith("++")) { key = Keys.Oemplus; s = s.Substring(0, s.Length - 1); }
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
                    int alias = Array.IndexOf(Aliases, l);
                    if (alias >= 0) { key = AliasKeys[alias]; continue; }
                    if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
                    else if (char.IsDigit(p[0])) return false;
                    // Only plain names: Enum.TryParse would also take "A,B" (A|B, which is C) or "-5".
                    foreach (char ch in p) if (!char.IsLetterOrDigit(ch)) return false;
                    Keys k;
                    if (!Enum.TryParse(p, true, out k)) return false;
                    key = k & Keys.KeyCode;
                }
            }
            return key != Keys.None;
        }

        // Short names people type in config.ini that aren't the names of the Keys enum (Format writes those).
        static readonly string[] Aliases = { "esc", "del", "ins", "pgup", "pgdn", "pgdown", "prtsc", "prtscn", "plus" };
        static readonly Keys[] AliasKeys = { Keys.Escape, Keys.Delete, Keys.Insert, Keys.PageUp, Keys.PageDown, Keys.PageDown,
                                             Keys.PrintScreen, Keys.PrintScreen, Keys.Oemplus };

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

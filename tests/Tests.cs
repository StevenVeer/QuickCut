using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using QuickCut;

namespace QuickCutTests
{
    static class Tests
    {
        static void Check(string name, bool ok, string detail = "") { Runner.Check(name, ok, detail); }

        // Parses a combo and formats it back, or "<invalid>".
        static string Combo(string s)
        {
            uint m; Keys k;
            return KeyCombo.TryParse(s, out m, out k) ? KeyCombo.Format(m, k) : "<invalid>";
        }

        static bool IsKey(string s, Keys expected)
        {
            uint m; Keys k;
            return KeyCombo.TryParse(s, out m, out k) && k == expected;
        }

        static void Same(string input, string expected)
        {
            string got = Combo(input);
            Check("\"" + input + "\" -> " + expected, got == expected, "got " + got);
        }

        public static void KeyComboTests()
        {
            Runner.Group("KeyCombo");
            Same("Ctrl+Space", "Ctrl+Space");
            Same("ctrl+shift+a", "Ctrl+Shift+A");
            Same("Control+Alt+Delete", "Ctrl+Alt+Delete");
            Same("Win+D", "Win+D");
            Same("Ctrl+1", "Ctrl+1");
            Same("Ctrl+-", "Ctrl+-");
            Same("Ctrl+/", "Ctrl+/");
            Same("Ctrl+Shift+Escape", "Ctrl+Shift+Escape");
            Same("F13", "F13");
            Same(" Shift + F5 ", "Shift+F5");
            Same("A+B", "<invalid>");          // two keys
            Same("Ctrl+Shift", "<invalid>");   // only modifiers
            Same("Ctrl+65", "<invalid>");      // a number isn't a key name
            Same("", "<invalid>");
            Same("Ctrl+Nonsense", "<invalid>");
            // Short names people type by hand (the config.ini header uses Esc)
            Same("Ctrl+Shift+Esc", "Ctrl+Shift+Escape");
            Same("Ctrl+Alt+Del", "Ctrl+Alt+Delete");
            Same("Shift+Ins", "Shift+Insert");
            // PageUp/PageDown share their values with Prior/Next, so compare keys, not names
            Check("\"Ctrl+PgUp\" -> PageUp", IsKey("Ctrl+PgUp", Keys.PageUp));
            Check("\"Ctrl+PgDn\" -> PageDown", IsKey("Ctrl+PgDn", Keys.PageDown) && IsKey("PgDown", Keys.PageDown));
            Same("PrtSc", "PrintScreen");
            Same("Ctrl++", "Ctrl+=");           // the plus key (= with Shift on US keyboards)
            Same("+", "=");
            Same("Ctrl+", "<invalid>");          // a trailing separator isn't the plus key
            // Only plain key names: Enum.TryParse would read "A,B" as A|B (= C) and "-5" as a number
            Same("Ctrl+A,B", "<invalid>");
            Same("Ctrl+-5", "<invalid>");
            Same("Ctrl+,", "Ctrl+,");           // the comma key itself still works

            // Everything Format writes must parse back to the same combo (config.ini relies on it).
            Keys[] keys = { Keys.A, Keys.D0, Keys.D9, Keys.F1, Keys.F24, Keys.Space, Keys.Escape, Keys.Enter, Keys.Tab,
                Keys.Oemtilde, Keys.OemMinus, Keys.Oemplus, Keys.OemOpenBrackets, Keys.Oem6, Keys.Oem5, Keys.Oem1,
                Keys.Oem7, Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion, Keys.NumPad5, Keys.Add, Keys.Home, Keys.Delete };
            uint[] mods = { 0, KeyCombo.MOD_CONTROL, KeyCombo.MOD_CONTROL | KeyCombo.MOD_SHIFT,
                KeyCombo.MOD_ALT | KeyCombo.MOD_WIN, KeyCombo.MOD_CONTROL | KeyCombo.MOD_ALT | KeyCombo.MOD_SHIFT | KeyCombo.MOD_WIN };
            int bad = 0; string firstBad = "";
            foreach (uint m in mods)
                foreach (Keys k in keys)
                {
                    string s = KeyCombo.Format(m, k);
                    uint m2; Keys k2;
                    if (!KeyCombo.TryParse(s, out m2, out k2) || m2 != m || k2 != k) { if (bad++ == 0) firstBad = s; }
                }
            Check("Format -> TryParse round-trips", bad == 0, bad + " failed, first: " + firstBad);
        }

        public static void RingItemTests()
        {
            Runner.Group("RingItem serialization");
            RingItem b = new RingItem(ActionType.Program, "Pipe|Label", @"C:\x.exe");
            b.Args = "--a|b"; b.ColorHex = "#112233";
            RingItem rb = RingItem.Parse(b.Serialize());
            Check("program item round-trips", rb != null && rb.Type == ActionType.Program && rb.Target == @"C:\x.exe");
            Check("args with '|' round-trip", rb.Args == "--a|b", rb.Args);
            Check("color round-trips", rb.ColorHex == "#112233", rb.ColorHex);
            Check("'|' in a label becomes '/'", rb.Label == "Pipe/Label", rb.Label);

            RingItem c = new RingItem(ActionType.Keys, "Lbl", "Ctrl+Shift+Escape");
            c.Args = "line1\r\nline2";
            Check("newlines are flattened to spaces", RingItem.Parse(c.Serialize()).Args == "line1  line2", RingItem.Parse(c.Serialize()).Args);
            Check("every action type round-trips", RoundTripsAllTypes());

            RingItem a = new RingItem(ActionType.Url, "Search", "https://example.com/?q=a|b");
            RingItem ra = RingItem.Parse(a.Serialize());
            Check("a web address with '|' round-trips", ra != null && ra.Target == a.Target && ra.ColorHex == "" && ra.Args == "",
                ra == null ? "null" : "target=" + ra.Target + " color=" + ra.ColorHex + " args=" + ra.Args);
            Check("'|' in a target is saved as %7C", a.Serialize() == "Url|Search|https://example.com/?q=a%7Cb||", a.Serialize());
            Check("a target with %7c reads as '|'", RingItem.Parse("Url|S|x.com/?q=a%7cb||").Target == "x.com/?q=a|b");
            Check("other % escapes are left alone", RingItem.Parse(@"Program|S|%APPDATA%\x%20y.exe||").Target == @"%APPDATA%\x%20y.exe");

            Check("old 4-field format reads args", RingItem.Parse(@"Program|X|C:\a.exe|-v").Args == "-v");
            Check("type is case-insensitive", RingItem.Parse("media|Play|playpause") != null);
            Check("unknown type is rejected", RingItem.Parse("Nope|X|Y") == null);
            Check("too few fields are rejected", RingItem.Parse("Program|X") == null);

            RingItem orig = new RingItem(ActionType.Url, "Site", "example.com"); orig.ColorHex = "#ABCDEF"; orig.Args = "x";
            RingItem copy = orig.Clone();
            copy.Label = "Changed";
            Check("Clone copies every field", copy.Type == orig.Type && copy.Target == orig.Target && copy.ColorHex == orig.ColorHex && copy.Args == orig.Args);
            Check("Clone is independent", orig.Label == "Site");
        }

        static bool RoundTripsAllTypes()
        {
            foreach (ActionType t in Enum.GetValues(typeof(ActionType)))
            {
                RingItem r = RingItem.Parse(new RingItem(t, "L", "T").Serialize());
                if (r == null || r.Type != t || r.Label != "L" || r.Target != "T") return false;
            }
            return true;
        }

        public static void VolumeStepTests()
        {
            Runner.Group("Volume.NextStep");
            // percent, step, direction, expected
            int[,] cases = {
                { 37, 10, 1, 40 }, { 37, 10, -1, 30 }, { 40, 10, 1, 50 }, { 40, 10, -1, 30 },
                { 0, 5, -1, 0 }, { 100, 5, 1, 100 }, { 99, 5, 1, 100 }, { 1, 5, -1, 0 }, { 50, 5, -1, 45 }, { 52, 5, 1, 55 } };
            for (int i = 0; i < cases.GetLength(0); i++)
            {
                int got = Volume.NextStep(cases[i, 0], cases[i, 1], cases[i, 2]);
                Check(string.Format("{0}% {1} {2} -> {3}%", cases[i, 0], cases[i, 2] > 0 ? "up" : "down", cases[i, 1], cases[i, 3]),
                    got == cases[i, 3], "got " + got);
            }
        }

        public static void HitTestTests()
        {
            Runner.Group("Ring hit-testing");
            using (RingRenderer r = new RingRenderer(1f))
            {
                Check("center", r.HitTest(new PointF(r.Cx, r.Cy), 8, false) == RingRenderer.HitCenter);
                Check("top = button 1 of 8", r.HitTest(new PointF(r.Cx, r.Cy - 110), 8, false) == 0);
                Check("right = button 3 of 8", r.HitTest(new PointF(r.Cx + 110, r.Cy), 8, false) == 2);
                Check("bottom = button 5 of 8", r.HitTest(new PointF(r.Cx, r.Cy + 110), 8, false) == 4);
                Check("left = button 7 of 8", r.HitTest(new PointF(r.Cx - 110, r.Cy), 8, false) == 6);
                Check("just right of top = button 1 of 12", r.HitTest(new PointF(r.Cx + 10, r.Cy - 110), 12, false) == 0);
                Check("outside the ring, click mode = nothing", r.HitTest(new PointF(r.Cx + 200, r.Cy), 8, false) == RingRenderer.HitNone);
                Check("outside the ring, hold mode = direction", r.HitTest(new PointF(r.Cx + 400, r.Cy), 8, true) == 2);
                Check("no buttons = nothing on the ring", r.HitTest(new PointF(r.Cx, r.Cy - 110), 0, false) == RingRenderer.HitNone);
                Check("one button fills the ring", r.HitTest(new PointF(r.Cx, r.Cy + 110), 1, false) == 0);
                Check("Contains the ring", r.Contains(new PointF(r.Cx, r.Cy - 140)));
                Check("doesn't contain the corner", !r.Contains(new PointF(1, 1)));
            }
            using (RingRenderer r = new RingRenderer(1f, 1f, true, true))
            {
                Check("panels make it taller", r.Height > r.Size);
                Check("volume minus button", r.HitVolume(new PointF(r.Cx - 110, r.Size + 27), 5) == RingRenderer.VolDown);
                Check("volume plus button", r.HitVolume(new PointF(r.Cx + 110, r.Size + 27), 5) == RingRenderer.VolUp);
                Check("first volume block", r.HitVolume(new PointF(r.Cx - 85, r.Size + 35), 5) == 0);
                Check("last volume block (10% steps)", r.HitVolume(new PointF(r.Cx + 85, r.Size + 35), 10) == 9);
                Check("media panel hidden while nothing plays", r.HitMedia(new PointF(r.Cx, r.Size + 2 + 56 + 25)) == RingRenderer.MedNone);
            }
        }

        public static void HelperTests()
        {
            Runner.Group("Helpers");
            Check("AppName Spotify.exe", Media.AppName("Spotify.exe") == "Spotify");
            Check("AppName Store app id", Media.AppName("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic") == "ZuneMusic");
            Check("AppName chrome", Media.AppName("chrome") == "Chrome");
            Check("AppName empty", Media.AppName("") == "" && Media.AppName(null) == "");
            Check("ExeFromCommand quoted", AppCatalog.ExeFromCommand("\"C:\\P F\\a.exe\" --x \"%1\"") == "C:\\P F\\a.exe");
            Check("ExeFromCommand unquoted", AppCatalog.ExeFromCommand("C:\\a.exe %1") == "C:\\a.exe");
            Check("ExeFromCommand empty", AppCatalog.ExeFromCommand("") == null && AppCatalog.ExeFromCommand(null) == null);
            string exe = AppCatalog.ExeFromCommand("C:\\Program Files\\a.exe %1");
            Check("ExeFromCommand unquoted path with spaces", exe == "C:\\Program Files\\a.exe", "got " + exe);
            exe = AppCatalog.ExeFromCommand("C:\\Program Files\\B.EXE");
            Check("ExeFromCommand unquoted, no arguments", exe == "C:\\Program Files\\B.EXE", "got " + exe);
            exe = AppCatalog.ExeFromCommand("rundll32.exe url.dll,FileProtocolHandler %1");
            Check("ExeFromCommand stops at the first .exe", exe == "rundll32.exe", "got " + exe);
            Check("IsAppsFolder", AppCatalog.IsAppsFolder(@"shell:AppsFolder\X!Y") && !AppCatalog.IsAppsFolder(@"C:\a.exe"));
            Check("ParseHex #1a2B3c", Theme.ParseHex("#1a2B3c") == Color.FromArgb(255, 0x1a, 0x2b, 0x3c));
            Check("ParseHex without #", Theme.ParseHex("FFFFFF") == Color.FromArgb(255, 255, 255, 255));
            Check("ParseHex rejects bad input", Theme.ParseHex("#12345") == null && Theme.ParseHex("zzzzzz") == null && Theme.ParseHex("") == null);
            Check("ToHex", Theme.ToHex(Color.FromArgb(1, 2, 255)) == "#0102FF");
            Check("HSV round-trips on every preset", HsvRoundTrips());
            Check("TextOn: dark text on white, white on black",
                Theme.TextOn(Color.White).GetBrightness() < 0.5f && Theme.TextOn(Color.Black) == Color.White);
            Check("IsWebUrl youtube.com", Actions.IsWebUrl("youtube.com"));
            Check("IsWebUrl https", Actions.IsWebUrl("https://x.com") && Actions.IsWebUrl("HTTP://x.com"));
            Check("IsWebUrl spotify: is not web", !Actions.IsWebUrl("spotify:"));
            Check("IsWebUrl localhost:3000", Actions.IsWebUrl("localhost:3000"));
            Check("IsWebUrl example.com:8080/x", Actions.IsWebUrl("example.com:8080/x"));
            Check("IsWebUrl 192.168.1.1:80", Actions.IsWebUrl("192.168.1.1:80"));
            Check("IsWebUrl [::1]:3000", Actions.IsWebUrl("[::1]:3000"));
            Check("tel:112 is a link, not a host", !Actions.IsWebUrl("tel:112"));
            Check("mailto: is a link", !Actions.IsWebUrl("mailto:a@b.com"));
            Check("reverse-DNS link (com.epicgames.launcher://)", !Actions.IsWebUrl("com.epicgames.launcher://apps/x"));
            Check("ms-settings: is a link", !Actions.IsWebUrl("ms-settings:sound"));
            WebAddress("youtube.com", "https://youtube.com");
            WebAddress("  example.com:8080/a?b ", "https://example.com:8080/a?b");
            WebAddress("localhost:3000", "http://localhost:3000");
            WebAddress("LOCALHOST/x", "http://LOCALHOST/x");
            WebAddress("192.168.1.1", "http://192.168.1.1");
            WebAddress("10.0.0.2:8443/admin", "http://10.0.0.2:8443/admin");
            WebAddress("[::1]:3000", "http://[::1]:3000");
            WebAddress("https://x.com", "https://x.com");
            WebAddress("spotify:", "spotify:");
            WebAddress("", "");
            Check("MediaShortName", Actions.MediaShortName("next") == Str.MediaShort[2] && Actions.MediaShortName("nope") == "");
            Check("preset counts match their names",
                Palette.ButtonPresets.Length == Str.ButtonPresetNames.Length && Palette.HoverPresets.Length == Str.HoverPresetNames.Length);
            Check("media ids match their names",
                Actions.MediaIds.Length == Str.MediaNames.Length && Actions.MediaIds.Length == Str.MediaShort.Length);
            Check("every preset is a valid color", AllPresetsParse());
        }

        static void WebAddress(string input, string expected)
        {
            string got = Actions.WebAddress(input);
            Check("WebAddress \"" + input + "\" -> " + expected, got == expected, "got " + got);
        }

        // Saving and the crash log, in a temp folder (never the real %APPDATA%\QuickCut).
        public static void FileTests()
        {
            Runner.Group("Files");
            string dir = Path.Combine(Path.GetTempPath(), "QuickCutTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string f = Path.Combine(dir, "config.ini");
                Config.WriteAtomic(f, "one");
                Check("WriteAtomic creates the file", File.ReadAllText(f) == "one");
                Config.WriteAtomic(f, "two\r\nlines é");
                Check("WriteAtomic replaces the file", File.ReadAllText(f) == "two\r\nlines é");
                Check("WriteAtomic leaves no temp file", !File.Exists(f + ".tmp"));
                Check("WriteAtomic writes UTF-8 without a BOM", File.ReadAllBytes(f)[0] == (byte)'t');

                string log = Path.Combine(dir, "sub", "crash.log");
                CrashLog.WriteTo(log, "test", new InvalidOperationException("boom"));
                CrashLog.WriteTo(log, "test", null);
                string text = File.ReadAllText(log);
                Check("crash log has the error and its type", text.Contains("boom") && text.Contains("InvalidOperationException"));
                Check("crash log appends", text.Contains("(no details)") && text.IndexOf("boom") < text.IndexOf("(no details)"));
                File.WriteAllText(log, new string('x', 300 * 1024));
                CrashLog.WriteTo(log, "test", new Exception("fresh"));
                Check("crash log starts over when it's too big", new FileInfo(log).Length < 10 * 1024 && File.ReadAllText(log).Contains("fresh"));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        static bool HsvRoundTrips()
        {
            foreach (string[] set in new[] { Palette.HoverPresets, Palette.ButtonPresets })
                foreach (string hx in set)
                {
                    Color col = Theme.ParseHex(hx).Value;
                    float h, s, v;
                    Theme.ToHsv(col, out h, out s, out v);
                    Color back = Theme.FromHsv(h, s, v);
                    if (Math.Abs(back.R - col.R) > 1 || Math.Abs(back.G - col.G) > 1 || Math.Abs(back.B - col.B) > 1) return false;
                }
            return true;
        }

        static bool AllPresetsParse()
        {
            foreach (string hx in Palette.ButtonPresets) if (Theme.ParseHex(hx) == null) return false;
            foreach (string hx in Palette.HoverPresets) if (Theme.ParseHex(hx) == null) return false;
            return true;
        }

        // Real bugs that aren't fixed yet. They're reported but don't fail the run.
        // When one starts passing, move it into the group above as a normal Check.
        // None right now: add one with Runner.Group("Known bugs (not fixed yet)") and Runner.KnownBug(...).
        public static void KnownBugs()
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using QuickCut;

namespace QuickCutTests
{
    // Import / export of .quickcut files. Only uses a temp folder; never the real config.ini.
    static class TransferTests
    {
        static void Check(string name, bool ok, string detail = "") { Runner.Check(name, ok, detail); }

        static readonly List<AppEntry> NoApps = new List<AppEntry>();

        public static void Run()
        {
            Runner.Group("Import / export");
            string dir = Path.Combine(Path.GetTempPath(), "QuickCutTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                RoundTrip(dir);
                PortablePaths();
                BadFiles(dir);
                Cleanup();
                MissingApps();
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        static ConfigTransfer.ImportResult ParseText(string text, List<AppEntry> apps)
        {
            return ConfigTransfer.Parse(text, apps ?? NoApps);
        }

        static void RoundTrip(string dir)
        {
            Config src = new Config();
            src.ThemeMode = "dark"; src.HoverStyle = "grow"; src.Accent = "#3DDC97"; src.ButtonColor = "#1F3A5F"; src.RingSize = "large";
            src.Center = new RingItem(ActionType.Media, "Play", "playpause");
            RingItem url = new RingItem(ActionType.Url, "Mail", "https://mail.example.com/?a=1|2"); url.ColorHex = "#112233";
            src.Items.Add(url);
            src.Items.Add(new RingItem(ActionType.Media, "Next", "next"));
            src.Items.Add(new RingItem(ActionType.Keys, "Task Manager", "Ctrl+Shift+Escape"));
            string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            RingItem prog = new RingItem(ActionType.Program, "Notepad", notepad); prog.Args = "-x";
            src.Items.Add(prog);

            string file = Path.Combine(dir, "ring" + ConfigTransfer.Extension);
            ConfigTransfer.Export(src, file);
            string text = File.ReadAllText(file);
            Check("export writes no PC settings", !text.Contains("hotkey=") && !text.Contains("mouse=") && !text.Contains("volumestep="));
            Check("export writes Windows folders as variables", text.Contains("%SystemRoot%") && !text.Contains(Environment.SystemDirectory),
                text);

            ConfigTransfer.ImportResult r = ConfigTransfer.Import(file, NoApps);
            Check("exported file imports without errors", r.Error == null && r.Missing.Count == 0, r.Error + " / missing " + string.Join(",", r.Missing.ToArray()));
            if (r.Config == null) return;

            Config mine = new Config();
            mine.Hotkey = "Alt+Q"; mine.MouseTrigger = false; mine.SkipFullscreen = false; mine.VolumeStep = 10; mine.MediaBar = false;
            mine.Items.Add(new RingItem(ActionType.Url, "Old", "old.example.com"));
            ConfigTransfer.ApplyTo(r.Config, mine);

            Check("look comes back the same", mine.ThemeMode == "dark" && mine.HoverStyle == "grow" && mine.Accent == "#3DDC97"
                && mine.ButtonColor == "#1F3A5F" && mine.RingSize == "large");
            Check("center button comes back the same", mine.Center.Serialize() == src.Center.Serialize());
            bool same = mine.Items.Count == src.Items.Count;
            for (int i = 0; same && i < 3; i++) same = mine.Items[i].Serialize() == src.Items[i].Serialize();
            Check("buttons come back the same (replacing the old ones)", same);
            Check("program path expands back to the same file", mine.Items.Count == 4
                && string.Equals(Actions.ExpandPath(mine.Items[3].Target), notepad, StringComparison.OrdinalIgnoreCase) && mine.Items[3].Args == "-x",
                mine.Items.Count == 4 ? mine.Items[3].Target : "");
            Check("import keeps this PC's settings", mine.Hotkey == "Alt+Q" && !mine.MouseTrigger && !mine.SkipFullscreen && mine.VolumeStep == 10 && !mine.MediaBar);

            // A renamed config.ini imports too, but only its buttons and look.
            string ini = Path.Combine(dir, "config.quickcut");
            File.WriteAllText(ini, "hotkey=Win+Z\r\nmouse=0\r\ntheme=light\r\nitem=Url|Site|example.com||\r\n", new UTF8Encoding(true)); // with a BOM
            r = ConfigTransfer.Import(ini, NoApps);
            Config other = new Config(); other.Hotkey = "Ctrl+Space";
            if (r.Config != null) ConfigTransfer.ApplyTo(r.Config, other);
            Check("a renamed config.ini imports its buttons, not its hotkey", r.Error == null && other.Items.Count == 1
                && other.ThemeMode == "light" && other.Hotkey == "Ctrl+Space" && other.MouseTrigger);
        }

        static void PortablePaths()
        {
            var folders = new List<KeyValuePair<string, string>> {
                new KeyValuePair<string, string>("USERPROFILE", @"C:\Users\bob"),
                new KeyValuePair<string, string>("APPDATA", @"C:\Users\bob\AppData\Roaming"),
                new KeyValuePair<string, string>("ProgramFiles", @"C:\Program Files"),
                new KeyValuePair<string, string>("ProgramFiles(x86)", @"C:\Program Files (x86)") };
            Func<string, string> P = s => ConfigTransfer.Portable(s, folders);
            Check("AppData path uses %APPDATA%", P(@"C:\Users\bob\AppData\Roaming\Spotify\Spotify.exe") == @"%APPDATA%\Spotify\Spotify.exe");
            Check("profile path uses %USERPROFILE%", P(@"C:\Users\bob\Games\x.exe") == @"%USERPROFILE%\Games\x.exe");
            Check("matching ignores case", P(@"c:\users\BOB\a.exe") == @"%USERPROFILE%\a.exe");
            Check("a lookalike folder is left alone", P(@"C:\Users\bobby\a.exe") == @"C:\Users\bobby\a.exe");
            Check("Program Files (x86) isn't mistaken for Program Files", P(@"C:\Program Files (x86)\Steam\steam.exe") == @"%ProgramFiles(x86)%\Steam\steam.exe",
                P(@"C:\Program Files (x86)\Steam\steam.exe"));
            Check("paths inside arguments are replaced", P("--dir \"C:\\Users\\bob\\Docs\" --x") == "--dir \"%USERPROFILE%\\Docs\" --x");
            Check("other paths stay as they are", P(@"D:\Tools\a.exe") == @"D:\Tools\a.exe");
            Check("a bare folder is replaced too", P(@"C:\Users\bob") == "%USERPROFILE%");
            Check("empty stays empty", P("") == "" && P(null) == "");
        }

        static void BadFiles(string dir)
        {
            string empty = Path.Combine(dir, "empty.quickcut");
            File.WriteAllText(empty, "");
            Check("an empty file is refused", ConfigTransfer.Import(empty, NoApps).Error == Str.ImportNotQuickCut);
            Check("plain text without buttons is refused", ParseText("hello world\nfoo=bar\ntheme=dark\n", null).Error == Str.ImportNotQuickCut);
            string bin = Path.Combine(dir, "bin.quickcut");
            File.WriteAllBytes(bin, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 1, 2, 3, (byte)'i', (byte)'t', (byte)'e', (byte)'m', (byte)'=' });
            Check("a binary file is refused", ConfigTransfer.Import(bin, NoApps).Error == Str.ImportNotQuickCut);
            string big = Path.Combine(dir, "big.quickcut");
            StringBuilder sb = new StringBuilder();
            while (sb.Length <= ConfigTransfer.MaxFileSize) sb.Append("item=Url|Site|example.com||\n");
            File.WriteAllText(big, sb.ToString());
            Check("a huge file is refused", ConfigTransfer.Import(big, NoApps).Error == Str.ImportTooBig);
            ConfigTransfer.ImportResult r = ConfigTransfer.Import(Path.Combine(dir, "nope.quickcut"), NoApps);
            Check("a missing file gives an error, not a crash", r.Error != null && r.Config == null);
        }

        static void Cleanup()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 50; i++) sb.Append("item=Url|Site " + i + "|example.com||\n");
            ConfigTransfer.ImportResult r = ParseText(sb.ToString(), null);
            Check("at most 12 buttons are read", r.Config != null && r.Config.Items.Count == Config.MaxItems);

            r = ParseText("theme=purple\nhover=wobble\nsize=huge\naccent=#zzz\nbuttoncolor=#12345\nitem=Url|A|a.com|red|\n", null);
            Check("unknown theme, hover and size fall back to the defaults",
                r.Config.ThemeMode == "system" && r.Config.HoverStyle == "fill" && r.Config.RingSize == "medium");
            Check("invalid colors fall back to automatic", r.Config.Accent == "" && r.Config.ButtonColor == "" && r.Config.Items[0].ColorHex == "");
            r = ParseText("accent=windows\nbuttoncolor=#abcdef\nitem=Url|A|a.com|#00ff00|\n", null);
            Check("valid colors are kept", r.Config.Accent == "windows" && r.Config.ButtonColor == "#ABCDEF" && r.Config.Items[0].ColorHex == "#00FF00");

            r = ParseText("item=Media|Boom|selfdestruct||\nitem=Media|Next|next||\ncenter=Media|X|bogus||\n", null);
            Check("an unknown media action is dropped", r.Config.Items.Count == 1 && r.Config.Items[0].Target == "next");
            Check("an unusable center button falls back to Play/Pause", r.Config.Center.Type == ActionType.Media && r.Config.Center.Target == "playpause");

            r = ParseText("item=Url|" + new string('L', 500) + "|" + new string('t', 5000) + "||" + new string('a', 5000) + "\n", null);
            RingItem it = r.Config.Items[0];
            Check("overlong text is cut", it.Label.Length == 100 && it.Target.Length == 2048 && it.Args.Length == 2048);
            r = ParseText("bogus line without equals\n\n# comment\nwhatever=1\nitem=Keys|Lock|Win+L||\n", null);
            Check("unknown lines are skipped", r.Error == null && r.Config.Items.Count == 1);
        }

        static void MissingApps()
        {
            List<AppEntry> apps = new List<AppEntry> {
                new AppEntry("Spotify", @"C:\fake\Start Menu\Spotify.lnk"),
                new AppEntry("Steam", @"C:\fake\Start Menu\Steam.lnk"),
                new AppEntry("Calculator", AppCatalog.AppsFolderPrefix + "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App") };
            ConfigTransfer.ImportResult r = ParseText(
                "item=Program|Spotify|C:\\nope\\Spotify\\Spotify.exe||\n" +
                "item=Program|My games|C:\\nope\\Steam\\steam.exe||-silent\n" +
                "item=Program|Thing|C:\\nope\\thing.exe||\n" +
                "item=Program|Calculator|shell:AppsFolder\\Some.Other.Calc!App||\n" +
                "item=Program|Notepad|%SystemRoot%\\System32\\notepad.exe||\n", apps);
            List<RingItem> items = r.Config.Items;
            Check("an app found by its label is relinked", items[0].Target == @"C:\fake\Start Menu\Spotify.lnk");
            Check("an app found by its exe name is relinked, keeping its arguments",
                items[1].Target == @"C:\fake\Start Menu\Steam.lnk" && items[1].Args == "-silent");
            Check("an app that isn't installed keeps its button", items.Count == 5 && items[2].Target == @"C:\nope\thing.exe");
            Check("a Store app id that isn't installed is relinked by name", items[3].Target == apps[2].Path);
            Check("a path that exists is left alone", items[4].Target == @"%SystemRoot%\System32\notepad.exe");
            Check("only the missing app is listed", r.Missing.Count == 1 && r.Missing[0] == "Thing" && r.Relinked == 3,
                string.Join(",", r.Missing.ToArray()) + " relinked " + r.Relinked);
        }
    }
}

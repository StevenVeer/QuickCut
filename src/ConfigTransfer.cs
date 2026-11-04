using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QuickCut
{
    // Export and import of a ring (.quickcut files): the buttons and the look, so you can back them up,
    // move them to another PC or share them. The shortcut, the General toggles and "Start with Windows"
    // belong to this PC and are never exported; import leaves them as they are.
    //
    // The file is the config.ini format with only these keys: theme, hover, accent, buttoncolor, size,
    // center and item. Import reads only those, so even a renamed config.ini imports cleanly.
    static class ConfigTransfer
    {
        public const string Extension = ".quickcut";
        public const long MaxFileSize = 256 * 1024; // a full ring is a few KB; anything bigger isn't ours
        const int MaxLabel = 100, MaxText = 2048;

        static readonly string[] Themes = { "system", "light", "dark" };
        static readonly string[] Hovers = { "fill", "invert", "outline", "grow" };
        static readonly string[] Sizes = { "small", "medium", "large" };

        public class ImportResult
        {
            public Config Config;      // the buttons and look read from the file; null when Error is set
            public string Error;       // why the file can't be imported (already a sentence for the user)
            public readonly List<string> Missing = new List<string>(); // buttons whose app isn't on this PC
            public int Relinked;       // buttons whose app was found in another place on this PC
        }

        // ---------- export ----------

        public static void Export(Config cfg, string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# QuickCut buttons and look. To use them: QuickCut Settings > General > Import / export.");
            sb.AppendLine("theme=" + cfg.ThemeMode);
            sb.AppendLine("hover=" + cfg.HoverStyle);
            sb.AppendLine("accent=" + cfg.Accent);
            sb.AppendLine("buttoncolor=" + cfg.ButtonColor);
            sb.AppendLine("size=" + cfg.RingSize);
            sb.AppendLine("center=" + PortableItem(cfg.Center).Serialize());
            foreach (RingItem it in cfg.Items) sb.AppendLine("item=" + PortableItem(it).Serialize());
            Config.WriteAtomic(path, sb.ToString());
        }

        static RingItem PortableItem(RingItem it)
        {
            RingItem c = it.Clone();
            if (c.Type == ActionType.Program)
            {
                List<KeyValuePair<string, string>> folders = Folders();
                c.Target = Portable(c.Target, folders);
                c.Args = Portable(c.Args, folders);
            }
            return c;
        }

        // The folders that differ per PC and per user, longest first, so the most specific one wins
        // (%LOCALAPPDATA% before %USERPROFILE%).
        static List<KeyValuePair<string, string>> Folders()
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            foreach (string name in new[] { "LOCALAPPDATA", "APPDATA", "USERPROFILE", "ProgramFiles(x86)", "ProgramFiles", "SystemRoot" })
            {
                string dir = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(dir)) list.Add(new KeyValuePair<string, string>(name, dir));
            }
            return list;
        }

        // Writes folders as %VARIABLE%, e.g. C:\Users\you\AppData\Roaming\Spotify\Spotify.exe becomes
        // %APPDATA%\Spotify\Spotify.exe: it works for any user name on any PC (buttons expand variables
        // when they run) and doesn't give away yours. Only whole folder names are replaced, so
        // C:\Users\bobby is left alone when the profile is C:\Users\bob.
        internal static string Portable(string s, IList<KeyValuePair<string, string>> folders)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            List<KeyValuePair<string, string>> sorted = new List<KeyValuePair<string, string>>(folders);
            sorted.Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return b.Value.Length.CompareTo(a.Value.Length); });
            foreach (KeyValuePair<string, string> f in sorted)
            {
                string dir = f.Value.TrimEnd('\\');
                if (dir.Length < 3) continue; // never a bare drive like C:
                string token = "%" + f.Key + "%";
                int at = 0;
                while ((at = s.IndexOf(dir, at, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int end = at + dir.Length;
                    bool startOk = at == 0 || s[at - 1] == '"' || s[at - 1] == ' ' || s[at - 1] == '=';
                    bool endOk = end == s.Length || s[end] == '\\' || s[end] == '"';
                    if (startOk && endOk) { s = s.Substring(0, at) + token + s.Substring(end); at += token.Length; }
                    else at = end;
                }
            }
            return s;
        }

        // ---------- import ----------

        // Reads and checks a file. apps: the installed apps (AppCatalog.Cached()), used to find apps that
        // live somewhere else on this PC. Never throws for a bad file: the reason is in Error.
        public static ImportResult Import(string path, List<AppEntry> apps)
        {
            try
            {
                if (new FileInfo(path).Length > MaxFileSize) return Fail(Str.ImportTooBig);
                return Parse(File.ReadAllText(path, Encoding.UTF8), apps);
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                    return Fail(string.Format(Str.ImportFailed, ex.Message));
                throw;
            }
        }

        static ImportResult Fail(string error)
        {
            ImportResult r = new ImportResult();
            r.Error = error;
            return r;
        }

        internal static ImportResult Parse(string text, List<AppEntry> apps)
        {
            if (text.Length > MaxFileSize) return Fail(Str.ImportTooBig);
            // A binary file, or text without a single button: not a QuickCut file.
            if (text.IndexOf('\0') >= 0) return Fail(Str.ImportNotQuickCut);
            string[] lines = text.Split('\n');
            bool hasButtons = false;
            foreach (string raw in lines)
            {
                string l = raw.Trim().ToLowerInvariant();
                if (l.StartsWith("center=") || l.StartsWith("item=")) { hasButtons = true; break; }
            }
            if (!hasButtons) return Fail(Str.ImportNotQuickCut);

            Config c = Config.FromLines(lines); // unknown keys skipped, at most 12 buttons
            ImportResult r = new ImportResult();

            // Anything that isn't a known value falls back to the default, so a hand-edited or damaged
            // file can't leave the ring in a state the settings window can't show.
            if (Array.IndexOf(Themes, c.ThemeMode) < 0) c.ThemeMode = Themes[0];
            if (Array.IndexOf(Hovers, c.HoverStyle) < 0) c.HoverStyle = Hovers[0];
            if (Array.IndexOf(Sizes, c.RingSize) < 0) c.RingSize = "medium";
            c.Accent = c.Accent == "windows" ? "windows" : Hex(c.Accent);
            c.ButtonColor = Hex(c.ButtonColor);

            if (!Clean(c.Center)) c.Center = new Config().Center;
            c.Items.RemoveAll(delegate(RingItem it) { return !Clean(it); });
            Relink(c.Center, apps, r);
            foreach (RingItem it in c.Items) Relink(it, apps, r);

            r.Config = c;
            return r;
        }

        static string Hex(string s)
        {
            System.Drawing.Color? col = Theme.ParseHex(s);
            return col.HasValue ? Theme.ToHex(col.Value) : "";
        }

        // Trims a button to sane sizes. False when it can't work at all (a media action we don't know).
        static bool Clean(RingItem it)
        {
            if (it.Type == ActionType.Media && Array.IndexOf(Actions.MediaIds, it.Target) < 0) return false;
            it.Label = Cut(it.Label, MaxLabel);
            it.Target = Cut(it.Target, MaxText);
            it.Args = Cut(it.Args, MaxText);
            it.ColorHex = Hex(it.ColorHex);
            return true;
        }

        static string Cut(string s, int max) { s = s ?? ""; return s.Length > max ? s.Substring(0, max) : s; }

        // An app that isn't where the file says: look it up among the installed apps, first by the
        // button's label, then by the program's file name. Not installed: the button stays (pick the
        // app again to fix it) and is listed in Missing.
        static void Relink(RingItem it, List<AppEntry> apps, ImportResult r)
        {
            if (it.Type != ActionType.Program || it.Target.Trim().Length == 0 || Installed(it.Target, apps)) return;
            AppEntry match = AppCatalog.Find(apps, it.Label.Trim());
            if (match == null)
            {
                string exe = ExeName(it.Target);
                if (exe.Length > 0) match = AppCatalog.Find(apps, exe);
            }
            if (match != null) { it.Target = match.Path; r.Relinked++; }
            else r.Missing.Add(it.Label.Trim().Length > 0 ? it.Label.Trim() : it.Target);
        }

        static bool Installed(string target, List<AppEntry> apps)
        {
            if (AppCatalog.IsAppsFolder(target))
                return apps.Exists(delegate(AppEntry a) { return string.Equals(a.Path, target, StringComparison.OrdinalIgnoreCase); });
            try
            {
                string p = Actions.ExpandPath(target);
                return File.Exists(p) || Directory.Exists(p);
            }
            catch { return false; }
        }

        static string ExeName(string target)
        {
            if (AppCatalog.IsAppsFolder(target)) return "";
            try { return Path.GetFileNameWithoutExtension(Actions.ExpandPath(target)); }
            catch { return ""; }
        }

        // Puts the imported buttons and look into target; everything else in target stays.
        public static void ApplyTo(Config from, Config target)
        {
            target.ThemeMode = from.ThemeMode;
            target.HoverStyle = from.HoverStyle;
            target.Accent = from.Accent;
            target.ButtonColor = from.ButtonColor;
            target.RingSize = from.RingSize;
            target.Center = from.Center.Clone();
            target.Items.Clear();
            foreach (RingItem it in from.Items) target.Items.Add(it.Clone());
        }
    }
}

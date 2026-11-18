using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace QuickCut
{
    enum ActionType { Program, Url, Media, Keys }

    class RingItem
    {
        public ActionType Type;
        public string Label = "";
        public string Target = "";
        public string Args = "";
        public string ColorHex = ""; // own button color, "" = use the general button color
        // Own icon: "" = automatic (the app's or site's icon, the media or keyboard symbol), "letter" = the
        // label's first letter, an Icons.ButtonIcons name, or "file:<name>" for an image (IconStore).
        // Saved on its own "icon=" line after the button, so older versions just skip it.
        public string Icon = "";

        public RingItem() { }
        public RingItem(ActionType type, string label, string target) { Type = type; Label = label; Target = target; }

        public RingItem Clone()
        {
            RingItem c = new RingItem(Type, Label, Target);
            c.Args = Args;
            c.ColorHex = ColorHex;
            c.Icon = Icon;
            return c;
        }

        // The "icon=" line that follows the button's line, or null when it has no own icon.
        public string IconLine() { return Icon.Length == 0 ? null : "icon=" + Clean(Icon).Trim(); }

        // Format: Type|Label|Target|Color|Args  (Args may contain '|')
        public string Serialize()
        {
            return Type + "|" + Clean(Label).Replace('|', '/') + "|" + EscapeTarget(Clean(Target)) + "|" + Clean(ColorHex) + "|" + Clean(Args);
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " "); }

        // A '|' in the target (web addresses can have one) would split the line in the wrong place, so it's
        // saved as %7C, the URL encoding of '|'. Paths, key combos and media ids never contain either, and
        // a browser treats both spellings the same, so files written before this still read the same way.
        static string EscapeTarget(string s) { return s.Replace("|", "%7C"); }
        static string UnescapeTarget(string s) { return s.Replace("%7C", "|").Replace("%7c", "|"); }

        public static RingItem Parse(string s)
        {
            string[] p = s.Split(new[] { '|' }, 5);
            if (p.Length < 3) return null;
            ActionType t;
            if (!Enum.TryParse(p[0].Trim(), true, out t)) return null;
            RingItem it = new RingItem(t, p[1], UnescapeTarget(p[2]));
            if (p.Length == 4) it.Args = p[3]; // older format without a color field
            else if (p.Length == 5) { it.ColorHex = p[3]; it.Args = p[4]; }
            return it;
        }
    }

    class Config
    {
        public const string DefaultHotkey = "Ctrl+Space"; // one hand, same as other ring launchers
        public const int MaxItems = 12;

        public string Hotkey = DefaultHotkey;
        public bool MouseTrigger = true;    // double right-click opens the ring
        public bool SkipFullscreen = true;  // ...but not in fullscreen games/videos
        public bool LaunchNotice = true;    // small "QuickCut started" notice at launch
        public string ThemeMode = "system"; // system, light, dark
        public string HoverStyle = "fill";  // fill, invert, outline, grow
        public string Accent = "";          // hover color: "" = orange, "windows" = Windows accent, or #RRGGBB
        public string ButtonColor = "";     // color of all buttons, "" = theme default
        public string RingSize = "medium";  // small, medium (a saved "large" reads as medium)
        public bool VolumeBar = true;       // volume blocks under the ring
        public bool MediaBar = true;        // what's playing, under the volume (or in its place when that's off)
        public int VolumeStep = 5;          // percent per step (5 or 10): blocks, - / + and scrolling
        public string PanelPos = "below";   // where the volume and song panels sit: below, above, left, right
        public List<MixerApp> MixerApps = new List<MixerApp>(); // apps with their own volume row (Settings > Volume)

        public const int MaxMixerApps = 6;
        public static readonly string[] PanelPositions = { "below", "above", "left", "right" };

        public float SizeFactor { get { return RingSize == "small" ? 0.8f : 1f; } }
        public RingItem Center = new RingItem(ActionType.Media, Str.MediaShort[0], "playpause");
        public List<RingItem> Items = new List<RingItem>();

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickCut"); }
        }
        public static string FilePath { get { return Path.Combine(Folder, "config.ini"); } }

        // Default buttons: media controls plus the common apps found on this PC.
        public static Config CreateDefault()
        {
            Config c = new Config();
            List<AppEntry> apps = AppCatalog.Scan();
            Action<AppEntry> app = delegate(AppEntry a) { if (a != null) c.Items.Add(new RingItem(ActionType.Program, a.Name, a.Path)); };

            AppEntry spotify = AppCatalog.Spotify(apps);
            if (spotify != null) app(spotify);
            else if (Registry.ClassesRoot.OpenSubKey("spotify") != null) c.Items.Add(new RingItem(ActionType.Url, "Spotify", "spotify:"));
            c.Items.Add(Media("next"));
            app(AppCatalog.DefaultBrowser());
            app(AppCatalog.Explorer());
            app(AppCatalog.Find(apps, "Discord"));
            app(AppCatalog.Find(apps, "Steam"));
            c.Items.Add(Media("prev"));
            return c;
        }

        static RingItem Media(string id) { return new RingItem(ActionType.Media, Actions.MediaShortName(id), id); }

        public static Config Load(out bool firstRun)
        {
            firstRun = !File.Exists(FilePath);
            if (firstRun)
            {
                Config d = CreateDefault();
                try { d.Save(); } catch { }
                return d;
            }

            try { return FromLines(File.ReadAllLines(FilePath, Encoding.UTF8)); }
            catch { return CreateDefault(); }
        }

        // Reads settings in the config.ini format. Unknown keys are skipped, missing ones keep their
        // defaults, and at most MaxItems buttons are read. Also used to import a .quickcut file.
        public static Config FromLines(IEnumerable<string> lines)
        {
            Config c = new Config();
            RingItem last = null; // the button an "icon=" line belongs to
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (key == "hotkey") c.Hotkey = val;
                else if (key == "mouse") c.MouseTrigger = val != "0";
                else if (key == "skipfullscreen") c.SkipFullscreen = val != "0";
                else if (key == "launchnotice") c.LaunchNotice = val != "0";
                else if (key == "theme") c.ThemeMode = val;
                else if (key == "hover") c.HoverStyle = val;
                else if (key == "accent") c.Accent = val;
                else if (key == "buttoncolor") c.ButtonColor = val;
                else if (key == "size") c.RingSize = val == "small" ? "small" : "medium"; // "large" is gone: medium
                else if (key == "volumebar") c.VolumeBar = val != "0";
                else if (key == "mediabar") c.MediaBar = val != "0";
                else if (key == "volumestep") c.VolumeStep = val == "10" ? 10 : 5;
                else if (key == "volumepos")
                {
                    string pos = val.ToLowerInvariant();
                    c.PanelPos = Array.IndexOf(PanelPositions, pos) >= 0 ? pos : "below";
                }
                else if (key == "mixerapp" && c.MixerApps.Count < MaxMixerApps)
                {
                    MixerApp m = MixerApp.Parse(val);
                    if (m != null) c.MixerApps.Add(m);
                }
                else if (key == "center") { RingItem it = RingItem.Parse(val); if (it != null) c.Center = last = it; }
                else if (key == "item" && c.Items.Count < MaxItems)
                {
                    RingItem it = RingItem.Parse(val);
                    if (it != null) c.Items.Add(it);
                    last = it; // a skipped button's icon line is skipped too
                }
                else if (key == "item") last = null;
                else if (key == "icon" && last != null) last.Icon = val;
            }
            return c;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# QuickCut settings. Button format: Type|Label|Target|Color|Args");
            sb.AppendLine("# Type: Program, Url, Media (playpause/next/prev/stop/volup/voldown/mute), Keys (e.g. Ctrl+Shift+Esc)");
            sb.AppendLine("# Mixer app format: Name|exe (or pkg:<Store package family>)|Icon");
            sb.AppendLine("hotkey=" + Hotkey);
            sb.AppendLine("mouse=" + (MouseTrigger ? "1" : "0"));
            sb.AppendLine("skipfullscreen=" + (SkipFullscreen ? "1" : "0"));
            sb.AppendLine("launchnotice=" + (LaunchNotice ? "1" : "0"));
            sb.AppendLine("theme=" + ThemeMode);
            sb.AppendLine("hover=" + HoverStyle);
            sb.AppendLine("accent=" + Accent);
            sb.AppendLine("buttoncolor=" + ButtonColor);
            sb.AppendLine("size=" + RingSize);
            sb.AppendLine("volumebar=" + (VolumeBar ? "1" : "0"));
            sb.AppendLine("mediabar=" + (MediaBar ? "1" : "0"));
            sb.AppendLine("volumestep=" + VolumeStep);
            sb.AppendLine("volumepos=" + PanelPos);
            foreach (MixerApp m in MixerApps) sb.AppendLine("mixerapp=" + m.Serialize());
            AppendItem(sb, "center=", Center);
            foreach (RingItem it in Items) AppendItem(sb, "item=", it);
            WriteAtomic(FilePath, sb.ToString());
        }

        // A button's line, plus its "icon=" line when it has its own icon.
        internal static void AppendItem(StringBuilder sb, string key, RingItem it)
        {
            sb.AppendLine(key + it.Serialize());
            string icon = it.IconLine();
            if (icon != null) sb.AppendLine(icon);
        }

        // Writes the whole file or nothing: the text goes to a temp file next to it, which then takes the
        // old file's place in one step. Writing in place would leave an empty or half file (and lose every
        // button) if the PC crashed or lost power mid-write.
        public static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); }
            catch (IOException)
            {
                // Some drives (network shares, FAT) can't swap files: fall back to a plain overwrite.
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }

        public Config Clone()
        {
            Config c = new Config();
            c.Hotkey = Hotkey;
            c.MouseTrigger = MouseTrigger;
            c.SkipFullscreen = SkipFullscreen;
            c.LaunchNotice = LaunchNotice;
            c.ThemeMode = ThemeMode;
            c.HoverStyle = HoverStyle;
            c.Accent = Accent;
            c.ButtonColor = ButtonColor;
            c.RingSize = RingSize;
            c.VolumeBar = VolumeBar;
            c.MediaBar = MediaBar;
            c.VolumeStep = VolumeStep;
            c.PanelPos = PanelPos;
            foreach (MixerApp m in MixerApps) c.MixerApps.Add(m.Clone());
            c.Center = Center.Clone();
            foreach (RingItem it in Items) c.Items.Add(it.Clone());
            return c;
        }
    }
}

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

        public RingItem() { }
        public RingItem(ActionType type, string label, string target) { Type = type; Label = label; Target = target; }

        public RingItem Clone()
        {
            RingItem c = new RingItem(Type, Label, Target);
            c.Args = Args;
            c.ColorHex = ColorHex;
            return c;
        }

        // Format: Type|Label|Target|Color|Args  (Args may contain '|')
        public string Serialize()
        {
            return Type + "|" + Clean(Label).Replace('|', '/') + "|" + Clean(Target) + "|" + Clean(ColorHex) + "|" + Clean(Args);
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " "); }

        public static RingItem Parse(string s)
        {
            string[] p = s.Split(new[] { '|' }, 5);
            if (p.Length < 3) return null;
            ActionType t;
            if (!Enum.TryParse(p[0].Trim(), true, out t)) return null;
            RingItem it = new RingItem(t, p[1], p[2]);
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
        public string RingSize = "medium";  // small, medium, large
        public bool VolumeBar = true;       // volume blocks under the ring
        public int VolumeStep = 5;          // percent per step (5 or 10): blocks, - / + and scrolling

        public float SizeFactor { get { return RingSize == "small" ? 0.8f : RingSize == "large" ? 1.25f : 1f; } }
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

            Config c = new Config();
            try
            {
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
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
                    else if (key == "size") c.RingSize = val;
                    else if (key == "volumebar") c.VolumeBar = val != "0";
                    else if (key == "volumestep") c.VolumeStep = val == "10" ? 10 : 5;
                    else if (key == "center") { RingItem it = RingItem.Parse(val); if (it != null) c.Center = it; }
                    else if (key == "item" && c.Items.Count < MaxItems) { RingItem it = RingItem.Parse(val); if (it != null) c.Items.Add(it); }
                }
            }
            catch { return CreateDefault(); }
            return c;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# QuickCut settings. Button format: Type|Label|Target|Color|Args");
            sb.AppendLine("# Type: Program, Url, Media (playpause/next/prev/stop/volup/voldown/mute), Keys (e.g. Ctrl+Shift+Esc)");
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
            sb.AppendLine("volumestep=" + VolumeStep);
            sb.AppendLine("center=" + Center.Serialize());
            foreach (RingItem it in Items) sb.AppendLine("item=" + it.Serialize());
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
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
            c.VolumeStep = VolumeStep;
            c.Center = Center.Clone();
            foreach (RingItem it in Items) c.Items.Add(it.Clone());
            return c;
        }
    }
}

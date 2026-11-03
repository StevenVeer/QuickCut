using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QuickCut
{
    enum ActionType { Program, Url, Media, Keys }

    class RingItem
    {
        public ActionType Type;
        public string Label = "";
        public string Target = "";
        public string Args = "";

        public RingItem() { }
        public RingItem(ActionType type, string label, string target) { Type = type; Label = label; Target = target; }

        public RingItem Clone()
        {
            RingItem c = new RingItem(Type, Label, Target);
            c.Args = Args;
            return c;
        }

        // Format: Type|Label|Target|Args  (Args may contain '|')
        public string Serialize()
        {
            return Type + "|" + Clean(Label).Replace('|', '/') + "|" + Clean(Target) + "|" + Clean(Args);
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " "); }

        public static RingItem Parse(string s)
        {
            string[] p = s.Split(new[] { '|' }, 4);
            if (p.Length < 3) return null;
            ActionType t;
            if (!Enum.TryParse(p[0].Trim(), true, out t)) return null;
            RingItem it = new RingItem(t, p[1], p[2]);
            if (p.Length > 3) it.Args = p[3];
            return it;
        }
    }

    class Config
    {
        public const string DefaultHotkey = "Ctrl+Alt+Space";
        public const int MaxItems = 12;

        public string Hotkey = DefaultHotkey;
        public RingItem Center = new RingItem(ActionType.Media, "Play/Pauze", "playpause");
        public List<RingItem> Items = new List<RingItem>();

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickCut"); }
        }
        public static string FilePath { get { return Path.Combine(Folder, "config.ini"); } }

        public static Config CreateDefault()
        {
            Config c = new Config();
            string spotify = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
            c.Items.Add(File.Exists(spotify)
                ? new RingItem(ActionType.Program, "Spotify", spotify)
                : new RingItem(ActionType.Url, "Spotify", "spotify:"));
            c.Items.Add(new RingItem(ActionType.Media, "Volgende", "next"));
            c.Items.Add(new RingItem(ActionType.Media, "Volume +", "volup"));
            c.Items.Add(new RingItem(ActionType.Program, "Verkenner",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")));
            c.Items.Add(new RingItem(ActionType.Media, "Volume -", "voldown"));
            c.Items.Add(new RingItem(ActionType.Media, "Vorige", "prev"));
            return c;
        }

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
            sb.AppendLine("# QuickCut instellingen. Formaat knoppen: Soort|Naam|Doel|Argumenten");
            sb.AppendLine("# Soort: Program, Url, Media (playpause/next/prev/stop/volup/voldown/mute), Keys (bijv. Ctrl+Shift+Esc)");
            sb.AppendLine("hotkey=" + Hotkey);
            sb.AppendLine("center=" + Center.Serialize());
            foreach (RingItem it in Items) sb.AppendLine("item=" + it.Serialize());
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }

        public Config Clone()
        {
            Config c = new Config();
            c.Hotkey = Hotkey;
            c.Center = Center.Clone();
            foreach (RingItem it in Items) c.Items.Add(it.Clone());
            return c;
        }
    }
}

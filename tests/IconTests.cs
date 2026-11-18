using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using QuickCut;

namespace QuickCutTests
{
    // A button's own icon: how it's saved, and the line icons it can use. Pure code only.
    static class IconTests
    {
        static void Check(string name, bool ok, string detail = "") { Runner.Check(name, ok, detail); }

        public static void Run()
        {
            Runner.Group("Button icons");
            Config c = Config.FromLines(new[] {
                "center=Media|Play/Pause|playpause||",
                "icon=star",
                "item=Keys|Task Manager|Ctrl+Shift+Esc||",
                "icon=activity",
                "item=Url|YouTube|youtube.com||",
                "item=Keys|Snip|Win+Shift+S||",
                "icon=file:img-0123456789ab.png" });
            Check("center icon is read", c.Center.Icon == "star");
            Check("an item's icon is read", c.Items[0].Icon == "activity");
            Check("no icon line = automatic", c.Items[1].Icon == "");
            Check("an image icon is read", c.Items[2].Icon == "file:img-0123456789ab.png");
            Check("an icon line before any button is ignored", Config.FromLines(new[] { "icon=star", "item=Keys|A|Ctrl+A||" }).Items[0].Icon == "");

            string saved = string.Join("\n", SaveLines(c));
            Config back = Config.FromLines(saved.Split('\n'));
            Check("icons survive saving", back.Center.Icon == "star" && back.Items[0].Icon == "activity" && back.Items[1].Icon == "" && back.Items[2].Icon.StartsWith("file:"));
            Check("no icon line for automatic icons", saved.Split('\n').Count(l => l.StartsWith("icon=")) == 3);
            Check("Clone copies the icon", c.Items[0].Clone().Icon == "activity");

            // A 13th button (over the limit) is skipped, and so is its icon: it must not land on another button.
            string[] many = Enumerable.Range(0, 13).SelectMany(i => new[] { "item=Keys|K" + i + "|Ctrl+A||", "icon=star" }).ToArray();
            many[25] = "icon=lock"; // the 13th button's icon
            Config full = Config.FromLines(many);
            Check("icon of a skipped button is skipped", full.Items.Count == Config.MaxItems && full.Items.All(it => it.Icon == "star"));

            Runner.Group("Line icons");
            bool all = true;
            string bad = "";
            foreach (string n in Icons.ButtonIcons.Concat(new[] { "nav-buttons", "nav-look", "nav-volume", "nav-general", "eye", "globe", "check", "trash" }))
                if (!Draws(n)) { all = false; bad += n + " "; }
            Check("every icon draws something", all, bad);
            Check("an unknown icon is null", Icons.Get("nope") == null);
            Check("a broken path doesn't throw", new LineIcon("M2 2L5") != null && new LineIcon("Q1 2 3 4") != null);
        }

        static string[] SaveLines(Config c)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Config.AppendItem(sb, "center=", c.Center);
            foreach (RingItem it in c.Items) Config.AppendItem(sb, "item=", it);
            return sb.ToString().Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Draws the icon into a small bitmap and checks some pixels got ink.
        static bool Draws(string name)
        {
            LineIcon ic = Icons.Get(name);
            if (ic == null) return false;
            using (Bitmap b = new Bitmap(48, 48, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b)) ic.Draw(g, new RectangleF(0, 0, 48, 48), Color.Black);
                int ink = 0;
                for (int y = 0; y < 48; y += 2) for (int x = 0; x < 48; x += 2) if (b.GetPixel(x, y).A > 100) ink++;
                return ink > 10;
            }
        }
    }
}

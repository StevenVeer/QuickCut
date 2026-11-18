using System;
using System.Drawing;
using System.Linq;
using QuickCut;

namespace QuickCutTests
{
    // Per-app volume (the mixer) and where the panels go around the ring. Pure code only: no audio
    // sessions are read or changed.
    static class MixerTests
    {
        static void Check(string name, bool ok, string detail = "") { Runner.Check(name, ok, detail); }

        public static void Run()
        {
            Runner.Group("Mixer apps");
            MixerApp m = MixerApp.Parse("Spotify|Spotify.exe|C:\\Users\\x\\AppData\\Roaming\\Spotify\\Spotify.exe");
            Check("parses name, exe and icon", m != null && m.Name == "Spotify" && m.Match == "Spotify.exe" && m.Icon.EndsWith("Spotify.exe"));
            MixerApp back = MixerApp.Parse(m.Serialize());
            Check("round trips", back.Name == m.Name && back.Match == m.Match && back.Icon == m.Icon);
            Check("a '|' in the name doesn't break the line", MixerApp.Parse(new MixerApp("A|B", "a.exe", "").Serialize()).Match == "a.exe");
            Check("no exe = skipped", MixerApp.Parse("Name only") == null && MixerApp.Parse("Name||icon") == null);
            Check("icon is optional", MixerApp.Parse("Game|game.exe").Icon == "");

            Check("matches the exe, any case", MixerApp.Matches("discord.exe", @"C:\Users\x\AppData\Local\Discord\app-1.0\Discord.exe"));
            Check("another exe doesn't match", !MixerApp.Matches("discord.exe", @"C:\Program Files\Google\Chrome\chrome.exe"));
            Check("part of a name doesn't match", !MixerApp.Matches("cord.exe", @"C:\x\Discord.exe"));
            string store = @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.250.0.0_x64__zpdnekdrzrea0\Spotify.exe";
            Check("Store package matches its process", MixerApp.Matches("pkg:SpotifyAB.SpotifyMusic_zpdnekdrzrea0", store));
            Check("Store package of another publisher doesn't", !MixerApp.Matches("pkg:SpotifyAB.SpotifyMusic_other", store));
            Check("Store package doesn't match a desktop app", !MixerApp.Matches("pkg:SpotifyAB.SpotifyMusic_zpdnekdrzrea0", @"C:\x\Spotify.exe"));
            Check("Store app id gives its package", MixerApp.MatchFor(@"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") == "pkg:SpotifyAB.SpotifyMusic_zpdnekdrzrea0");
            Check("a web link gives nothing", MixerApp.MatchFor("steam://rungameid/123") == null);

            Runner.Group("Mixer settings");
            Config c = Config.FromLines(new[] { "volumepos=Left", "mixerapp=Spotify|Spotify.exe|", "mixerapp=Discord|Discord.exe|" });
            Check("position is read", c.PanelPos == "left");
            Check("apps are read in order", c.MixerApps.Count == 2 && c.MixerApps[1].Name == "Discord");
            Check("unknown position = below", Config.FromLines(new[] { "volumepos=diagonal" }).PanelPos == "below");
            Check("default position = below", new Config().PanelPos == "below");
            Check("a saved large ring opens as medium", Config.FromLines(new[] { "size=large" }).RingSize == "medium"
                && Config.FromLines(new[] { "size=small" }).RingSize == "small" && new Config().SizeFactor == 1f);
            string[] many = Enumerable.Range(0, 10).Select(i => "mixerapp=App" + i + "|a" + i + ".exe|").ToArray();
            Check("at most " + Config.MaxMixerApps + " apps", Config.FromLines(many).MixerApps.Count == Config.MaxMixerApps);
            Config clone = c.Clone();
            clone.MixerApps[0].Name = "Changed";
            Check("Clone copies the apps", clone.PanelPos == "left" && c.MixerApps[0].Name == "Spotify");

            Runner.Group("Panel positions");
            foreach (string pos in Config.PanelPositions)
                using (RingRenderer r = new RingRenderer(1f, 1f, true, true, 0, 0, pos, 2))
                {
                    int row;
                    // The first app row is right next to the volume panel, on the side away from the ring.
                    bool ok = r.Contains(new PointF(r.Cx, r.Cy)) && !r.Contains(new PointF(-5, -5));
                    Check(pos + ": ring center is on the ring", ok && r.HitTest(new PointF(r.Cx, r.Cy), 8, false) == RingRenderer.HitCenter);
                    Check(pos + ": ring fits in the image", r.Cx - 160 >= 0 && r.Cy - 160 >= 0 && r.Cx + 160 <= r.Width + 0.5f && r.Cy + 160 <= r.Height + 0.5f);
                    PointF rowPoint = FindRow(r, out row);
                    Check(pos + ": app rows can be hit", row == 0 || row == 1, "no row found"); // above: row 2 is the highest
                    Check(pos + ": rows don't overlap the ring", Dist(rowPoint, r.Cx, r.Cy) > 148);
                }
            using (RingRenderer below = new RingRenderer(1f, 1f, true, true, 0, 0, "below", 2))
            using (RingRenderer right = new RingRenderer(1f, 1f, true, true, 0, 0, "right", 2))
            using (RingRenderer left = new RingRenderer(1f, 1f, true, true, 0, 0, "left", 2))
            using (RingRenderer above = new RingRenderer(1f, 1f, true, true, 0, 0, "above", 2))
            {
                Check("below: as wide as the ring", below.Width == below.Size);
                Check("below: the ring is at the top", below.Cy == below.Size / 2f);
                Check("above: the ring is at the bottom", above.Cy == above.Height - above.Size / 2f);
                Check("left / right: wider than the ring", left.Width > left.Size && right.Width == left.Width);
                Check("right: the ring is on the left", right.Cx == right.Size / 2f);
                Check("left: the ring is on the right", left.Cx == left.Width - left.Size / 2f);
                Check("2 rows: volume on the ring side of the rows (below)",
                    below.HitVolume(new PointF(below.Cx - 110, below.Size + 27), 5) == RingRenderer.VolDown);
            }
            using (RingRenderer r = new RingRenderer(1f, 1f, false, true, 0, 0, "below", 3))
                Check("no app rows without the volume panel", r.AppRows == 0);
            using (RingRenderer r = new RingRenderer(1f, 1f, true, true, 0, 0, "below", 1))
            {
                int row;
                // Row 0 starts 56 below the volume panel's top (322): 378..414. Icon on the left, blocks from x 146.
                Check("row icon = mute", r.HitApp(new PointF(26 + 20, 378 + 18), 5, out row) == RingRenderer.AppMute && row == 0);
                Check("first row block", r.HitApp(new PointF(26 + 121, 378 + 18), 5, out row) == 0);
                Check("last row block (10% steps)", r.HitApp(new PointF(26 + 218, 378 + 18), 10, out row) == 9);
                Check("off the rows = nothing", r.HitApp(new PointF(26 + 121, 300), 5, out row) == RingRenderer.VolNone && row == -1);
            }
        }

        // Scans the image for the first app row.
        static PointF FindRow(RingRenderer r, out int row)
        {
            row = -1;
            for (int y = 0; y < r.Height; y += 4)
                for (int x = 0; x < r.Width; x += 4)
                {
                    PointF p = new PointF(x, y);
                    if (r.HitApp(p, 5, out row) != RingRenderer.VolNone) return p;
                }
            return PointF.Empty;
        }

        static float Dist(PointF p, float x, float y) { float dx = p.X - x, dy = p.Y - y; return (float)Math.Sqrt(dx * dx + dy * dy); }
    }
}

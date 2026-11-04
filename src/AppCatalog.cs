using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace QuickCut
{
    class AppEntry
    {
        public string Name, Path;
        public AppEntry(string name, string path) { Name = name; Path = path; }
    }

    // Finds installed apps via Start menu and desktop shortcuts, plus Store apps.
    static class AppCatalog
    {
        static readonly string[] Skip = { "uninstall", "readme", "read me", "help", "documentation", "license", "release notes", "website", "manual" };

        // Scanning takes about a second, so the result is kept and refreshed in the background.
        static List<AppEntry> cached;
        static readonly object cacheLock = new object();

        public static List<AppEntry> Cached()
        {
            lock (cacheLock)
            {
                if (cached == null) cached = Scan();
                return cached;
            }
        }

        // Rescans on a background thread (the shell needs an STA thread), so newly installed
        // apps show up and the picker opens instantly.
        public static void RefreshInBackground()
        {
            Thread t = new Thread(delegate()
            {
                List<AppEntry> fresh = Scan();
                lock (cacheLock) cached = fresh;
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public static List<AppEntry> Scan()
        {
            Dictionary<string, AppEntry> byName = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
            Environment.SpecialFolder[] roots = {
                Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory };
            foreach (Environment.SpecialFolder f in roots)
            {
                string dir = Environment.GetFolderPath(f);
                if (Directory.Exists(dir)) Walk(dir, byName, 0);
            }
            AddAppsFolder(byName);
            List<AppEntry> list = new List<AppEntry>(byName.Values);
            lock (names) foreach (AppEntry a in list) names[a.Path] = a.Name;
            list.Sort(delegate(AppEntry a, AppEntry b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return list;
        }

        static void Walk(string dir, Dictionary<string, AppEntry> byName, int depth)
        {
            try
            {
                foreach (string file in Directory.GetFiles(dir))
                {
                    string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".lnk" && ext != ".url") continue;
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    string lower = name.ToLowerInvariant();
                    bool skip = false;
                    foreach (string s in Skip) if (lower.Contains(s)) { skip = true; break; }
                    if (!skip && !byName.ContainsKey(name)) byName[name] = new AppEntry(name, file);
                }
                if (depth < 4)
                    foreach (string sub in Directory.GetDirectories(dir)) Walk(sub, byName, depth + 1);
            }
            catch { } // folders we may not read
        }

        // Store apps (Spotify from the Microsoft Store, Calculator, ...) have no shortcut file.
        // They live in the shell's "All apps" folder and start via shell:AppsFolder\<id>.
        public const string AppsFolderPrefix = @"shell:AppsFolder\";
        static readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static void AddAppsFolder(Dictionary<string, AppEntry> byName)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) return;
                object shell = Activator.CreateInstance(t);
                object folder = Call(shell, "NameSpace", "shell:AppsFolder");
                if (folder == null) return;
                object items = Call(folder, "Items");
                int count = (int)Get(items, "Count");
                for (int i = 0; i < count; i++)
                {
                    object item = Call(items, "Item", i);
                    string name = Get(item, "Name") as string;
                    string id = Get(item, "Path") as string;
                    Marshal.ReleaseComObject(item);
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id) || byName.ContainsKey(name)) continue;
                    string lower = name.ToLowerInvariant();
                    bool skip = false;
                    foreach (string s in Skip) if (lower.Contains(s)) { skip = true; break; }
                    if (!skip) byName[name] = new AppEntry(name, AppsFolderPrefix + id);
                }
                Marshal.ReleaseComObject(items);
                Marshal.ReleaseComObject(folder);
                Marshal.ReleaseComObject(shell);
            }
            catch { } // no shell available: shortcuts alone still work
        }

        static object Call(object o, string method, params object[] args)
        {
            return o.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, o, args);
        }

        static object Get(object o, string prop)
        {
            return o.GetType().InvokeMember(prop, BindingFlags.GetProperty, null, o, null);
        }

        public static bool IsAppsFolder(string target)
        {
            return target != null && target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase);
        }

        // Friendly name for a target found by an earlier scan, or null.
        public static string NameFor(string target)
        {
            string n;
            lock (names) return names.TryGetValue(target ?? "", out n) ? n : null;
        }

        public static AppEntry Find(List<AppEntry> apps, string name)
        {
            foreach (AppEntry a in apps) if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        // Common apps that exist on this PC, in a sensible order.
        public static List<AppEntry> Suggested(List<AppEntry> apps)
        {
            List<AppEntry> r = new List<AppEntry>();
            Action<AppEntry> add = delegate(AppEntry a) { if (a != null && r.FindIndex(x => x.Path == a.Path) < 0) r.Add(a); };
            add(Spotify(apps));
            add(DefaultBrowser());
            add(Find(apps, "Discord"));
            add(Find(apps, "Steam"));
            add(Explorer());
            return r;
        }

        public static AppEntry Spotify(List<AppEntry> apps)
        {
            AppEntry a = Find(apps, "Spotify");
            if (a != null) return a;
            string exe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
            return File.Exists(exe) ? new AppEntry("Spotify", exe) : null;
        }

        public static AppEntry Explorer()
        {
            return new AppEntry(Str.DefaultExplorer,
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        }

        public static AppEntry DefaultBrowser()
        {
            try
            {
                string progId = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId", null) as string;
                if (string.IsNullOrEmpty(progId)) return null;
                string exe = ExeFromCommand(Registry.GetValue(@"HKEY_CLASSES_ROOT\" + progId + @"\shell\open\command", "", null) as string);
                if (exe == null || !File.Exists(exe)) return null;
                string name = FileVersionInfo.GetVersionInfo(exe).FileDescription;
                if (string.IsNullOrEmpty(name)) name = System.IO.Path.GetFileNameWithoutExtension(exe);
                return new AppEntry(ShortBrowserName(name.Trim()), exe);
            }
            catch { return null; }
        }

        // "Brave Browser" -> "Brave", "Google Chrome" -> "Chrome": fits a ring label.
        static string ShortBrowserName(string name)
        {
            foreach (string prefix in new[] { "Google ", "Microsoft ", "Mozilla " })
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(prefix.Length);
            if (name.EndsWith(" Browser", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 8);
            return name.Trim();
        }

        // "C:\x\app.exe" --flag "%1"  ->  C:\x\app.exe
        public static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd.Substring(1, e - 1) : null; }
            // Unquoted, the path may still have spaces (C:\Program Files\app.exe %1): it ends at ".exe".
            int x = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (x > 0 && (x + 4 == cmd.Length || char.IsWhiteSpace(cmd[x + 4]))) return cmd.Substring(0, x + 4);
            int sp = cmd.IndexOf(' ');
            return sp > 0 ? cmd.Substring(0, sp) : cmd;
        }
    }
}

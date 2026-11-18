using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickCut
{
    // An app in the mixer list (Settings > Volume). It gets its own volume row under the ring while
    // it's running.
    class MixerApp
    {
        public string Name = "";  // shown in the row
        public string Match = ""; // the exe ("spotify.exe"), or "pkg:<package family>" for a Store app
        public string Icon = "";  // where the row's icon comes from: an exe, a shortcut or a shell:AppsFolder\ id

        public MixerApp() { }
        public MixerApp(string name, string match, string icon) { Name = name; Match = match; Icon = icon; }

        public MixerApp Clone() { return new MixerApp(Name, Match, Icon); }

        // Format: Name|Match|Icon
        public string Serialize()
        {
            return Clean(Name).Replace('|', '/') + "|" + Clean(Match).Replace('|', '/') + "|" + Clean(Icon);
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

        public static MixerApp Parse(string s)
        {
            string[] p = (s ?? "").Split(new[] { '|' }, 3);
            if (p.Length < 2 || p[1].Trim().Length == 0) return null;
            return new MixerApp(p[0].Trim(), p[1].Trim(), p.Length > 2 ? p[2].Trim() : "");
        }

        // True when a process started from this path belongs to the app.
        public bool MatchesProcess(string processPath) { return Matches(Match, processPath); }

        public static bool Matches(string match, string processPath)
        {
            if (string.IsNullOrEmpty(match) || string.IsNullOrEmpty(processPath)) return false;
            if (match.StartsWith("pkg:", StringComparison.OrdinalIgnoreCase))
            {
                // Store apps run from ...\WindowsApps\<Name>_<Version>_<Arch>_<ResourceId>_<PublisherId>\...
                string family = match.Substring(4);
                int us = family.LastIndexOf('_');
                if (us <= 0) return false;
                string name = family.Substring(0, us), publisher = family.Substring(us + 1);
                const string root = @"\WindowsApps\";
                int w = processPath.IndexOf(root, StringComparison.OrdinalIgnoreCase);
                if (w < 0) return false;
                int start = w + root.Length, end = processPath.IndexOf('\\', start);
                if (end < 0) return false;
                string full = processPath.Substring(start, end - start);
                return full.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
                    && full.EndsWith("_" + publisher, StringComparison.OrdinalIgnoreCase);
            }
            string file;
            try { file = Path.GetFileName(processPath); }
            catch { return false; }
            return string.Equals(file, match, StringComparison.OrdinalIgnoreCase);
        }

        // What to match for an app picked from the installed apps: the exe a shortcut starts, or the
        // package of a Store app. Null when it can't be told (a web link, a Steam game shortcut, ...).
        public static string MatchFor(string target)
        {
            try
            {
                string t = Actions.ExpandPath(target);
                if (AppCatalog.IsAppsFolder(t))
                {
                    string id = t.Substring(AppCatalog.AppsFolderPrefix.Length);
                    int bang = id.IndexOf('!');
                    if (bang > 0) return "pkg:" + id.Substring(0, bang);
                    return id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(id) : null;
                }
                string exe = WindowSwitcher.ExeFor(t);
                return exe != null ? Path.GetFileName(exe) : null;
            }
            catch { return null; }
        }
    }

    // Per-app volume: the audio sessions Windows keeps for every app that plays sound (the same ones
    // the Windows Volume Mixer shows), on every active output device.
    static class AppVolume
    {
        // One audio session of one process.
        public class Session
        {
            public uint Pid;
            public string Path; // the process's exe
            public bool Active; // playing right now (not just open)
            internal ISimpleAudioVolume Vol;
        }

        // The sessions of one app from the mixer list.
        public class AppSessions
        {
            public MixerApp App;
            // Empty while the app runs but hasn't made a sound yet: Windows only creates a session
            // when an app first plays something (Spotify before you press play, for one).
            public readonly List<Session> Sessions = new List<Session>();
            public string ProcessPath; // one of the app's processes, for its icon
            public bool HasSound { get { return Sessions.Count > 0; } }

            // The level shown: the session that's playing, else the first. level: 0..1.
            public bool TryGet(out float level, out bool muted)
            {
                level = 0; muted = false;
                Session s = Main();
                if (s == null) return false;
                try
                {
                    s.Vol.GetMasterVolume(out level);
                    s.Vol.GetMute(out muted);
                    return true;
                }
                catch { return false; } // the app just closed
            }

            Session Main()
            {
                foreach (Session s in Sessions) if (s.Active) return s;
                return Sessions.Count > 0 ? Sessions[0] : null;
            }

            // Sets the level on all of the app's sessions and unmutes them.
            public void Set(float level)
            {
                Guid ctx = Guid.Empty;
                level = Math.Max(0f, Math.Min(1f, level));
                foreach (Session s in Sessions)
                    try { s.Vol.SetMasterVolume(level, ref ctx); s.Vol.SetMute(false, ref ctx); }
                    catch { }
            }

            public void SetMute(bool mute)
            {
                Guid ctx = Guid.Empty;
                foreach (Session s in Sessions)
                    try { s.Vol.SetMute(mute, ref ctx); }
                    catch { }
            }
        }

        static IMMDeviceEnumerator enumerator;
        static readonly Dictionary<uint, string> pathCache = new Dictionary<uint, string>();

        // Every app session that isn't closed, except Windows' own system sounds and QuickCut.
        public static List<Session> Sessions()
        {
            List<Session> list = new List<Session>();
            try
            {
                if (enumerator == null) enumerator = (IMMDeviceEnumerator)CreateEnumerator();
                IMMDeviceCollection devices;
                if (enumerator.EnumAudioEndpoints(0 /*eRender*/, 1 /*DEVICE_STATE_ACTIVE*/, out devices) != 0 || devices == null) return list;
                uint count;
                devices.GetCount(out count);
                uint me = (uint)Process.GetCurrentProcess().Id;
                for (uint d = 0; d < count; d++)
                {
                    IMMDevice dev;
                    if (devices.Item(d, out dev) != 0 || dev == null) continue;
                    AddSessions(dev, me, list);
                }
            }
            catch { } // no Core Audio: no rows
            if (pathCache.Count > 512) pathCache.Clear();
            return list;
        }

        static void AddSessions(IMMDevice dev, uint me, List<Session> list)
        {
            try
            {
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out o) != 0) return;
                IAudioSessionManager2 mgr = o as IAudioSessionManager2;
                if (mgr == null) return;
                IAudioSessionEnumerator sessions;
                if (mgr.GetSessionEnumerator(out sessions) != 0 || sessions == null) return;
                int n;
                sessions.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        IAudioSessionControl ctl;
                        if (sessions.GetSession(i, out ctl) != 0 || ctl == null) continue;
                        IAudioSessionControl2 ctl2 = ctl as IAudioSessionControl2;
                        ISimpleAudioVolume vol = ctl as ISimpleAudioVolume;
                        if (ctl2 == null || vol == null) continue;
                        if (ctl2.IsSystemSoundsSession() == 0) continue; // S_OK means it is
                        int state;
                        ctl.GetState(out state);
                        if (state == 2 /*expired*/) continue;
                        uint pid;
                        if (ctl2.GetProcessId(out pid) != 0 || pid == 0 || pid == me) continue;
                        string path = PathOf(pid);
                        if (path == null) continue;
                        Session s = new Session();
                        s.Pid = pid; s.Path = path; s.Active = state == 1; s.Vol = vol;
                        list.Add(s);
                    }
                    catch { }
                }
            }
            catch { }
        }

        static string PathOf(uint pid)
        {
            string p;
            if (pathCache.TryGetValue(pid, out p)) return p;
            p = null;
            IntPtr hp = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
            if (hp != IntPtr.Zero)
                try
                {
                    StringBuilder sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hp, 0, sb, ref size)) p = sb.ToString();
                }
                finally { CloseHandle(hp); }
            pathCache[pid] = p;
            return p;
        }

        // The mixer apps that are running, with their sessions, in the order of the list. An app that
        // runs without a session yet is included too (no sessions), so its row is there from the start.
        public static List<AppSessions> ForApps(IList<MixerApp> apps)
        {
            List<AppSessions> rows = new List<AppSessions>();
            if (apps == null || apps.Count == 0) return rows;
            List<Session> all = Sessions();
            Process[] procs = null;
            foreach (MixerApp a in apps)
            {
                AppSessions r = new AppSessions();
                r.App = a;
                foreach (Session s in all) if (a.MatchesProcess(s.Path)) r.Sessions.Add(s);
                if (r.Sessions.Count > 0) r.ProcessPath = r.Sessions[0].Path;
                else
                {
                    if (procs == null) procs = Process.GetProcesses();
                    r.ProcessPath = RunningPath(a, procs);
                }
                if (r.ProcessPath != null) rows.Add(r);
            }
            if (procs != null) foreach (Process p in procs) p.Dispose();
            return rows;
        }

        // The exe of a running process of the app, or null when it isn't running.
        static string RunningPath(MixerApp a, Process[] procs)
        {
            bool byExe = !a.Match.StartsWith("pkg:", StringComparison.OrdinalIgnoreCase);
            string name = byExe ? Path.GetFileNameWithoutExtension(a.Match) : null;
            foreach (Process p in procs)
            {
                string pn;
                try { pn = p.ProcessName; } catch { continue; }
                // By name first: cheap, and only those processes need their full path read.
                if (byExe && !string.Equals(pn, name, StringComparison.OrdinalIgnoreCase)) continue;
                string path = PathOf((uint)p.Id);
                if (path != null && a.MatchesProcess(path)) return path;
            }
            return null;
        }

        // Apps with sound right now, one entry per exe, playing ones first: offered when adding an app.
        public static List<MixerApp> Running()
        {
            List<MixerApp> result = new List<MixerApp>();
            List<MixerApp> quiet = new List<MixerApp>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Session> all = Sessions();
            // Playing sessions first, so an exe with one of each counts as playing.
            all.Sort(delegate(Session a, Session b) { return b.Active.CompareTo(a.Active); });
            foreach (Session s in all)
            {
                string exe = Path.GetFileName(s.Path);
                if (!seen.Add(exe)) continue;
                MixerApp m = new MixerApp(DisplayName(s.Path), exe, s.Path);
                if (s.Active) result.Add(m); else quiet.Add(m);
            }
            result.AddRange(quiet);
            return result;
        }

        // "Spotify" for Spotify.exe: the file's description, else its name.
        static string DisplayName(string exe)
        {
            string name = null;
            try { name = FileVersionInfo.GetVersionInfo(exe).FileDescription; } catch { }
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0 || name.Length > 40)
                name = Path.GetFileNameWithoutExtension(exe);
            name = name.Trim();
            foreach (string prefix in new[] { "Google ", "Microsoft ", "Mozilla " })
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length) name = name.Substring(prefix.Length);
            return name;
        }

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

        // The device enumerator, created by its class id. Volume.cs creates the same object: .NET then
        // hands back the wrapper it already made, and a typed `new SomeComClass()` fails to cast it
        // ("Unable to cast MMDeviceEnumeratorCo to MMDeviceEnumeratorCo"). Untyped, only the interface
        // cast happens, which works whatever wrapper comes back.
        internal static object CreateEnumerator()
        {
            return Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl
        {
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(string name, ref Guid ctx);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(string path, ref Guid ctx);
            [PreserveSig] int GetGroupingParam(out Guid param);
            [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid ctx);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface ISimpleAudioVolume
        {
            void SetMasterVolume(float level, ref Guid ctx);
            void GetMasterVolume(out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}

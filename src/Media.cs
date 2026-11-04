using System;
using System.Drawing;
using System.IO;
using System.Threading;

namespace QuickCut
{
    // What's playing right now, as Windows reports it (the same info as the media flyout).
    // Works with any app that reports to Windows: Spotify, browsers, VLC, Media Player, ...
    class MediaInfo
    {
        public string Title = "", Artist = "", App = "";
        public bool Playing;
        public Bitmap Thumb;   // may be null; some apps report it a moment later than the title
        public string Key { get { return App + "\n" + Title + "\n" + Artist; } }
    }

    // All calls run on a background thread, so the ring never waits for them. Changed fires
    // (on that thread) when the info is different from before.
    static class Media
    {
        public const int Prev = 0, PlayPause = 1, Next = 2;

        static MediaInfo current;
        static int busy;           // 1 while a refresh is running
        static bool unsupported;   // older Windows (before 10 1809): no media API
        public static event Action Changed;

        public static MediaInfo Current { get { return current; } }

        public static void Refresh()
        {
            if (unsupported || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Update(MediaWin.Fetch(current)); }
                catch (TypeLoadException) { unsupported = true; }
                catch (MissingMethodException) { unsupported = true; }
                catch { }
                finally { Interlocked.Exchange(ref busy, 0); }
            });
        }

        // Sends prev / play-pause / next to the current player, then reads the new state.
        public static void Command(int which)
        {
            if (unsupported) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { MediaWin.Command(which); } catch { }
                // Flip play/pause right away, so the button doesn't lag behind the click.
                MediaInfo c = current;
                if (which == PlayPause && c != null)
                {
                    MediaInfo n = new MediaInfo { Title = c.Title, Artist = c.Artist, App = c.App, Thumb = c.Thumb, Playing = !c.Playing };
                    current = n;
                    Raise();
                }
                Thread.Sleep(300);
                Refresh();
            });
        }

        static void Update(MediaInfo n)
        {
            MediaInfo old = current;
            bool same = old == null ? n == null
                : n != null && old.Key == n.Key && old.Playing == n.Playing && old.Thumb == n.Thumb;
            current = n;
            if (!same) Raise();
        }

        static void Raise()
        {
            Action h = Changed;
            if (h != null) try { h(); } catch { }
        }

        // "Spotify.exe" -> "Spotify", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" -> "ZuneMusic"
        public static string AppName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string s = id;
            int bang = s.IndexOf('!');
            if (bang >= 0) s = s.Substring(0, bang);
            int us = s.IndexOf('_');
            if (us > 0) s = s.Substring(0, us);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            else { int dot = s.LastIndexOf('.'); if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1); }
            if (s.Length > 0) s = char.ToUpper(s[0]) + s.Substring(1);
            return s;
        }
    }

    // The WinRT part, kept apart so a missing API only fails here (and Media marks it unsupported).
    static class MediaWin
    {
        static Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager mgr;

        // Waits for a WinRT call without the SDK's await helpers (they need the SDK's Windows.winmd).
        internal static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            int waited = 0;
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (waited > 3000) { op.Cancel(); throw new TimeoutException(); }
                Thread.Sleep(5); waited += 5;
            }
            return op.GetResults();
        }

        static Windows.Media.Control.GlobalSystemMediaTransportControlsSession Session()
        {
            if (mgr == null) mgr = Wait(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            return mgr.GetCurrentSession();
        }

        // prev: the last result, to reuse its thumbnail when the track hasn't changed.
        public static MediaInfo Fetch(MediaInfo prev)
        {
            var s = Session();
            if (s == null) return null;
            var p = Wait(s.TryGetMediaPropertiesAsync());
            if (p == null || string.IsNullOrEmpty(p.Title)) return null;
            var pb = s.GetPlaybackInfo();
            MediaInfo m = new MediaInfo();
            m.Title = p.Title ?? "";
            m.Artist = p.Artist ?? "";
            m.App = Media.AppName(s.SourceAppUserModelId);
            m.Playing = pb != null && pb.PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (prev != null && prev.Key == m.Key && prev.Thumb != null) m.Thumb = prev.Thumb;
            else if (p.Thumbnail != null) m.Thumb = LoadThumb(p.Thumbnail);
            return m;
        }

        static Bitmap LoadThumb(Windows.Storage.Streams.IRandomAccessStreamReference r)
        {
            try
            {
                var stream = Wait(r.OpenReadAsync());
                try
                {
                    uint len = (uint)Math.Min(stream.Size, 8 * 1024 * 1024);
                    if (len == 0) return null;
                    var reader = new Windows.Storage.Streams.DataReader(stream);
                    uint got = Wait(reader.LoadAsync(len));
                    byte[] buf = new byte[got];
                    reader.ReadBytes(buf);
                    using (MemoryStream ms = new MemoryStream(buf))
                    using (Image img = Image.FromStream(ms))
                        return Square(img, 96);
                }
                finally { IDisposable d = stream as IDisposable; if (d != null) d.Dispose(); }
            }
            catch { return null; }
        }

        // Center-crops to a small square (video thumbnails are 16:9), so drawing is cheap.
        static Bitmap Square(Image img, int px)
        {
            int side = Math.Min(img.Width, img.Height);
            Rectangle src = new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side);
            Bitmap b = new Bitmap(px, px);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new Rectangle(0, 0, px, px), src, GraphicsUnit.Pixel);
            }
            return b;
        }

        public static void Command(int which)
        {
            var s = Session();
            if (s == null) return;
            if (which == Media.Prev) Wait(s.TrySkipPreviousAsync());
            else if (which == Media.Next) Wait(s.TrySkipNextAsync());
            else Wait(s.TryTogglePlayPauseAsync());
        }
    }
}

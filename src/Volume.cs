using System;
using System.Runtime.InteropServices;

namespace QuickCut
{
    // Reads and sets the Windows master volume of the default output device (Core Audio).
    // The device is looked up on every call, so plugging in headphones just works.
    static class Volume
    {
        static IMMDeviceEnumerator enumerator;

        static IAudioEndpointVolume Endpoint()
        {
            if (enumerator == null) enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
            IMMDevice dev;
            if (enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out dev) != 0 || dev == null) return null;
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out o) != 0) return null;
            return o as IAudioEndpointVolume;
        }

        // level: 0..1. False when there is no output device (or Core Audio isn't available).
        public static bool TryGet(out float level, out bool muted)
        {
            level = 0; muted = false;
            try
            {
                IAudioEndpointVolume ep = Endpoint();
                if (ep == null) return false;
                ep.GetMasterVolumeLevelScalar(out level);
                ep.GetMute(out muted);
                return true;
            }
            catch { return false; }
        }

        // Sets the level and unmutes.
        public static bool Set(float level)
        {
            try
            {
                IAudioEndpointVolume ep = Endpoint();
                if (ep == null) return false;
                Guid ctx = Guid.Empty;
                ep.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, level)), ref ctx);
                ep.SetMute(false, ref ctx);
                return true;
            }
            catch { return false; }
        }

        public static bool SetMute(bool mute)
        {
            try
            {
                IAudioEndpointVolume ep = Endpoint();
                if (ep == null) return false;
                Guid ctx = Guid.Empty;
                ep.SetMute(mute, ref ctx);
                return true;
            }
            catch { return false; }
        }

        // One step up (+1) or down (-1). A level between steps snaps to the next step in that direction,
        // so 37% goes to 40% / 30% with 10% steps.
        public static int NextStep(int percent, int step, int dir)
        {
            int next = dir > 0 ? percent / step * step + step : (percent + step - 1) / step * step - step;
            return Math.Max(0, Math.Min(100, next));
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCo { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            void RegisterControlChangeNotify(IntPtr notify);
            void UnregisterControlChangeNotify(IntPtr notify);
            void GetChannelCount(out uint count);
            void SetMasterVolumeLevel(float db, ref Guid ctx);
            void SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            void GetMasterVolumeLevel(out float db);
            void GetMasterVolumeLevelScalar(out float level);
            void SetChannelVolumeLevel(uint channel, float db, ref Guid ctx);
            void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
            void GetChannelVolumeLevel(uint channel, out float db);
            void GetChannelVolumeLevelScalar(uint channel, out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}

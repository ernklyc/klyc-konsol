using System.Runtime.InteropServices;

namespace KlycKonsol.Input;

/// <summary>Varsayılan çıkış aygıtının ana ses seviyesi ve sessize alma (Core Audio).</summary>
public static class SystemAudio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr p);
        int UnregisterControlChangeNotify(IntPtr p);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float level, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
        int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        int GetChannelVolumeLevel(uint ch, out float level);
        int GetChannelVolumeLevelScalar(uint ch, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    static IAudioEndpointVolume? Open()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out var obj);
            return (IAudioEndpointVolume)obj;
        }
        catch { return null; }
    }

    /// <summary>0..1 arası ses seviyesi; okunamazsa null.</summary>
    public static float? Volume
    {
        get
        {
            var v = Open();
            if (v == null) return null;
            try { v.GetMasterVolumeLevelScalar(out var level); return level; } catch { return null; }
        }
        set
        {
            var v = Open();
            if (v == null || value == null) return;
            var ctx = Guid.Empty;
            try { v.SetMasterVolumeLevelScalar(Math.Clamp(value.Value, 0f, 1f), ref ctx); } catch { }
        }
    }

    public static bool Muted
    {
        get
        {
            var v = Open();
            if (v == null) return false;
            try { v.GetMute(out var m); return m; } catch { return false; }
        }
        set
        {
            var v = Open();
            if (v == null) return;
            var ctx = Guid.Empty;
            try { v.SetMute(value, ref ctx); } catch { }
        }
    }
}

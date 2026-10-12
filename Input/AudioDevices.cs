using System.Runtime.InteropServices;

namespace KlycKonsol.Input;

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Ses çıkış aygıtlarını listeler ve varsayılanı değiştirir (kulaklık ↔ hoparlör ↔ TV). Listeleme belgeli Core Audio ile;
/// varsayılan değiştirme için Windows'un belgelenmemiş ama yıllardır kararlı olan PolicyConfig arayüzü kullanılır (SoundSwitch gibi araçlar da bunu kullanır).
/// </summary>
public static class AudioDevices
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Fmtid; public uint Pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public IntPtr Ptr;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint i, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

    /// <summary>Son COM hatası (tanı için; Konsol günlüğüne yazılır).</summary>
    public static string? LastError { get; private set; }

    const uint DeviceStateActive = 1;
    static PropertyKey FriendlyName = new() { Fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Pid = 14 };
    static PropertyKey DeviceDesc = new() { Fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Pid = 2 };

    /// <summary>Etkin çıkış aygıtları (varsayılan işaretli). Windows ses hizmeti o an meşgulse birkaç kez yeniden dener; okunamazsa boş liste.</summary>
    public static List<AudioDevice> Outputs()
    {
        List<AudioDevice> list = new();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            list = TryOutputs();
            if (list.Count > 0) break;
            Thread.Sleep(150);
        }
        return list;
    }

    static List<AudioDevice> TryOutputs()
    {
        var list = new List<AudioDevice>();
        try
        {
            var en = (IEnumerator)new MMDeviceEnumerator();
            string? defId = null;
            if (en.GetDefaultAudioEndpoint(0, 1, out var def) == 0 && def.GetId(out var id0) == 0) defId = id0;
            var hr = en.EnumAudioEndpoints(0, DeviceStateActive, out var col);
            if (hr != 0) { LastError = $"EnumAudioEndpoints 0x{hr:X8}"; return list; }
            col.GetCount(out var n);
            for (uint i = 0; i < n; i++)
            {
                if (col.Item(i, out var dev) != 0 || dev.GetId(out var id) != 0) continue;
                list.Add(new AudioDevice(id, NameOf(dev) ?? id, string.Equals(id, defId, StringComparison.Ordinal)));
            }
        }
        catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; }
        return list;
    }

    static string? NameOf(IDevice dev)
    {
        try
        {
            if (dev.OpenPropertyStore(0, out var store) != 0) return null;
            foreach (var key in new[] { FriendlyName, DeviceDesc })
            {
                var k = key;
                if (store.GetValue(ref k, out var pv) != 0) continue;
                try { if (pv.Vt == 31 && pv.Ptr != IntPtr.Zero) return Marshal.PtrToStringUni(pv.Ptr); }   // VT_LPWSTR
                finally { PropVariantClear(ref pv); }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Aygıtı oyun, medya ve iletişim rollerinde varsayılan yapar. Başarılıysa true.</summary>
    public static bool SetDefault(string deviceId)
    {
        try
        {
            var cfg = (IPolicyConfig)new PolicyConfigClient();
            var ok = true;
            for (var role = 0; role < 3; role++) ok &= cfg.SetDefaultEndpoint(deviceId, role) == 0;
            return ok;
        }
        catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return false; }
    }
}

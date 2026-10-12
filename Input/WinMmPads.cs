using System.Runtime.InteropServices;

namespace KlycKonsol.Input;

/// <summary>
/// XInput dışı kolları (PlayStation, Nintendo Switch Pro, genel HID kollar) Windows'un klasik joystick arayüzüyle (winmm) okur: kol arka plandayken de çalışır
/// (masaüstü modu ve bekçi Guide'ı için gerekli; Windows.Gaming.Input arka planda girdi vermez). Xbox/XInput kolları (üretici Microsoft) atlanır; onları XInput okur.
/// Düğme/çubuk eşlemesi <see cref="HidPadMapper"/>'dadır. Gerçek donanımla (her kol modeliyle) doğrulanmadı.
/// </summary>
public sealed class WinMmPads
{
    const uint JOYERR_NOERROR = 0;
    const uint JOY_RETURNALL = 0x000000FF;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct JOYCAPSW
    {
        public ushort wMid, wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax, wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOYINFOEX { public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2; }

    [DllImport("winmm.dll")] static extern uint joyGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint joyGetDevCapsW(UIntPtr id, out JOYCAPSW caps, uint size);
    [DllImport("winmm.dll")] static extern uint joyGetPosEx(uint id, ref JOYINFOEX info);

    sealed record Dev(uint Id, HidPadKind Kind, string Name, uint[] Min, uint[] Max, int Buttons);

    readonly object _lock = new();
    List<Dev> _devs = new();
    long _nextScan;

    public int Count { get { lock (_lock) return _devs.Count; } }

    /// <summary>Bağlı HID kolların (ad, tür) listesi.</summary>
    public IReadOnlyList<(string Name, HidPadKind Kind)> Describe() { lock (_lock) return _devs.Select(d => (d.Name, d.Kind)).ToList(); }

    /// <summary>
    /// Kolları yeniden tarar. <paramref name="xinputPresent"/> true ise tanınmayan ("genel") kollar atlanır: büyük olasılıkla XInput kolunun joystick görünümüdür.
    /// </summary>
    public void Rescan(bool xinputPresent)
    {
        var found = new List<Dev>();
        try
        {
            var n = Math.Min(joyGetNumDevs(), 16u);
            for (uint id = 0; id < n; id++)
            {
                if (joyGetDevCapsW((UIntPtr)id, out var c, (uint)Marshal.SizeOf<JOYCAPSW>()) != JOYERR_NOERROR) continue;
                var probe = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNALL };
                if (joyGetPosEx(id, ref probe) != JOYERR_NOERROR) continue;                  // takılı değil
                if (c.wMid == 0x045E) continue;                                               // Microsoft (Xbox): XInput okur
                var kind = HidPadMapper.Detect(c.wMid, c.wPid);
                if (kind == HidPadKind.Generic && xinputPresent) continue;
                found.Add(new Dev(id, kind, string.IsNullOrWhiteSpace(c.szPname) ? HidPadMapper.Name(kind) : c.szPname.Trim(),
                    [c.wXmin, c.wYmin, c.wZmin, c.wRmin, c.wUmin, c.wVmin], [c.wXmax, c.wYmax, c.wZmax, c.wRmax, c.wUmax, c.wVmax], (int)Math.Min(c.wNumButtons, 32)));
            }
        }
        catch { /* winmm yoksa ya da hata: HID kol yok sayılır */ }
        lock (_lock) _devs = found;
    }

    /// <summary>Gerekirse yeniden tarar (2 sn'de bir; kol yokken de aynı hızda, ucuz).</summary>
    public void RescanIfDue(long now, bool xinputPresent)
    {
        if (now < _nextScan) return;
        _nextScan = now + 2000;
        Rescan(xinputPresent);
    }

    /// <summary>POV (0,01 derece; 65535 = orta) → switch konumu (0 orta, 1 yukarı, saat yönünde 8'e kadar).</summary>
    public static int PovToSwitch(uint pov) => pov > 35999 ? 0 : (int)((pov + 2250) / 4500 % 8) + 1;

    /// <summary>Ham eksen değerini kolun kendi aralığına göre 0..1'e çevirir.</summary>
    public static double Norm(uint v, uint min, uint max) => max <= min ? 0.5 : Math.Clamp((v - (double)min) / (max - min), 0, 1);

    /// <summary>Tüm HID kolların birleşik durumu (düğmeler OR, çubuklar en büyük sapma). Hiç kol yoksa false.</summary>
    public bool TryRead(out PadState merged)
    {
        merged = default;
        List<Dev> devs;
        lock (_lock) devs = _devs;
        if (devs.Count == 0) return false;
        double bestL = 0, bestR = 0; var any = false;
        foreach (var d in devs)
        {
            var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNALL };
            if (joyGetPosEx(d.Id, ref info) != JOYERR_NOERROR) continue;
            any = true;
            var buttons = new bool[32];
            for (var i = 0; i < 32; i++) buttons[i] = (info.dwButtons & (1u << i)) != 0;
            var axes = new[]
            {
                Norm(info.dwXpos, d.Min[0], d.Max[0]), Norm(info.dwYpos, d.Min[1], d.Max[1]), Norm(info.dwZpos, d.Min[2], d.Max[2]),
                Norm(info.dwRpos, d.Min[3], d.Max[3]), Norm(info.dwUpos, d.Min[4], d.Max[4]), Norm(info.dwVpos, d.Min[5], d.Max[5]),
            };
            var s = HidPadMapper.Map(d.Kind, buttons, PovToSwitch(info.dwPOV), axes);
            merged.Buttons |= s.Buttons;
            merged.LT = Math.Max(merged.LT, s.LT); merged.RT = Math.Max(merged.RT, s.RT);
            var ml = Math.Abs(s.LX) + Math.Abs(s.LY); var mr = Math.Abs(s.RX) + Math.Abs(s.RY);
            if (ml > bestL) { bestL = ml; merged.LX = s.LX; merged.LY = s.LY; }
            if (mr > bestR) { bestR = mr; merged.RX = s.RX; merged.RY = s.RY; }
        }
        return any;
    }
}

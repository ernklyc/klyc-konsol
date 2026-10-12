using System.Runtime.InteropServices;

namespace KlycKonsol.Input;

/// <param name="Hz">Yenileme hızı (Hz)</param>
public readonly record struct DisplayMode(int Width, int Height, int Hz)
{
    public string Resolution => $"{Width}×{Height}";
    public override string ToString() => $"{Width}×{Height} · {Hz} Hz";
}

/// <summary>
/// Ana ekranın çözünürlük / yenileme hızı listesi ve değiştirilmesi (EnumDisplaySettings / ChangeDisplaySettingsEx).
/// Liste/seçim mantığı saf (test edilebilir); sistem çağrıları ince sarmalayıcıda. Değişiklik her zaman önce CDS_TEST ile denenir.
/// </summary>
public static class DisplayModes
{
    // ---- Saf mantık -----------------------------------------------------------------------

    /// <summary>Konsol için anlamlı çözünürlükler: en az 1280×720, büyükten küçüğe, tekrarsız.</summary>
    public static List<(int Width, int Height)> Resolutions(IEnumerable<DisplayMode> modes) =>
        modes.Where(m => m.Width >= 1280 && m.Height >= 720).Select(m => (m.Width, m.Height)).Distinct()
            .OrderByDescending(r => r.Width).ThenByDescending(r => r.Height).ToList();

    /// <summary>Bir çözünürlükte desteklenen yenileme hızları (en az 50 Hz; 59/60 gibi yakın değerler tek sayılır), büyükten küçüğe.</summary>
    public static List<int> RatesFor(IEnumerable<DisplayMode> modes, int width, int height)
    {
        var rates = modes.Where(m => m.Width == width && m.Height == height && m.Hz >= 50).Select(m => m.Hz).Distinct().OrderByDescending(h => h).ToList();
        var result = new List<int>();
        foreach (var r in rates)
            if (!result.Any(x => Math.Abs(x - r) <= 1)) result.Add(r);   // 60 ve 59 (59.94) iki ayrı seçenek olmasın
        return result;
    }

    /// <summary>İstenen yenileme hızına en yakın desteklenen hız (çözünürlük değişince önceki hız yoksa).</summary>
    public static int NearestRate(IReadOnlyList<int> rates, int wanted) =>
        rates.Count == 0 ? wanted : rates.OrderBy(r => Math.Abs(r - wanted)).ThenByDescending(r => r).First();

    // ---- Windows --------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string? device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);

    const int EnumCurrent = -1;
    const uint DmPelsWidth = 0x80000, DmPelsHeight = 0x100000, DmFrequency = 0x400000;
    const uint CdsUpdateRegistry = 1, CdsTest = 2;

    static DEVMODE New() => new() { dmDeviceName = "", dmFormName = "", dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

    public static DisplayMode? Current()
    {
        try
        {
            var dm = New();
            if (!EnumDisplaySettingsW(null, EnumCurrent, ref dm)) return null;
            return new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency);
        }
        catch { return null; }
    }

    /// <summary>Ana ekranın desteklediği tüm kipler (yalnızca Windows'un "masaüstünde kullanılabilir" saydığı çözünürlük/hız birleşimleri).</summary>
    public static List<DisplayMode> Supported()
    {
        var list = new HashSet<DisplayMode>();
        try
        {
            for (var i = 0; i < 4000; i++)
            {
                var dm = New();
                if (!EnumDisplaySettingsW(null, i, ref dm)) break;
                if (dm.dmBitsPerPel < 32) continue;
                list.Add(new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency));
            }
        }
        catch { }
        return list.ToList();
    }

    /// <summary>Kipin bu ekranda uygulanabilir olup olmadığını (değiştirmeden) sınar.</summary>
    public static bool CanApply(DisplayMode mode) => Change(mode, CdsTest) == 0;

    /// <summary>Kipi uygular (kalıcı: kayıt defterine yazılır). Başarılıysa true.</summary>
    public static bool Apply(DisplayMode mode)
    {
        if (Change(mode, CdsTest) != 0) return false;
        return Change(mode, CdsUpdateRegistry) == 0;
    }

    static int Change(DisplayMode mode, uint flags)
    {
        try
        {
            var dm = New();
            if (!EnumDisplaySettingsW(null, EnumCurrent, ref dm)) return -1;   // geçerli kipten başla: yön/renk gibi alanlar korunur
            dm.dmPelsWidth = (uint)mode.Width; dm.dmPelsHeight = (uint)mode.Height; dm.dmDisplayFrequency = (uint)mode.Hz;
            dm.dmFields = DmPelsWidth | DmPelsHeight | DmFrequency;
            return ChangeDisplaySettingsExW(null, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
        }
        catch { return -1; }
    }
}

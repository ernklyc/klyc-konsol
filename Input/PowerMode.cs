using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace KlycKonsol.Input;

/// <summary>
/// "Oyun modu": Konsol arka plana indiğinde kendini Windows'un en düşük öncelikli ve en verimli sınıfına alır,
/// belleğini geri verir; böylece oyun sırasında işlemci / bellek / ısı yükü neredeyse sıfır olur.
/// Konsol öne geldiğinde her şey normale döner.
/// </summary>
/// <remarks>
/// - Öncelik sınıfı IDLE: iş parçacıkları yalnızca sistem boştayken çalışır (oyunun önüne geçemez).
/// - EcoQoS (ProcessPowerThrottling): Windows "Verimlilik modu" ile aynı; işlemci frekansı/çekirdek tercihi düşürülür.
/// - Bellek önceliği çok düşük: Windows bellek darlığında önce bu sürecin sayfalarını atar.
/// Dikkat: alt süreçler öncelik sınıfını miras alır; bu yüzden oyun HER ZAMAN Konsol normal moddayken başlatılır, ardından Enter() çağrılır.
/// </remarks>
public static class PowerMode
{
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetPriorityClass(IntPtr h, uint cls);
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr h, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr h, int infoClass, ref uint info, int size);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);

    const uint IDLE = 0x40, NORMAL = 0x20;
    const int ProcessMemoryPriority = 0, ProcessPowerThrottling = 4;
    const uint PowerThrottlingVersion = 1, ExecutionSpeed = 1;
    const uint MemoryPriorityVeryLow = 1, MemoryPriorityNormal = 5;

    public static bool Active { get; private set; }

    public static void Enter()
    {
        if (Active) return;
        Active = true;
        var h = GetCurrentProcess();
        try
        {
            SetPriorityClass(h, IDLE);
            var t = new PROCESS_POWER_THROTTLING_STATE { Version = PowerThrottlingVersion, ControlMask = ExecutionSpeed, StateMask = ExecutionSpeed };
            SetProcessInformation(h, ProcessPowerThrottling, ref t, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            var mp = MemoryPriorityVeryLow;
            SetProcessInformation(h, ProcessMemoryPriority, ref mp, sizeof(uint));
        }
        catch { }
        TrimMemory();
    }

    public static void Exit()
    {
        if (!Active) return;
        Active = false;
        var h = GetCurrentProcess();
        try
        {
            SetPriorityClass(h, NORMAL);
            var t = new PROCESS_POWER_THROTTLING_STATE { Version = PowerThrottlingVersion, ControlMask = ExecutionSpeed, StateMask = 0 };   // 0 = sistem varsayılanı
            SetProcessInformation(h, ProcessPowerThrottling, ref t, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            var mp = MemoryPriorityNormal;
            SetProcessInformation(h, ProcessMemoryPriority, ref mp, sizeof(uint));
        }
        catch { }
    }

    /// <summary>Yönetilen yığını sıkıştırır ve çalışma kümesini (RAM'de tutulan sayfalar) küçültür.</summary>
    public static void TrimMemory()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }
}

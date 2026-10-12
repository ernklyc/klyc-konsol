using System.Runtime.CompilerServices;

namespace KlycKonsol.Tests;

/// <summary>Testler hiçbir zaman gerçek %AppData%\KLYC-Konsol klasörüne yazmasın: veri klasörü geçici bir yere yönlendirilir.</summary>
static class TestIsolation
{
    [ModuleInitializer]
    public static void Init() =>
        Environment.SetEnvironmentVariable("KLYC_DATA_DIR", Path.Combine(Path.GetTempPath(), "klyc-konsol-tests-" + Environment.ProcessId));
}

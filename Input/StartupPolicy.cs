using KlycKonsol.Library;

namespace KlycKonsol.Input;

/// <summary>
/// Windows açılışında (Run anahtarı, <c>--startup</c> bayrağı) Konsol'un ne yapacağı: kol takılıysa doğrudan arayüz açılır;
/// kol yoksa (bilgisayar fare/klavyeyle kullanılacak) arka planda bekler, kol takılıp Guide'a / Ctrl+Alt+K'ya basılınca ya da simgeden açılır.
/// Elle başlatılınca (bayrak yok) hep açılır.
/// </summary>
public static class StartupPolicy
{
    /// <summary>Kolun bağlanması için açılışta beklenen süre (kablosuz kol uyandırılırken geç görünebilir).</summary>
    public static readonly TimeSpan PadWait = TimeSpan.FromSeconds(8);

    public const string StartupFlag = "--startup";

    /// <summary>Bu açılışta "kol var mı" diye beklenip yoksa gizlenmeli mi?</summary>
    public static bool ShouldWaitForPad(IEnumerable<string> args, AppState state) =>
        args.Contains(StartupFlag, StringComparer.OrdinalIgnoreCase) && state.StartupNeedsPad;

    /// <summary>Run anahtarına yazılacak komut: yol tırnaklı, açılış bayrağıyla.</summary>
    public static string RunCommand(string exePath) => $"\"{exePath}\" {StartupFlag}";
}

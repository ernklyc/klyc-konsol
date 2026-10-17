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

    /// <summary>
    /// İlk açılışta (bir kez) "bilgisayar açılınca Konsol da açılsın" kendiliğinden açılır: PlayStation gibi, güç düğmesine basınca doğrudan arayüz.
    /// Kullanıcı Ayarlar'dan kapatırsa bir daha açılmaz (<see cref="AppState.AutostartInitialized"/>). Test/otomasyon açılışlarında
    /// (yalıtılmış veri klasörü, hata ayıklama bayrakları, bekçiden açılış) hiç dokunulmaz: gerçek Windows kaydı değişmesin.
    /// </summary>
    public static bool ShouldInitAutostart(IEnumerable<string> args, AppState state, bool isolatedData)
    {
        if (state.AutostartInitialized || isolatedData) return false;
        var a = args.ToList();
        string[] skip = { "--debugcmd", "--virtualonly", "--noextras", "--bench", "--from-guard", "--nopad" };
        return !a.Any(x => skip.Contains(x, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Run anahtarını yazar ve ilk kurulum durumunu işaretler; "açılışta kol yoksa gizli bekle" kapatılır (her açılışta arayüz gelsin). Başarılıysa true.</summary>
    public static bool InitAutostart(AppState state, string exePath, Action<string, string>? setRun = null)
    {
        try
        {
            setRun ??= (name, cmd) => { using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath, true); k.SetValue(name, cmd); };
            setRun(RunName, RunCommand(exePath));
            state.StartupNeedsPad = false;
            state.AutostartInitialized = true;
            state.Save();
            return true;
        }
        catch { state.AutostartInitialized = true; state.Save(); return false; }   // yazılamadıysa tekrar tekrar deneme
    }

    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunName = "KLYC-Konsol";
}

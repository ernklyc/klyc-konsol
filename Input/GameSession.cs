using System.Diagnostics;
using System.Text.RegularExpressions;
using KlycKonsol.Library;
using Microsoft.Win32;

namespace KlycKonsol.Input;

/// <summary>
/// Oyun oturumu optimizasyonu: oyun başlarken Windows güç planını "Yüksek/Nihai performans"a alır (yalnızca prizdeyken),
/// isteğe bağlı olarak bildirimleri (toast) susturur; oyun bitince her şeyi eski haline getirir.
/// Çökmeye karşı: eski değerler AppState'e yazılır; Konsol sonraki açılışta ve kapanırken geri yüklemeyi dener.
/// </summary>
public static class GameSession
{
    public sealed record Scheme(Guid Id, string Name, bool Active);

    const string ToastKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    const string ToastValue = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

    public static bool Active { get; private set; }

    // ---- Saf yardımcılar (test edilebilir) ------------------------------

    /// <summary>"powercfg /list" çıktısını ayrıştırır (dilden bağımsız: GUID + parantez içi ad + '*' etkin işareti).</summary>
    public static List<Scheme> ParseSchemes(string output)
    {
        var list = new List<Scheme>();
        foreach (Match m in Regex.Matches(output, @"([0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})\s+\(([^)]*)\)(\s*\*)?"))
            list.Add(new Scheme(Guid.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), m.Groups[3].Success));
        return list;
    }

    /// <summary>
    /// Geri yükleme kararı: yalnızca etkin plan hâlâ Konsol'un oyun başında seçtiği plansa eski plana dönülür. Başka biri (Pulse, kullanıcı)
    /// planı değiştirdiyse onun seçimi ezilmez. Konsol'un uyguladığı plan bilinmiyorsa (eski sürümden kalan oturum) eski davranış: geri yükle.
    /// </summary>
    public static bool ShouldRestore(string? appliedPlan, string? activePlan) =>
        string.IsNullOrEmpty(appliedPlan) || string.IsNullOrEmpty(activePlan)
        || string.Equals(appliedPlan, activePlan, StringComparison.OrdinalIgnoreCase);

    static readonly Guid HighPerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    static readonly Guid UltimatePerformance = Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61");
    static readonly Regex HighName = new(@"y.{1,3}ksek\s+performans|high\s+performance", RegexOptions.IgnoreCase);
    static readonly Regex UltimateName = new(@"nihai|ultimate", RegexOptions.IgnoreCase);

    /// <summary>Tercih sırası: Yüksek performans, sonra Nihai performans; yoksa null (hiçbir şey değiştirilmez).</summary>
    public static Scheme? PickTarget(IReadOnlyList<Scheme> schemes) =>
        schemes.FirstOrDefault(s => s.Id == HighPerformance || HighName.IsMatch(s.Name))
        ?? schemes.FirstOrDefault(s => s.Id == UltimatePerformance || UltimateName.IsMatch(s.Name));

    // ---- Oturum ---------------------------------------------------------

    public static void Begin(AppState state, bool usePowerPlan, bool muteToasts)
    {
        if (Active) return;
        Active = true;

        if (usePowerPlan && OnAcPower())
        {
            try
            {
                var schemes = ParseSchemes(RunPowerCfg("/list"));
                var current = schemes.FirstOrDefault(s => s.Active);
                var target = PickTarget(schemes);
                if (current != null && target != null && current.Id != target.Id)
                {
                    AppLog.Write("güç planı", $"oyun başlıyor: {current.Name} → {target.Name}");
                    state.RestorePowerPlan = current.Id.ToString();
                    state.AppliedPowerPlan = target.Id.ToString();
                    state.Save();                       // önce kaydet, sonra değiştir: çökerse geri alınabilsin
                    RunPowerCfg($"/setactive {target.Id}");
                }
            }
            catch { }
        }

        if (muteToasts)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(ToastKey);
                state.RestoreToasts = k.GetValue(ToastValue) is int v ? v : -1;   // -1 = değer yoktu (varsayılan: açık)
                state.Save();
                k.SetValue(ToastValue, 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    /// <summary>Oyun bitince (ya da Konsol kapanırken / sonraki açılışta) eski ayarlara döner.</summary>
    public static void End(AppState state)
    {
        Active = false;
        var dirty = false;

        if (state.RestorePowerPlan is { Length: > 0 } plan)
        {
            try
            {
                var active = ParseSchemes(RunPowerCfg("/list")).FirstOrDefault(x => x.Active)?.Id.ToString();
                var restore = ShouldRestore(state.AppliedPowerPlan, active);
                AppLog.Write("güç planı", restore ? $"oyun bitti: eski plana dönülüyor ({plan})" : $"oyun bitti: etkin plan ({active}) Konsol'un seçtiği ({state.AppliedPowerPlan}) değil, başkası değiştirmiş: DOKUNULMADI");
                if (restore) RunPowerCfg($"/setactive {plan}");
            }
            catch { }
            state.RestorePowerPlan = null;
            state.AppliedPowerPlan = null;
            dirty = true;
        }
        if (state.RestoreToasts is { } toasts)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(ToastKey);
                if (toasts < 0) k.DeleteValue(ToastValue, throwOnMissingValue: false);
                else k.SetValue(ToastValue, toasts, RegistryValueKind.DWord);
            }
            catch { }
            state.RestoreToasts = null;
            dirty = true;
        }
        if (dirty) state.Save();
    }

    static bool OnAcPower() => Native.Battery() is not { } b || b.charging;   // pil yoksa (masaüstü) hep "prizde"

    static string RunPowerCfg(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(8000);
        return text;
    }
}

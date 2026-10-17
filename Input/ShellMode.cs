using System.Diagnostics;
using Microsoft.Win32;
using KlycKonsol.Library;

namespace KlycKonsol.Input;

/// <summary>Kayıt defteri erişimi (testlerde sahtesi verilir; gerçek Windows kabuk ayarına dokunulmaz).</summary>
public interface IShellRegistry
{
    string? GetShell();
    void SetShell(string value);
    void DeleteShell();
}

/// <summary>Gerçek kayıt: HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell (yalnızca bu kullanıcı; yönetici izni gerekmez).</summary>
public sealed class WinlogonShellRegistry : IShellRegistry
{
    const string KeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
    public string? GetShell() { using var k = Registry.CurrentUser.OpenSubKey(KeyPath); return k?.GetValue("Shell") as string; }
    public void SetShell(string value) { using var k = Registry.CurrentUser.CreateSubKey(KeyPath, true); k.SetValue("Shell", value, RegistryValueKind.String); }
    public void DeleteShell() { using var k = Registry.CurrentUser.OpenSubKey(KeyPath, true); k?.DeleteValue("Shell", false); }
}

/// <summary>
/// "Konsol modu": bilgisayar açılınca (oturum açıldığında) Windows masaüstü/görev çubuğu yerine doğrudan Konsol gelir — konsol gibi.
/// Kabuk olarak küçük bir gözetmen (<c>KlycShell.exe</c>) kaydedilir; o Konsol'u başlatır ve her türlü sorunda Windows masaüstüne geri döner
/// (Safe Mode, SHIFT basılı oturum açma, Konsol bulunamaz, çökme döngüsü). Her zaman geri alınabilir: Konsol → Ayarlar → Konsol modu (kapat),
/// son çare Ctrl+Alt+Del → Görev Yöneticisi → Yeni görev → explorer.exe. Yalnızca bu kullanıcıyı etkiler (HKCU).
/// </summary>
public static class ShellMode
{
    public const string StubName = "KlycShell.exe";

    /// <summary>Gözetmenin güncellemelerden etkilenmeyen kalıcı kopyası (Velopack "current" klasörü güncellemede değişir; çalışan kabuk dosyası kilitlenmesin).</summary>
    public static string StubDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KLYC-Konsol", "shell");
    public static string OffFile => Path.Combine(AppPaths.Dir, "shell.off");

    /// <summary>Kabuk değeri: gözetmen + Konsol yolu (+ yönetici olarak açma görevi). Yollar tırnaklı.</summary>
    public static string BuildCommand(string stubExe, string konsolExe, string? task = null) =>
        $"\"{stubExe}\" --konsol \"{konsolExe}\"" + (string.IsNullOrEmpty(task) ? "" : $" --task \"{task}\"");

    public static bool IsEnabled(IShellRegistry? reg = null)
    {
        try { return (reg ?? new WinlogonShellRegistry()).GetShell() is { } v && v.Contains(StubName, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>Konsol'un kararlı başlatma yolu: Velopack kurulumunda kök "KLYC Konsol.exe" (güncellemeden etkilenmez), değilse çalışan exe.</summary>
    public static string StableKonsolExe(string processPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(processPath);
            if (dir != null && string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(dir) is { } root)
            {
                var stub = Path.Combine(root, "KLYC Konsol.exe");
                if (File.Exists(stub)) return stub;
            }
        }
        catch { }
        return processPath;
    }

    /// <summary>Kabuğu etkinleştirir. Ön koşullar (gözetmen dosyası, Konsol dosyası) sağlanmazsa HİÇBİR ŞEY yazmaz. (ok, ileti)</summary>
    public static (bool Ok, string Message) Enable(AppState state, string processPath, string? elevatedTask, IShellRegistry? reg = null, string? stubSource = null, string? stubDir = null)
    {
        reg ??= new WinlogonShellRegistry();
        try
        {
            var src = stubSource ?? Path.Combine(Path.GetDirectoryName(processPath) ?? "", StubName);
            if (!File.Exists(src)) return (false, "KlycShell.exe bulunamadı (kurulum eksik).");
            var konsol = StableKonsolExe(processPath);
            if (!File.Exists(konsol)) return (false, "Konsol dosyası bulunamadı.");
            var dir = stubDir ?? StubDir;
            Directory.CreateDirectory(dir);
            var stub = Path.Combine(dir, StubName);
            try { File.Copy(src, stub, overwrite: true); }
            catch (IOException) when (File.Exists(stub)) { /* çalışan kopya kilitli: aynı sürüm varsayılır */ }
            if (!File.Exists(stub)) return (false, "Gözetmen kopyalanamadı.");

            var current = reg.GetShell();
            if (current is null || !current.Contains(StubName, StringComparison.OrdinalIgnoreCase))
            {
                state.ShellBackup = current ?? "";   // "" = değer yoktu (varsayılan explorer.exe)
                state.Save();
            }
            reg.SetShell(BuildCommand(stub, konsol, elevatedTask));
            return (true, "Konsol modu açıldı: bir sonraki oturum açışında Windows masaüstü yerine Konsol gelir.");
        }
        catch (Exception ex) { return (false, $"Açılamadı: {ex.Message}"); }
    }

    /// <summary>Önceki kabuğa (yoksa Windows varsayılanına) döner.</summary>
    public static (bool Ok, string Message) Disable(AppState state, IShellRegistry? reg = null)
    {
        reg ??= new WinlogonShellRegistry();
        try
        {
            var backup = state.ShellBackup;
            if (string.IsNullOrEmpty(backup) || backup.Contains(StubName, StringComparison.OrdinalIgnoreCase)) reg.DeleteShell();
            else reg.SetShell(backup);
            state.ShellBackup = null; state.Save();
            return (true, "Konsol modu kapatıldı: bir sonraki oturum açışında Windows masaüstü gelir.");
        }
        catch (Exception ex) { return (false, $"Kapatılamadı: {ex.Message}"); }
    }

    public static bool ExplorerRunning()
    {
        try { return Process.GetProcessesByName("explorer").Any(p => { try { return p.SessionId == Process.GetCurrentProcess().SessionId; } catch { return false; } }); }
        catch { return false; }
    }

    /// <summary>Windows masaüstünü (görev çubuğu dahil) başlatır; zaten açıksa dokunmaz. Konsol modunda "masaüstüne geç" ve "Konsol'dan çık" bunu kullanır.</summary>
    public static bool EnsureExplorer()
    {
        if (ExplorerRunning()) return true;
        try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); AppLog.Write("kabuk", "Windows masaüstü başlatıldı"); return true; }
        catch (Exception ex) { AppLog.Write("kabuk", "masaüstü başlatılamadı: " + ex.Message); return false; }
    }
}

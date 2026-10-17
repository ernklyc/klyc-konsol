using System.Diagnostics;
using Microsoft.Win32;

namespace KlycKonsol.Input;

/// <summary>
/// "Yönetici olarak başlat (UAC'siz)": kolla kullanırken Windows'un yönetici onay penceresine (UAC) cevap verilemez; Konsol yönetici yetkisiyle
/// açılırsa başlattığı programlar da onay istemez. Oturum açılışında Konsol, "en yüksek ayrıcalıkla çalıştır" işaretli bir zamanlanmış görevle
/// başlatılır (Pulse'ın "Windows ile başlat"ı da böyle çalışır). Görevi OLUŞTURMAK bir kez yönetici onayı ister (klavye/fareyle).
/// Dikkat: tarayıcı ve Konsol'dan başlattığın oyunlar da yönetici yetkisiyle çalışır; bu yüzden varsayılan KAPALI.
/// </summary>
public static class ElevatedStart
{
    public const string TaskName = "KLYC Konsol (yonetici)";

    /// <summary>schtasks bağımsız değişkenleri (saf; testlerde doğrulanır). Görev yalnızca bu kullanıcının oturum açışında çalışır.</summary>
    public static string CreateArgs(string exePath, string user) =>
        $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\" {StartupPolicy.StartupFlag}\" /sc onlogon /ru \"{user}\" /rl highest /f";

    public static string DeleteArgs => $"/delete /tn \"{TaskName}\" /f";

    public static bool IsEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/query /tn \"{TaskName}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(8000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Konsol şu an yönetici yetkisiyle mi çalışıyor?</summary>
    public static bool IsElevated()
    {
        try { using var id = System.Security.Principal.WindowsIdentity.GetCurrent(); return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    static bool RunElevated(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(30000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }   // UAC reddedildi ya da onay verilmedi
    }

    /// <summary>Görevi oluşturur ve Run anahtarındaki (yetkisiz) başlatmayı kaldırır (çift açılmasın). UAC onayı ister. Başarılıysa true.</summary>
    public static bool Enable(string exePath)
    {
        var ok = RunElevated(CreateArgs(exePath, $"{Environment.UserDomainName}\\{Environment.UserName}"));
        if (ok) { try { using var k = Registry.CurrentUser.OpenSubKey(StartupPolicy.RunKeyPath, true); k?.DeleteValue(StartupPolicy.RunName, false); } catch { } }
        return ok;
    }

    /// <summary>Görevi siler ve yetkisiz otomatik başlatmayı geri yazar.</summary>
    public static bool Disable(string exePath)
    {
        var ok = RunElevated(DeleteArgs);
        if (ok) { try { using var k = Registry.CurrentUser.CreateSubKey(StartupPolicy.RunKeyPath, true); k.SetValue(StartupPolicy.RunName, StartupPolicy.RunCommand(exePath)); } catch { } }
        return ok;
    }
}

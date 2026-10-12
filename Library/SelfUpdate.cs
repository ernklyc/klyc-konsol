using Velopack;
using Velopack.Sources;

namespace KlycKonsol.Library;

/// <summary>
/// Uygulama içi güncelleme (Velopack): yalnızca kurulum dosyasıyla (Setup.exe) kurulmuş Konsol kendini güncelleyebilir; zip / geliştirme kopyası güncellemeyi yalnızca
/// sürüm sayfasında gösterir. Kaynak GitHub sürümleridir (public depo, hesap/anahtar yok); testlerde <c>KLYC_UPDATE_SOURCE</c> ile yerel klasör verilir.
/// İndirilen paketin bütünlüğünü Velopack (SHA) doğrular. Güncelleme ancak kullanıcı "indir ve kur" derse yapılır; kendiliğinden kurulmaz.
/// </summary>
public static class SelfUpdate
{
    public static IUpdateSource CreateSource(string? localFeed = null) =>
        (localFeed ?? Environment.GetEnvironmentVariable("KLYC_UPDATE_SOURCE")) is { Length: > 0 } dir
            ? new SimpleFileSource(new DirectoryInfo(dir))
            : new GithubSource("https://github.com/" + UpdateService.Repo, null, false);

    public static UpdateManager CreateManager(string? localFeed = null) => new(CreateSource(localFeed));

    /// <summary>Setup.exe ile (Velopack) kurulmuş mu?</summary>
    public static bool IsInstalled(string? localFeed = null)
    {
        try { return CreateManager(localFeed).IsInstalled; }
        catch { return false; }
    }

    /// <summary>Kurulu sürümden yeni bir paket var mı? Yoksa/kurulu değilse null.</summary>
    public static async Task<Velopack.UpdateInfo?> CheckAsync(string? localFeed = null)
    {
        var mgr = CreateManager(localFeed);
        if (!mgr.IsInstalled) return null;
        return await mgr.CheckForUpdatesAsync();
    }

    /// <summary>
    /// Paketi indirir (ilerleme %), sonra uygulamayı kapatıp güncelleyip yeniden başlatır. Dönerse hata olmuştur (başarıda süreç sona erer).
    /// Güncelleme dosyaları kilitlenmesin diye çalışan bekçi önce durdurulur.
    /// </summary>
    public static async Task ApplyAsync(Velopack.UpdateInfo info, Action<int>? progress = null, string? localFeed = null, string[]? restartArgs = null)
    {
        var mgr = CreateManager(localFeed);
        await mgr.DownloadUpdatesAsync(info, p => progress?.Invoke(p));
        AppLog.Write("güncelleme", $"{info.TargetFullRelease.Version} indirildi, kuruluyor ve yeniden başlatılıyor");
        try { Input.GuardLauncher.StopRunning(); } catch { }
        mgr.ApplyUpdatesAndRestart(info.TargetFullRelease, restartArgs);
    }
}

/// <summary>
/// Başsız sınama kapıları (arayüz açılmaz). Gerçek kurulumun gerçekten güncellenebildiğini göstermek için:
/// <c>--selftest-update &lt;yerel yayın klasörü&gt; &lt;sonuç dosyası&gt;</c> güncelleyip <c>--selftest-version &lt;sonuç dosyası&gt;</c> ile yeniden başlar; o da yeni sürümü dosyaya yazıp çıkar.
/// </summary>
public static class SelfUpdateTest
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        var i = Array.FindIndex(args, a => a.Equals("--selftest-version", StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length)
        {
            File.WriteAllText(args[i + 1], AppInfo.Version);
            return true;
        }
        i = Array.FindIndex(args, a => a.Equals("--selftest-update", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 2 >= args.Length) return false;
        var feed = args[i + 1]; var result = args[i + 2];
        try
        {
            if (!SelfUpdate.IsInstalled(feed)) { File.WriteAllText(result, "kurulu-degil"); exitCode = 2; return true; }
            var info = SelfUpdate.CheckAsync(feed).GetAwaiter().GetResult();
            if (info is null) { File.WriteAllText(result, "guncelleme-yok"); exitCode = 3; return true; }
            File.WriteAllText(result, "eski:" + AppInfo.Version);
            SelfUpdate.ApplyAsync(info, null, feed, new[] { "--selftest-version", result }).GetAwaiter().GetResult();
            File.WriteAllText(result, "uygulama-donmedi");   // buraya gelinmemeli: başarıda süreç sona erer
            exitCode = 4;
        }
        catch (Exception ex) { File.WriteAllText(result, "hata:" + ex.Message); exitCode = 1; }
        return true;
    }
}

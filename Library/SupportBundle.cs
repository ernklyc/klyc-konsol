using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KlycKonsol.Library;

/// <summary>
/// "Sorun bildir": hata ayıklamaya yetecek bilgiyi KİŞİSEL VERİ OLMADAN tek bir zipte toplar (masaüstüne): sürüm, Windows, ekran, kol sayısı,
/// ayar bayrakları, kütüphane SAYILARI (oyun listesi değil) ve günlükler. Kullanıcı adı, bilgisayar adı ve profil yolları temizlenir.
/// Hiçbir şey kendiliğinden gönderilmez; kullanıcı zipi istediği yere (GitHub sorunu) kendisi ekler.
/// </summary>
public static class SupportBundle
{
    /// <summary>Metindeki kullanıcı adı / bilgisayar adı / profil yollarını genel adlarla değiştirir. Büyük-küçük harf farkı önemsizdir.</summary>
    public static string Sanitize(string text, string userName, string machineName, string userProfile)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var s = text;
        // Önce tam profil yolu (C:\Users\ad), sonra yalnızca ad: yol içinde ya da düz metinde geçse de temizlenir
        if (!string.IsNullOrWhiteSpace(userProfile)) s = Regex.Replace(s, Regex.Escape(userProfile), @"C:\Users\<kullanici>", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?i)([A-Z]:\\Users\\)([^\\/:*?""<>|\r\n]+)", "$1<kullanici>");
        if (userName.Length >= 3) s = Regex.Replace(s, @"(?<![\p{L}\p{N}])" + Regex.Escape(userName) + @"(?![\p{L}\p{N}])", "<kullanici>", RegexOptions.IgnoreCase);
        if (machineName.Length >= 3) s = Regex.Replace(s, @"(?<![\p{L}\p{N}])" + Regex.Escape(machineName) + @"(?![\p{L}\p{N}])", "<bilgisayar>", RegexOptions.IgnoreCase);
        return s;
    }

    /// <summary>Ayarlardan yalnızca bayraklar/sayılar (yol, liste, tarayıcı adresi yok).</summary>
    public static string DescribeState(AppState st) => string.Join(Environment.NewLine, new[]
    {
        $"UseGuard={st.UseGuard}", $"CheckUpdates={st.CheckUpdates}", $"ControllerOnly={st.ControllerOnly}", $"IncludePartial={st.IncludePartial}",
        $"AutoReturn={st.AutoReturn}", $"PowerPlanOnGame={st.PowerPlanOnGame}", $"MuteNotificationsOnGame={st.MuteNotificationsOnGame}",
        $"UiSounds={st.UiSounds}", $"Haptics={st.Haptics}", $"StartupNeedsPad={st.StartupNeedsPad}",
        $"Favorites={st.Favorites.Count}", $"Hidden={st.Hidden.Count}", $"GameFolders={st.GameFolders.Count}", $"Bookmarks={st.Bookmarks.Count}",
    });

    /// <summary>Kütüphane özeti: mağaza başına oyun sayısı (adlar yok).</summary>
    public static string DescribeLibrary(IEnumerable<Models.Game> games) =>
        string.Join(Environment.NewLine, games.GroupBy(g => $"{g.Source} / {g.Kind}").OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}"));

    /// <summary>info.txt içeriği (saf: dışarıdan gelen değerlerle).</summary>
    public static string BuildInfo(string version, bool installed, string os, string runtime, string culture, string screen, int pads, string pulse, string state, string library, DateTime now) =>
        string.Join(Environment.NewLine, new[]
        {
            "KLYC Konsol sorun bildirimi", $"Tarih: {now:yyyy-MM-dd HH:mm}", "",
            $"Sürüm: {version}", $"Kurulum: {(installed ? "Setup.exe (kendini güncelleyebilir)" : "zip / geliştirme")}",
            $"Windows: {os}", $".NET: {runtime}", $"Dil/bölge: {culture}", $"Ekran: {screen}", $"Bağlı kol sayısı: {pads}", $"Pulse: {pulse}", "",
            "[Ayarlar (yalnızca bayraklar ve sayılar)]", state, "", "[Kütüphane (yalnızca sayılar)]", library, "",
            "Not: günlüklerde kullanıcı adı, bilgisayar adı ve profil yolları temizlenmiştir. Oyun adları ve kimlikleri günlüklerde geçebilir.",
        });

    /// <summary>Günlükleri temizleyip zipler; masaüstüne (yoksa geçici klasöre) yazar ve yolunu döner.</summary>
    public static string Create(string info, string? outDir = null)
    {
        var user = Environment.UserName; var machine = Environment.MachineName;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        outDir ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(outDir) || !Directory.Exists(outDir)) outDir = Path.GetTempPath();
        var path = Path.Combine(outDir, $"KLYC-Konsol-sorun-bildir-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        if (File.Exists(path)) File.Delete(path);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(Sanitize(content, user, machine, profile));
        }
        Add("info.txt", info);
        foreach (var f in new[] { "konsol.log", "konsol.log.1", "crash.log", "tray.log", "status.json" })
        {
            var p = Path.Combine(AppPaths.Dir, f);
            if (!File.Exists(p)) continue;
            try { Add(f, TailText(p, 300 * 1024)); } catch { }
        }
        return path;
    }

    /// <summary>Dosyanın son <paramref name="maxBytes"/> baytı (kullanımdaki dosyayı da okur).</summary>
    static string TailText(string path, int maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > maxBytes) fs.Seek(-maxBytes, SeekOrigin.End);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd();
    }
}

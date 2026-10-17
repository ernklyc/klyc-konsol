using System.Text.Json;

namespace KlycKonsol.Library;

public static class AppPaths
{
    /// <summary>Veri klasörü. KLYC_DATA_DIR ortam değişkeni verilirse oraya yazılır (testler gerçek kullanıcı verisine dokunmasın).</summary>
    public static readonly string Dir = Environment.GetEnvironmentVariable("KLYC_DATA_DIR") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KLYC-Konsol");

    public static string LibraryFile => Path.Combine(Dir, "library.json");
    public static string StateFile => Path.Combine(Dir, "state.json");
}

/// <summary>library.json: elle eklenen oyun/uygulamalar.</summary>
public sealed class UserLibrary
{
    public List<UserEntry> Entries { get; set; } = new();

    public static UserLibrary Load()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Dir);
            if (!File.Exists(AppPaths.LibraryFile))
            {
                var sample = new UserLibrary();
                File.WriteAllText(AppPaths.LibraryFile, JsonSerializer.Serialize(sample, Json.Options));
                return sample;
            }
            return JsonSerializer.Deserialize<UserLibrary>(File.ReadAllText(AppPaths.LibraryFile), Json.Options) ?? new();
        }
        catch { return new(); }
    }
}

/// <param name="Type">Steam türü: game, dlc, demo, tool... ("removed" = mağazada yok)</param>
public sealed record OwnedInfo(string Name, string Type);

public static class AppInfo
{
    /// <summary>Derleme sürümü (csproj &lt;Version&gt;), örn. "0.5.0".</summary>
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "?";
}
public sealed record Bookmark(string Title, string Url);

/// <summary>Bir oyuna özel ayarlar. "auto"/"default" = genel ayar geçerli.</summary>
public sealed class GameOptions
{
    /// <summary>"auto" (genel ayara uy) ya da "off" (bu oyunda güç planını değiştirme).</summary>
    public string PowerPlan { get; set; } = "auto";
    /// <summary>"default" (genel ayar), "on" ya da "off": oyun kapanınca Konsol'a dön.</summary>
    public string AutoReturn { get; set; } = "default";
}

/// <param name="Kind">"game" ya da "app"</param>
/// <param name="Target">exe yolu, URL ya da protokol (ör. ms-settings:)</param>
public sealed record UserEntry(string Title, string Target, string? Args = null, string Kind = "game", string? Image = null, bool? Controller = null);

/// <summary>state.json: son oynama zamanları ve favoriler.</summary>
public sealed class AppState
{
    /// <summary>Oyun sırasında Konsol'u tamamen kapat, yerine çok küçük bekçi çalışsın (KlycGuard.exe varsa).</summary>
    public bool UseGuard { get; set; } = true;
    /// <summary>Günde bir kez GitHub'dan yeni sürüm sor (hiçbir şey indirmez).</summary>
    public bool CheckUpdates { get; set; } = true;
    public long LastUpdateCheck { get; set; }
    public string DismissedUpdate { get; set; } = "";
    /// <summary>Tarayıcı oturumu: açık sekmelerin adresleri (Konsol yeniden açılınca / oyundan dönünce geri yüklenir).</summary>
    public List<string> BrowserTabs { get; set; } = new();
    public int BrowserCurrent { get; set; }
    public List<Bookmark> Bookmarks { get; set; } = new();
    /// <summary>Sayfa yakınlaştırması (1.0 = %100); kanepeden okunabilsin diye varsayılan %125.</summary>
    public double BrowserZoom { get; set; } = 1.25;
    /// <summary>Menüde gezinirken / seçerken kısa arayüz sesleri.</summary>
    public bool UiSounds { get; set; } = true;
    /// <summary>Onay / geri tuşlarında kısa titreşim.</summary>
    public bool Haptics { get; set; } = true;
    public Dictionary<string, long> LastPlayed { get; set; } = new();
    public List<string> Favorites { get; set; } = new();
    /// <summary>Steam hesabındaki oyunların adı/türü (appid → bilgi); kurulu olmayanlar listesi için önbellek.</summary>
    public Dictionary<string, OwnedInfo> SteamOwned { get; set; } = new();
    /// <summary>Konsol'un kendi izlediği oynama süreleri (saniye); Steam oyunları Steam'in kaydından okunur.</summary>
    public Dictionary<string, long> PlaySeconds { get; set; } = new();
    /// <summary>Kol desteği önbelleği: oyun kimliği → "full" | "partial" | "none".</summary>
    public Dictionary<string, string> Support { get; set; } = new();
    /// <summary>Son doğrulama zamanı (unix sn); "none" sonuçları 30 gün sonra yeniden sorulur.</summary>
    public Dictionary<string, long> SupportChecked { get; set; } = new();
    /// <summary>Kütüphaneden gizlenen oyunların kimlikleri.</summary>
    public List<string> Hidden { get; set; } = new();
    /// <summary>Kullanıcının elle verdiği kol desteği: kimlik → "full" | "partial" | "none" (otomatik sonucu ezer).</summary>
    public Dictionary<string, string> SupportOverride { get; set; } = new();
    /// <summary>Steam'de ada göre bulunan karşılıklar: oyun kimliği → Steam appid.</summary>
    public Dictionary<string, string> SteamIds { get; set; } = new();
    /// <summary>Kısayolları taranacak ek klasörler (Masaüstü\Game otomatik dahildir).</summary>
    public List<string> GameFolders { get; set; } = new();
    /// <summary>Kol başına ölü bölge (0-1), kalibrasyonla ayarlanır.</summary>
    public double[] Deadzones { get; set; } = { 0.24, 0.24, 0.24, 0.24 };
    /// <summary>Oyun başına ayarlar (kimlik → ayar). Yoksa varsayılanlar geçerli.</summary>
    public Dictionary<string, GameOptions> GameOptions { get; set; } = new();
    /// <summary>Oyun öncesi etkin olan güç planının GUID'i (oyun bitince / çökme sonrası geri yüklenir).</summary>
    public string? RestorePowerPlan { get; set; }
    /// <summary>Konsol'un oyun başında kendi seçtiği güç planı. Oyun bitince etkin plan bu değilse (Pulse/kullanıcı değiştirmiş) eski plana dönülmez.</summary>
    public string? AppliedPowerPlan { get; set; }
    /// <summary>Oyun öncesi bildirim ayarı (-1 = değer yoktu).</summary>
    public int? RestoreToasts { get; set; }
    /// <summary>Oyun başlarken Windows güç planını "Yüksek performans"a al, oyun bitince geri döndür.</summary>
    public bool PowerPlanOnGame { get; set; } = true;
    /// <summary>Oyun sırasında Windows bildirimlerini (toast) sustur, oyun bitince geri aç.</summary>
    public bool MuteNotificationsOnGame { get; set; }
    /// <summary>Windows açılışında kol takılı değilse Konsol gizli beklesin (fare/klavyeyle açılan bilgisayarı örtmesin); kol takılıysa doğrudan açılır.</summary>
    public bool StartupNeedsPad { get; set; } = true;
    /// <summary>Oyun kapanınca Konsol otomatik öne gelsin.</summary>
    public bool AutoReturn { get; set; } = true;
    /// <summary>Game Bar / Guide tuşu çakışması ilk açılışta bir kez sorulur.</summary>
    public bool GuidePromptShown { get; set; }
    /// <summary>Sadece kol destekli oyunlar gösterilsin.</summary>
    public bool ControllerOnly { get; set; } = true;
    /// <summary>Kısmi kol desteği de sayılsın.</summary>
    public bool IncludePartial { get; set; } = true;
    /// <summary>Arayüz dili: "auto" (Windows Türkçeyse Türkçe, değilse İngilizce), "tr" ya da "en".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>İlk açılışta "Windows ile başlat" kendiliğinden açıldı mı (bir kez yapılır; kullanıcı sonradan kapatırsa yeniden açılmaz).</summary>
    public bool AutostartInitialized { get; set; }
    /// <summary>Kütüphane sıralaması: "smart" (varsayılan), "recent", "name", "playtime".</summary>
    public string LibrarySort { get; set; } = "smart";
    /// <summary>Üst çubukta Pulse modu / ısı bilgisini göster (Pulse çalışıyorsa). Kapatılırsa Pulse sensör okumaz.</summary>
    public bool ShowPulseInfo { get; set; } = true;

    public static AppState Load()
    {
        try
        {
            return File.Exists(AppPaths.StateFile)
                ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(AppPaths.StateFile)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Dir);
            File.WriteAllText(AppPaths.StateFile, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}

static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}

using System.Text.Json;
using System.Windows;
using KlycKonsol.Models;

namespace KlycKonsol.Library;

/// <summary>
/// Yerel önbellekte kapağı/hero görseli olmayan oyunlar için görselleri indirip
/// %AppData%\KLYC-Konsol\art altında saklar (Steam CDN ve Epic'in kendi katalog önbelleği).
/// </summary>
public static class ArtService
{
    static readonly string Dir = Path.Combine(AppPaths.Dir, "art");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static string Safe(string id) => new string(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    static string CoverFile(Game g) => Path.Combine(Dir, Safe(g.Id) + "_cover.jpg");
    static string HeroFile(Game g) => Path.Combine(Dir, Safe(g.Id) + "_hero.jpg");

    /// <summary>Daha önce indirilmiş görselleri hemen bağlar (internet gerekmez).</summary>
    public static void ApplyCached(IEnumerable<Game> games)
    {
        foreach (var g in games)
        {
            if (g.Kind != EntryKind.Game) continue;
            if ((g.Cover == null || g.IconCover) && File.Exists(CoverFile(g))) { g.Cover = GameScanner.LoadImage(CoverFile(g), 460); g.IconCover = false; }
            if (g.HeroPath == null && File.Exists(HeroFile(g))) g.HeroPath = HeroFile(g);
        }
    }

    /// <summary>Eksik görselleri indirir. Her oyun bitince <paramref name="onEach"/> UI iş parçacığında çağrılır.</summary>
    public static async Task FillMissingAsync(IEnumerable<Game> games, Action<Game> onEach)
    {
        var todo = games.Where(g => g.Kind == EntryKind.Game && (g.Cover == null || g.IconCover || g.HeroPath == null)
                                    && (g.SteamAppId != null || g.Id.StartsWith("epic:"))).ToList();
        if (todo.Count == 0) return;

        Directory.CreateDirectory(Dir);
        Dictionary<string, (string? tall, string? wide)> epic = new();
        if (todo.Any(g => g.Id.StartsWith("epic:"))) epic = await Task.Run(LoadEpicCatalog);

        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(todo.Select(async g =>
        {
            await gate.WaitAsync();
            try
            {
                var changed = false;
                // Önce Epic'in kendi görseli (varsa), eksik kalanı Steam CDN'inden tamamla.
                if (g.Meta != null && epic.TryGetValue(g.Meta, out var urls))
                {
                    if ((g.Cover == null || g.IconCover) && urls.tall != null) changed |= await Download(CoverFile(g), urls.tall);
                    if (g.HeroPath == null && urls.wide != null) changed |= await Download(HeroFile(g), urls.wide);
                }
                if (g.SteamAppId is { } id)
                {
                    if ((g.Cover == null || g.IconCover) && !File.Exists(CoverFile(g)))
                        changed |= await Download(CoverFile(g),
                            $"https://cdn.akamai.steamstatic.com/steam/apps/{id}/library_600x900.jpg",
                            $"https://cdn.akamai.steamstatic.com/steam/apps/{id}/header.jpg");
                    if (g.HeroPath == null && !File.Exists(HeroFile(g)))
                        changed |= await Download(HeroFile(g), $"https://cdn.akamai.steamstatic.com/steam/apps/{id}/library_hero.jpg");
                }

                if (changed)
                {
                    ImageSourceHolder? cover = null;
                    if ((g.Cover == null || g.IconCover) && File.Exists(CoverFile(g)))
                        cover = new ImageSourceHolder(GameScanner.LoadImage(CoverFile(g), 460));
                    var hero = g.HeroPath == null && File.Exists(HeroFile(g)) ? HeroFile(g) : null;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (cover?.Image != null) { g.Cover = cover.Image; g.IconCover = false; }
                        if (hero != null) g.HeroPath = hero;
                        onEach(g);
                    });
                }
            }
            catch { }
            finally { gate.Release(); }
        }));
    }

    sealed record ImageSourceHolder(System.Windows.Media.ImageSource? Image);

    static async Task<bool> Download(string file, params string[] urls)
    {
        if (File.Exists(file)) return true;
        foreach (var url in urls)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                if (bytes.Length < 2000) continue;
                await File.WriteAllBytesAsync(file, bytes);
                return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>catcache.bin: base64 JSON dizisi. CatalogItemId → (dikey kapak, yatay görsel).</summary>
    static Dictionary<string, (string? tall, string? wide)> LoadEpicCatalog()
    {
        var map = new Dictionary<string, (string?, string?)>();
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Catalog", "catcache.bin");
            if (!File.Exists(path)) return map;

            using var doc = JsonDocument.Parse(Convert.FromBase64String(File.ReadAllText(path)));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("id", out var idEl) || !e.TryGetProperty("keyImages", out var imgs)) continue;
                string? tall = null, wide = null;
                foreach (var img in imgs.EnumerateArray())
                {
                    var type = img.GetProperty("type").GetString();
                    var url = img.GetProperty("url").GetString();
                    if (type == "DieselGameBoxTall") tall = url;
                    else if (type == "DieselGameBox") wide = url;
                    else if (type == "OfferImageWide") wide ??= url;
                }
                map[idEl.GetString() ?? ""] = (tall, wide);
            }
        }
        catch { }
        return map;
    }
}

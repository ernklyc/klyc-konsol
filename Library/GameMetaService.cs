using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using KlycKonsol.Models;

namespace KlycKonsol.Library;

/// <summary>Detay sayfası için oyun bilgisi (açıklama, tür, çıkış tarihi, geliştirici, ekran görüntüleri).</summary>
public sealed record GameMeta(string? Description, string[] Genres, string? Released, string[] Developers, string[] Screenshots);

/// <summary>
/// Bilgiyi Steam mağazasından (Türkçe, appdetails) alır; Steam karşılığı olmayan Epic oyunlarında Epic'in yerel kataloğundan okur.
/// Sonuçlar %AppData%\KLYC-Konsol\meta altında JSON + küçük ekran görüntüleri olarak önbelleğe alınır.
/// </summary>
public static class GameMetaService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static string Dir => Path.Combine(AppPaths.Dir, "meta");
    static string Safe(string id) => new string(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    static string JsonFile(Game g) => Path.Combine(Dir, Safe(g.Id) + ".json");

    public static GameMeta? TryLoadCached(Game g)
    {
        try
        {
            var f = JsonFile(g);
            if (!File.Exists(f)) return null;
            var m = JsonSerializer.Deserialize<GameMeta>(File.ReadAllText(f));
            // Dosyası silinmiş ekran görüntülerini ele
            return m == null ? null : m with { Screenshots = m.Screenshots.Where(File.Exists).ToArray() };
        }
        catch { return null; }
    }

    public static async Task<GameMeta?> FetchAsync(Game g, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Dir);
        GameMeta? meta = null;

        if (g.SteamAppId != null)
        {
            try
            {
                var json = await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={g.SteamAppId}&l=turkish", ct);
                var parsed = ParseSteam(json, g.SteamAppId);
                if (parsed != null)
                {
                    var shots = new List<string>();
                    var i = 0;
                    foreach (var url in parsed.Screenshots.Take(3))
                    {
                        var file = Path.Combine(Dir, $"{Safe(g.Id)}_s{i++}.jpg");
                        try
                        {
                            if (!File.Exists(file)) await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(url, ct), ct);
                            shots.Add(file);
                        }
                        catch { }
                    }
                    meta = parsed with { Screenshots = shots.ToArray() };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        meta ??= FromEpicCatalog(g);
        if (meta != null)
        {
            try { File.WriteAllText(JsonFile(g), JsonSerializer.Serialize(meta)); } catch { }
        }
        return meta;
    }

    /// <summary>Steam appdetails yanıtı → GameMeta (saf fonksiyon; ekran görüntüleri henüz URL).</summary>
    public static GameMeta? ParseSteam(string json, string appId)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(appId, out var entry)) return null;
        if (!entry.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;
        var d = entry.GetProperty("data");

        string? Text(string prop) => d.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        IEnumerable<string> List(string prop, string inner) =>
            d.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(e => inner == "" ? e.GetString() : e.TryGetProperty(inner, out var x) ? x.GetString() : null).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!)
                : Enumerable.Empty<string>();

        var released = d.TryGetProperty("release_date", out var rd) && rd.TryGetProperty("date", out var dt) ? dt.GetString() : null;
        return new GameMeta(
            Clean(Text("short_description")),
            List("genres", "description").ToArray()!,
            string.IsNullOrWhiteSpace(released) ? null : released,
            List("developers", "").ToArray()!,
            List("screenshots", "path_thumbnail").ToArray()!);
    }

    /// <summary>HTML etiketlerini ve varlıklarını temizler.</summary>
    public static string? Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var t = Regex.Replace(html, "<[^>]+>", " ");
        t = WebUtility.HtmlDecode(t);
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    /// <summary>Epic'in yerel kataloğundan (catcache.bin) açıklama ve geliştirici.</summary>
    static GameMeta? FromEpicCatalog(Game g)
    {
        if (g.Meta == null || !g.Id.StartsWith("epic:")) return null;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Catalog", "catcache.bin");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(Convert.FromBase64String(File.ReadAllText(path)));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("id", out var id) || id.GetString() != g.Meta) continue;
                var desc = e.TryGetProperty("description", out var dv) ? dv.GetString() : null;
                var dev = e.TryGetProperty("developer", out var dd) ? dd.GetString() : null;
                return new GameMeta(Clean(desc), Array.Empty<string>(), null, string.IsNullOrWhiteSpace(dev) ? Array.Empty<string>() : new[] { dev }, Array.Empty<string>());
            }
        }
        catch { }
        return null;
    }
}

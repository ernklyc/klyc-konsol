using System.Text.Json;
using KlycKonsol.Models;

namespace KlycKonsol.Library;

/// <summary>
/// Epic Games Store'un şu an ücretsiz verdiği (ve yakında verecek) oyunlar. Herkese açık, kimlik gerektirmeyen
/// "freeGamesPromotions" uç noktası kullanılır; sonuç 6 saat önbelleğe alınır, kapaklar art klasörüne indirilir.
/// </summary>
public static class EpicFreeService
{
    public sealed record Offer(string Id, string Title, string? Slug, string? ImageUrl, long Starts, long Ends);

    const string Endpoint = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=tr&country=TR&allowCountries=TR";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static string CacheFile => Path.Combine(AppPaths.Dir, "epicfree.json");
    static string ArtDir => Path.Combine(AppPaths.Dir, "art");

    // ---- Saf ayrıştırıcı (test edilebilir) ------------------------------

    /// <summary>Yanıttan şu an ücretsiz olanlar ile yakında ücretsiz olacaklar (%100 indirim) çıkarılır.</summary>
    public static List<Offer> Parse(string json, DateTimeOffset now)
    {
        var offers = new List<Offer>();
        using var doc = JsonDocument.Parse(json);
        var elements = doc.RootElement.GetProperty("data").GetProperty("Catalog").GetProperty("searchStore").GetProperty("elements");

        foreach (var e in elements.EnumerateArray())
        {
            var title = e.TryGetProperty("title", out var t) ? t.GetString() : null;
            var id = e.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(id)) continue;
            if (!e.TryGetProperty("promotions", out var promos) || promos.ValueKind != JsonValueKind.Object) continue;

            foreach (var kind in new[] { "promotionalOffers", "upcomingPromotionalOffers" })
            {
                if (!promos.TryGetProperty(kind, out var groups) || groups.ValueKind != JsonValueKind.Array) continue;
                foreach (var grp in groups.EnumerateArray())
                {
                    if (!grp.TryGetProperty("promotionalOffers", out var list)) continue;
                    foreach (var o in list.EnumerateArray())
                    {
                        // Yalnızca %100 indirim = ücretsiz ("discountPercentage": 0 ⇒ ödenecek yüzde 0)
                        if (!o.TryGetProperty("discountSetting", out var ds) || !ds.TryGetProperty("discountPercentage", out var pct) || pct.GetInt32() != 0) continue;
                        var start = ParseDate(o, "startDate");
                        var end = ParseDate(o, "endDate");
                        if (end != 0 && end < now.ToUnixTimeSeconds()) continue;   // süresi dolmuş
                        offers.Add(new Offer(id, title, Slug(e), Image(e), start, end));
                    }
                }
            }
        }
        return offers.GroupBy(o => o.Id).Select(g => g.OrderBy(o => o.Starts).First()).OrderBy(o => o.Starts).ThenBy(o => o.Title).ToList();
    }

    static long ParseDate(JsonElement o, string prop) =>
        o.TryGetProperty(prop, out var v) && DateTimeOffset.TryParse(v.GetString(), out var d) ? d.ToUnixTimeSeconds() : 0;

    static string? Slug(JsonElement e)
    {
        if (e.TryGetProperty("offerMappings", out var om) && om.ValueKind == JsonValueKind.Array)
            foreach (var m in om.EnumerateArray())
                if (m.TryGetProperty("pageSlug", out var s) && s.GetString() is { Length: > 0 } slug) return slug;
        if (e.TryGetProperty("catalogNs", out var ns) && ns.TryGetProperty("mappings", out var maps) && maps.ValueKind == JsonValueKind.Array)
            foreach (var m in maps.EnumerateArray())
                if (m.TryGetProperty("pageSlug", out var s) && s.GetString() is { Length: > 0 } slug) return slug;
        if (e.TryGetProperty("productSlug", out var ps) && ps.GetString() is { Length: > 0 } p) return p;
        return null;
    }

    static string? Image(JsonElement e)
    {
        if (!e.TryGetProperty("keyImages", out var imgs) || imgs.ValueKind != JsonValueKind.Array) return null;
        string? tall = null, thumb = null, wide = null;
        foreach (var i in imgs.EnumerateArray())
        {
            var type = i.TryGetProperty("type", out var t) ? t.GetString() : null;
            var url = i.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (type == "OfferImageTall") tall = url;
            else if (type == "Thumbnail") thumb = url;
            else if (type == "OfferImageWide") wide = url;
        }
        return tall ?? thumb ?? wide;
    }

    // ---- Ağ + önbellek --------------------------------------------------

    /// <summary>Önbellekten (varsa) hemen; yeni değilse arka planda yenileyip <paramref name="onChanged"/> çağırır.</summary>
    public static List<Game> LoadCached()
    {
        try
        {
            if (!File.Exists(CacheFile)) return new();
            var offers = Parse(File.ReadAllText(CacheFile), DateTimeOffset.UtcNow);
            return ToGames(offers);
        }
        catch { return new(); }
    }

    public static async Task RefreshAsync(Action onChanged, CancellationToken ct)
    {
        try
        {
            if (File.Exists(CacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) < TimeSpan.FromHours(6)) return;
            var json = await Http.GetStringAsync(Endpoint, ct);
            Parse(json, DateTimeOffset.UtcNow);   // geçerliliği doğrula, bozuksa önbelleğe yazma
            Directory.CreateDirectory(AppPaths.Dir);
            await File.WriteAllTextAsync(CacheFile, json, ct);
            System.Windows.Application.Current.Dispatcher.Invoke(onChanged);
        }
        catch { }
    }

    /// <summary>Kapak görsellerini indirir (yoksa); indirileni bağlar.</summary>
    public static async Task EnsureCoversAsync(IEnumerable<Game> offers, CancellationToken ct)
    {
        Directory.CreateDirectory(ArtDir);
        foreach (var g in offers.Where(g => g.Availability == GameAvailability.FreeOffer && g.Cover == null))
        {
            try
            {
                var file = Path.Combine(ArtDir, $"epicfree_{new string(g.Id.Where(char.IsLetterOrDigit).ToArray())}.jpg");
                if (!File.Exists(file) && g.ImageUrl is { } url)
                    await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync(url, ct), ct);
                if (File.Exists(file))
                {
                    var img = GameScanner.LoadImage(file, 460);
                    if (img != null) System.Windows.Application.Current.Dispatcher.Invoke(() => g.Cover = img);
                }
            }
            catch (OperationCanceledException) { return; }
            catch { }
        }
    }

    public static List<Game> ToGames(IEnumerable<Offer> offers) => offers.Select(o =>
    {
        var g = new Game
        {
            Title = o.Title,
            Source = "Epic",
            Kind = EntryKind.Game,
            Id = $"epicfree:{o.Id}",
            Target = o.Slug == null ? null : $"https://store.epicgames.com/tr/p/{o.Slug}",
            StoreUrl = o.Slug == null ? null : $"https://store.epicgames.com/tr/p/{o.Slug}",
            Availability = GameAvailability.FreeOffer,
            OfferStarts = o.Starts,
            OfferEnds = o.Ends,
            Accent = Palette.For(o.Title),
            ImageUrl = o.ImageUrl,
            Support = PadSupport.Unknown,
        };
        var file = Path.Combine(ArtDir, $"epicfree_{new string(g.Id.Where(char.IsLetterOrDigit).ToArray())}.jpg");
        if (File.Exists(file)) g.Cover = GameScanner.LoadImage(file, 460);
        return g;
    }).ToList();
}

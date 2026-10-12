using System.Text;
using System.Text.Json;
using System.Windows;
using KlycKonsol.Models;

namespace KlycKonsol.Library;

/// <summary>
/// Oyunların kol desteğini öğrenir ve state.json'da önbelleğe alır.
/// Kaynak: Steam mağaza verisi (appdetails: controller_support = full/partial ya da kategori 28/18).
/// Steam dışı oyunlar (Epic, kısayollar, Xbox/Store) için Steam mağazasında ada göre eşleşen sürümün bilgisi kullanılır.
/// Kullanıcının elle verdiği değer (bağlam menüsü) her zaman otomatik sonucun önüne geçer.
/// </summary>
public static class ControllerSupportService
{
    static readonly HttpClient Http = CreateClient();
    const int FullCategory = 28, PartialCategory = 18;
    static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(1300);   // Steam: ~200 istek / 5 dk

    /// <summary>Steam'de farklı adla geçen oyunlar (kısayol adı → mağaza adı).</summary>
    static readonly Dictionary<string, string> Aliases = new()
    {
        ["pal"] = "Palworld",
        ["fifa25"] = "EA SPORTS FC 25",
        ["cs2"] = "Counter-Strike 2",
        ["forza4"] = "Forza Horizon 4",
        ["hollowknightsilksong"] = "Hollow Knight: Silksong",
    };

    /// <summary>Steam'de bulunmayan ama bilinen oyunlar (ad → destek).</summary>
    static readonly Dictionary<string, PadSupport> Known = new()
    {
        ["valorant"] = PadSupport.None,              // yalnızca klavye + fare
        ["minecraftlauncher"] = PadSupport.Partial,  // Bedrock/Java sürümleri kol destekler
        ["leagueoflegends"] = PadSupport.None,
        ["legendsofruneterra"] = PadSupport.None,
        ["2xko"] = PadSupport.Full,                  // dövüş oyunu, kol öncelikli
    };

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KLYC-Konsol/" + AppInfo.Version);
        return c;
    }

    static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    public static PadSupport ParseName(string v) => Parse(v);
    static PadSupport Parse(string v) => v switch { "full" => PadSupport.Full, "partial" => PadSupport.Partial, "none" => PadSupport.None, _ => PadSupport.Unknown };
    static string Name(PadSupport s) => s switch { PadSupport.Full => "full", PadSupport.Partial => "partial", _ => "none" };

    /// <summary>Önbellekteki / bilinen / kullanıcı değerlerini anında uygular (internet gerekmez).</summary>
    public static void ApplyCached(IEnumerable<Game> games, AppState state)
    {
        foreach (var g in games.Where(g => g.Kind == EntryKind.Game))
        {
            if (state.SteamIds.TryGetValue(g.Id, out var sid) && g.SteamAppId == null) g.SteamAppId = sid;
            if (g.Support == PadSupport.Unknown && state.Support.TryGetValue(g.Id, out var v)) g.Support = Parse(v);
            if (g.Support == PadSupport.Unknown && Known.TryGetValue(Norm(g.Title), out var k)) g.Support = k;
            if (state.SupportOverride.TryGetValue(g.Id, out var o)) g.Support = Parse(o);   // kullanıcı her zaman son söz
        }
    }

    /// <summary>Doğrulanmamış oyunları sırayla sorgular; her sonuçta <paramref name="onResolved"/> UI iş parçacığında çağrılır.</summary>
    public static async Task ResolveAsync(IEnumerable<Game> games, AppState state, Action<Game> onResolved, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var todo = games.Where(g => g.Kind == EntryKind.Game && g.Support == PadSupport.Unknown).ToList();

        foreach (var g in todo)
        {
            if (ct.IsCancellationRequested) return;
            // Yakın zamanda sorulmuş sonucu tekrar sorma.
            if (state.SupportChecked.TryGetValue(g.Id, out var t) && now - t < 30L * 24 * 3600 && state.Support.ContainsKey(g.Id)) continue;

            try
            {
                var appId = g.SteamAppId;
                if (appId == null)
                {
                    var query = Aliases.TryGetValue(Norm(g.Title), out var alias) ? alias : g.Title;
                    appId = await FindSteamIdByNameAsync(query, ct);
                    await Task.Delay(Gap, ct);
                    if (appId != null) { g.SteamAppId = appId; state.SteamIds[g.Id] = appId; }
                }
                if (appId == null) { Record(g, state, PadSupport.None, now, onResolved); continue; }

                var support = await QueryAsync(appId, ct);
                if (support != null) Record(g, state, support.Value, now, onResolved);
            }
            catch (OperationCanceledException) { return; }
            catch { /* internet yok / geçici hata: bir sonraki açılışta tekrar denenir */ }

            try { await Task.Delay(Gap, ct); } catch (OperationCanceledException) { return; }
        }
    }

    static void Record(Game g, AppState state, PadSupport s, long now, Action<Game> onResolved)
    {
        if (state.SupportOverride.ContainsKey(g.Id)) return;
        state.Support[g.Id] = Name(s);
        state.SupportChecked[g.Id] = now;
        state.Save();
        Application.Current.Dispatcher.Invoke(() => { g.Support = s; onResolved(g); });
    }

    public sealed record SteamBasic(string Name, string Type, PadSupport Support);

    /// <summary>Tek istekle ad + tür + kol desteği (appdetails). Mağazada yoksa Type = "removed".</summary>
    public static async Task<SteamBasic?> QueryBasicAsync(string appId, CancellationToken ct)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic,categories&l=english";
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return ParseBasic(Encoding.UTF8.GetString(bytes), appId);
    }

    /// <summary>appdetails JSON → ad, tür, kol desteği (saf fonksiyon).</summary>
    public static SteamBasic? ParseBasic(string json, string appId)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(appId, out var entry)) return null;
        if (!entry.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return new SteamBasic("", "removed", PadSupport.None);
        var data = entry.GetProperty("data");
        var name = data.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var type = data.TryGetProperty("type", out var t) ? t.GetString() ?? "game" : "game";
        return new SteamBasic(name, type, SupportFrom(data));
    }

    static PadSupport SupportFrom(JsonElement data)
    {
        if (data.TryGetProperty("controller_support", out var cs))
        {
            var v = cs.GetString();
            if (v == "full") return PadSupport.Full;
            if (v == "partial") return PadSupport.Partial;
        }
        if (data.TryGetProperty("categories", out var cats))
        {
            var ids = cats.EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToHashSet();
            if (ids.Contains(FullCategory)) return PadSupport.Full;
            if (ids.Contains(PartialCategory)) return PadSupport.Partial;
        }
        return PadSupport.None;
    }

    public static void RecordSupport(string gameId, PadSupport s, AppState state)
    {
        state.Support[gameId] = Name(s);
        state.SupportChecked[gameId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    static async Task<PadSupport?> QueryAsync(string appId, CancellationToken ct)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic,categories&l=english";
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));

        if (!doc.RootElement.TryGetProperty(appId, out var entry)) return null;
        if (!entry.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return PadSupport.None;   // mağazada yok
        var data = entry.GetProperty("data");

        if (data.TryGetProperty("controller_support", out var cs))
        {
            var v = cs.GetString();
            if (v == "full") return PadSupport.Full;
            if (v == "partial") return PadSupport.Partial;
        }
        if (data.TryGetProperty("categories", out var cats))
        {
            var ids = cats.EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToHashSet();
            if (ids.Contains(FullCategory)) return PadSupport.Full;
            if (ids.Contains(PartialCategory)) return PadSupport.Partial;
        }
        return PadSupport.None;
    }

    /// <summary>Steam dışı bir oyunun Steam karşılığını ada göre bulur.</summary>
    static async Task<string?> FindSteamIdByNameAsync(string title, CancellationToken ct)
    {
        var url = "https://store.steampowered.com/api/storesearch/?cc=us&l=english&term=" + Uri.EscapeDataString(title);
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("items", out var items)) return null;

        var want = Norm(title);
        string? partial = null;
        foreach (var it in items.EnumerateArray())
        {
            if (it.GetProperty("type").GetString() != "app") continue;
            var name = Norm(it.GetProperty("name").GetString() ?? "");
            var id = it.GetProperty("id").GetInt32().ToString();
            if (name == want) return id;
            if (partial == null && want.Length >= 4 && (name.StartsWith(want) || want.StartsWith(name))) partial = id;   // ör. "... Enhanced"
        }
        return partial;
    }
}

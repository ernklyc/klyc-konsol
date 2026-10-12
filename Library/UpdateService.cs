using System.Text.Json;

namespace KlycKonsol.Library;

public sealed record UpdateInfo(string Tag, Version Version, string Url, string Notes);

/// <summary>
/// Güncelleme denetimi: GitHub'daki en son kararlı sürümü sorar (günde en fazla bir kez, elle istenirse hemen).
/// Hiçbir şey indirmez/kurmaz — "İndir" yalnızca sürüm sayfasını Konsol tarayıcısında açar. Ağ yoksa ya da depo gizliyse sessizce geçer.
/// </summary>
public static class UpdateService
{
    public const string Repo = "ernklyc/klyc-konsol";
    const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KLYC-Konsol/" + AppInfo.Version);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static Version Current { get; } = Version.TryParse(AppInfo.Version, out var v) ? v : new Version(0, 0, 0);

    /// <summary>"v1.2.3" / "1.2.3-beta" → Version; ayrıştırılamazsa null.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().TrimStart('v', 'V');
        var dash = t.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) t = t[..dash];
        return Version.TryParse(t, out var v) ? v : null;
    }

    /// <summary>GitHub "releases/latest" yanıtından bilgi; taslak / ön sürüm ya da çözülemeyen etiket → null.</summary>
    public static UpdateInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (r.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var ver = ParseTag(tag);
        if (tag == null || ver == null) return null;
        var url = r.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        var notes = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new UpdateInfo(tag, ver, url, notes);
    }

    public static bool IsNewer(UpdateInfo info) => info.Version > Current;

    /// <summary>Başarıda (bilgi, null); hatada (null, hata metni). Yayın yoksa (404) "henüz yayın yok".</summary>
    public static async Task<(UpdateInfo? Info, string? Error)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(Api, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, "Henüz yayınlanmış sürüm yok");
            if (!resp.IsSuccessStatusCode) return (null, $"Sunucu yanıtı: {(int)resp.StatusCode}");
            return (ParseRelease(await resp.Content.ReadAsStringAsync(ct)), null);
        }
        catch (OperationCanceledException) { throw; }
        catch { return (null, "Bağlantı kurulamadı"); }
    }
}

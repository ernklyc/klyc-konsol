namespace KlycKonsol.Library;

/// <summary>Tarayıcı sekmesi için özel kipler. YouTube TV (youtube.com/tv), televizyon arayüzünü yalnızca TV/konsol kimliği (User-Agent) gönderilirse sunar.</summary>
public static class BrowserModes
{
    public const string YouTubeTvUrl = "https://www.youtube.com/tv";

    /// <summary>YouTube TV'nin kabul ettiği konsol kimliği (PlayStation 5 tarayıcısı) — sunucu tarafında doğrulandı: tam arayüz gelir.</summary>
    public const string TvUserAgent = "Mozilla/5.0 (PlayStation; PlayStation 5/2.26) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.0 Safari/605.1.15";

    /// <summary>Adres YouTube TV arayüzü mü (youtube.com/tv ve altı; "/tvxyz" gibi başka yollar hariç).</summary>
    public static bool IsYouTubeTv(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        if (host != "youtube.com" && host != "www.youtube.com" && host != "m.youtube.com") return false;
        var p = u.AbsolutePath;
        return p.Equals("/tv", StringComparison.OrdinalIgnoreCase) || p.StartsWith("/tv/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>TV kimliğiyle açık sekmede kalınması gereken adresler: YouTube'un kendi alanları ve oturum açma (Google) yönlendirmeleri.</summary>
    public static bool StaysInTv(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var h = u.Host.ToLowerInvariant();
        return h == "youtube.com" || h.EndsWith(".youtube.com") || h == "google.com" || h.EndsWith(".google.com") || h.EndsWith(".googlevideo.com") || h.EndsWith(".ytimg.com") || h.EndsWith(".gstatic.com");
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using KlycKonsol.Models;

namespace KlycKonsol.Library.Stores;

/// <summary>
/// Riot Games (VALORANT, League of Legends, Legends of Runeterra, 2XKO).
/// Kurulu ürünler ProgramData\Riot Games\Metadata\&lt;ürün&gt;.live\*.product_settings.yaml dosyasında listelenir;
/// başlatma: RiotClientServices.exe --launch-product=&lt;ürün&gt; --launch-patchline=live.
/// </summary>
public static class RiotScanner
{
    static readonly Dictionary<string, string> Products = new()
    {
        ["valorant"] = "VALORANT",
        ["league_of_legends"] = "League of Legends",
        ["bacon"] = "Legends of Runeterra",
        ["lion"] = "2XKO",
    };

    public static List<Game> Scan() => Scan(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games"));

    public static List<Game> Scan(string riotProgramData)
    {
        var games = new List<Game>();
        var client = FindClient(riotProgramData);
        if (client == null) return games;

        var meta = Path.Combine(riotProgramData, "Metadata");
        if (!Directory.Exists(meta)) return games;

        foreach (var dir in Directory.EnumerateDirectories(meta))
        {
            var folder = Path.GetFileName(dir);                 // ör. valorant.live
            if (!folder.EndsWith(".live", StringComparison.OrdinalIgnoreCase)) continue;   // .pbe / game_patch dışarıda
            var product = folder[..^5];
            if (!Products.TryGetValue(product, out var title)) continue;

            var yaml = Path.Combine(dir, folder + ".product_settings.yaml");
            if (!File.Exists(yaml)) continue;
            var install = ParseInstallPath(File.ReadAllText(yaml));
            if (install == null || !Directory.Exists(install)) continue;   // metadata kalmış ama oyun kurulu değil

            var ico = Path.Combine(dir, folder + ".ico");
            var icon = File.Exists(ico) ? GameScanner.LoadImage(ico, 460) : null;

            games.Add(new Game
            {
                Title = title,
                Source = "Riot",
                Target = client,
                Args = $"--launch-product={product} --launch-patchline=live",
                Id = $"riot:{product}",
                InstallDir = install,
                Cover = icon,
                IconCover = icon != null,
                Accent = Palette.For(title),
            });
        }
        return games;
    }

    /// <summary>RiotClientInstalls.json içinden RiotClientServices.exe yolu.</summary>
    static string? FindClient(string riotProgramData)
    {
        try
        {
            var f = Path.Combine(riotProgramData, "RiotClientInstalls.json");
            if (!File.Exists(f)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            foreach (var key in new[] { "rc_live", "rc_default" })
            {
                if (doc.RootElement.TryGetProperty(key, out var v) && v.GetString() is { Length: > 0 } p)
                {
                    p = p.Replace('/', '\\');
                    if (File.Exists(p)) return p;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>product_settings.yaml içindeki product_install_full_path değeri (yalnızca bu satır gerekli: tam YAML ayrıştırıcıya gerek yok).</summary>
    public static string? ParseInstallPath(string yaml)
    {
        var m = Regex.Match(yaml, @"^product_install_full_path:\s*""?([^""\r\n]+?)""?\s*$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim().Replace('/', '\\') : null;
    }
}

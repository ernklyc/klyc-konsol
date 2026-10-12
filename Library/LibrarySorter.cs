using KlycKonsol.Models;

namespace KlycKonsol.Library;

/// <summary>Kütüphane sıralaması: "smart" (favoriler, sonra son oynananlar, sonra ada göre — varsayılan), "recent", "name", "playtime".</summary>
public static class LibrarySorter
{
    public static readonly string[] Modes = { "smart", "recent", "name", "playtime" };

    public static string Next(string? mode)
    {
        var i = Array.IndexOf(Modes, mode ?? "smart");
        return Modes[(i + 1) % Modes.Length];
    }

    public static string Label(string? mode) => mode switch
    {
        "recent" => "Son oynanan",
        "name" => "Ada göre (A-Z)",
        "playtime" => "En çok oynanan",
        _ => "Akıllı (favoriler, son oynananlar)",
    };

    public static IEnumerable<Game> Order(IEnumerable<Game> games, string? mode)
    {
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        return mode switch
        {
            "recent" => games.OrderByDescending(g => g.LastPlayed).ThenBy(g => g.Title, cmp),
            "name" => games.OrderBy(g => g.Title, cmp),
            "playtime" => games.OrderByDescending(g => g.PlaySeconds).ThenByDescending(g => g.LastPlayed).ThenBy(g => g.Title, cmp),
            _ => games.OrderByDescending(g => g.IsFavorite).ThenByDescending(g => g.LastPlayed).ThenBy(g => g.Title, cmp),
        };
    }
}

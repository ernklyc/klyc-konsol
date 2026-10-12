using KlycKonsol.Library;
using KlycKonsol.Models;
using Xunit;

namespace KlycKonsol.Tests;

public class LibrarySorterTests
{
    static Game G(string title, long last = 0, long secs = 0, bool fav = false) =>
        new() { Title = title, Id = "t:" + title, LastPlayed = last, PlaySeconds = secs, IsFavorite = fav };

    static string[] Titles(IEnumerable<Game> g) => g.Select(x => x.Title).ToArray();

    readonly List<Game> _games = new()
    {
        G("Cuphead", last: 100, secs: 5000),
        G("Alan Wake", last: 300, secs: 100),
        G("Hades", last: 200, secs: 9000, fav: true),
        G("Balatro"),
    };

    [Fact] public void Smart_FavoritesFirst_ThenRecent_ThenName() =>
        Assert.Equal(new[] { "Hades", "Alan Wake", "Cuphead", "Balatro" }, Titles(LibrarySorter.Order(_games, "smart")));

    [Fact] public void Unknown_FallsBackToSmart() =>
        Assert.Equal(Titles(LibrarySorter.Order(_games, "smart")), Titles(LibrarySorter.Order(_games, "bilinmeyen")));

    [Fact] public void Recent_IgnoresFavorites() =>
        Assert.Equal(new[] { "Alan Wake", "Hades", "Cuphead", "Balatro" }, Titles(LibrarySorter.Order(_games, "recent")));

    [Fact] public void Name_IsAlphabetical() =>
        Assert.Equal(new[] { "Alan Wake", "Balatro", "Cuphead", "Hades" }, Titles(LibrarySorter.Order(_games, "name")));

    [Fact] public void Playtime_MostFirst() =>
        Assert.Equal(new[] { "Hades", "Cuphead", "Alan Wake", "Balatro" }, Titles(LibrarySorter.Order(_games, "playtime")));

    [Fact] public void Next_CyclesThroughAllModesAndWraps()
    {
        var m = "smart"; var seen = new List<string>();
        for (var i = 0; i < LibrarySorter.Modes.Length; i++) { seen.Add(m); m = LibrarySorter.Next(m); }
        Assert.Equal("smart", m);
        Assert.Equal(LibrarySorter.Modes.Length, seen.Distinct().Count());
    }
}

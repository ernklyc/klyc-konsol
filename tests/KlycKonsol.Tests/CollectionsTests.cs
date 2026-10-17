using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

public class CollectionsTests
{
    [Theory]
    [InlineData("  Co-op  ", "Co-op")]
    [InlineData("Çocuklar   için", "Çocuklar için")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CleanName_TrimsAndRejectsEmpty(string? raw, string? expected) => Assert.Equal(expected, Collections.CleanName(raw));

    [Fact]
    public void CleanName_LimitsLength_AndRejectsControlChars()
    {
        Assert.Equal(Collections.MaxNameLength, Collections.CleanName(new string('a', 50))!.Length);
        Assert.Null(Collections.CleanName("a\tb"));
    }

    [Fact]
    public void Toggle_CreatesAddsAndRemoves_AndDropsEmptyCollection()
    {
        var all = new List<GameCollection>();
        Assert.Equal((true, "Co-op"), Collections.Toggle(all, " Co-op ", "steam:1"));
        Assert.Equal((true, "Co-op"), Collections.Toggle(all, "CO-OP", "steam:2"));   // büyük/küçük harf aynı koleksiyon
        Assert.Single(all);
        Assert.Equal(new[] { "steam:1", "steam:2" }, all[0].Ids);
        Assert.True(Collections.Contains(all, "co-op", "steam:2"));
        Assert.Equal((false, "Co-op"), Collections.Toggle(all, "Co-op", "steam:1"));
        Assert.Equal((false, "Co-op"), Collections.Toggle(all, "Co-op", "steam:2"));
        Assert.Empty(all);                                                              // boşalan koleksiyon kaldırılır
    }

    [Fact]
    public void Toggle_RespectsMaxCollections_AndBadInput()
    {
        var all = new List<GameCollection>();
        for (var i = 0; i < Collections.MaxCollections; i++) Assert.NotNull(Collections.Toggle(all, "K" + i, "g"));
        Assert.Null(Collections.Toggle(all, "Dokuzuncu", "g"));
        Assert.NotNull(Collections.Toggle(all, "K0", "h"));                              // var olana ekleme sınırdan etkilenmez
        Assert.Null(Collections.Toggle(all, "  ", "g"));
        Assert.Null(Collections.Toggle(all, "K1", ""));
        Assert.Equal(Collections.MaxCollections, all.Count);
    }

    [Fact]
    public void Members_KeepsCollectionOrder_AndIgnoresMissingGames()
    {
        var c = new GameCollection { Name = "x", Ids = { "b", "gone", "a" } };
        var games = new[] { ("a", 1), ("b", 2), ("c", 3) };
        Assert.Equal(new[] { 2, 1 }, Collections.Members(c, games, g => g.Item1).Select(g => g.Item2).ToArray());
        Assert.Empty(Collections.Members(null, games, g => g.Item1));
    }

    [Fact]
    public void AppState_RoundTripsCollections_AndOldStateHasNone()
    {
        var s = new AppState();
        Collections.Toggle(s.Collections, "Co-op", "steam:1");
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppState>(json)!;
        Assert.Equal("Co-op", back.Collections[0].Name);
        Assert.Empty(System.Text.Json.JsonSerializer.Deserialize<AppState>("{}")!.Collections);
    }
}

using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

public class GameMetaTests
{
    static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", n));

    [Fact]
    public void ParseSteam_RealHadesResponse()
    {
        var m = GameMetaService.ParseSteam(Fixture("hades.appdetails.tr.json"), "1145360");
        Assert.NotNull(m);
        Assert.Contains("god of the dead", m!.Description);
        Assert.Contains("Aksiyon", m.Genres);
        Assert.Equal("17 Eyl 2020", m.Released);
        Assert.Equal(new[] { "Supergiant Games" }, m.Developers);
        Assert.Equal(11, m.Screenshots.Length);
        Assert.All(m.Screenshots, s => Assert.StartsWith("https://", s));
    }

    [Fact]
    public void ParseSteam_ReturnsNullWhenNotFound()
    {
        Assert.Null(GameMetaService.ParseSteam("{\"1\":{\"success\":false}}", "1"));
        Assert.Null(GameMetaService.ParseSteam("{}", "1"));
    }

    [Theory]
    [InlineData("<b>Merhaba</b>  d&uuml;nya &amp; <i>ay</i>", "Merhaba dünya & ay")]
    [InlineData("  \n a \t b  ", "a b")]
    public void Clean_StripsHtmlAndDecodes(string input, string expected) =>
        Assert.Equal(expected, GameMetaService.Clean(input));

    [Fact]
    public void Clean_EmptyBecomesNull() => Assert.Null(GameMetaService.Clean("  "));
}

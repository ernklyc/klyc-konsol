using KlycKonsol.Library;
using KlycKonsol.Models;
using Xunit;

namespace KlycKonsol.Tests;

public class EpicFreeTests
{
    static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "epic.freegames.json"));
    static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_KeepsOnlyFullDiscountOffers_CurrentAndUpcoming()
    {
        var offers = EpicFreeService.Parse(Json, Now);
        var titles = offers.Select(o => o.Title).ToList();
        Assert.Contains("Out of Sight", titles);
        Assert.Contains("TerraScape", titles);
        Assert.Contains("Bad Cheese", titles);          // yakında ücretsiz
        Assert.DoesNotContain("Ghostrunner 2", titles); // %20 indirim, ücretsiz değil
        Assert.DoesNotContain("LISA: Definitive Edition", titles);
        Assert.DoesNotContain("Luftrausers", titles);   // promosyonu yok
    }

    [Fact]
    public void Parse_FillsSlugImageAndDates_AndSortsCurrentBeforeUpcoming()
    {
        var offers = EpicFreeService.Parse(Json, Now);
        var cur = offers.First(o => o.Title == "Out of Sight");
        Assert.False(string.IsNullOrEmpty(cur.Slug));
        Assert.StartsWith("http", cur.ImageUrl);
        Assert.True(cur.Ends > cur.Starts);
        Assert.True(offers.ToList().FindIndex(o => o.Title == "Out of Sight") < offers.ToList().FindIndex(o => o.Title == "Bad Cheese"));
    }

    [Fact]
    public void Parse_DropsExpiredOffers()
    {
        var later = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var titles = EpicFreeService.Parse(Json, later).Select(o => o.Title).ToList();
        Assert.DoesNotContain("Out of Sight", titles);
        Assert.Contains("Bad Cheese", titles);
    }

    [Fact]
    public void ToGames_BuildsFreeOfferEntriesWithStoreUrl()
    {
        var games = EpicFreeService.ToGames(EpicFreeService.Parse(Json, Now));
        var g = games.First(x => x.Title == "Out of Sight");
        Assert.Equal(GameAvailability.FreeOffer, g.Availability);
        Assert.StartsWith("https://store.epicgames.com/tr/p/", g.StoreUrl);
        Assert.StartsWith("epicfree:", g.Id);
        Assert.Equal("ÜCRETSİZ", g.BadgeText);
    }
}

public class SteamOwnedTests
{
    [Fact]
    public void BuildFromCache_SkipsInstalledAndNonGames_AndAppliesUsageAndSupport()
    {
        var state = new AppState();
        state.SteamOwned["100"] = new OwnedInfo("Kurulu Oyun", "game");
        state.SteamOwned["200"] = new OwnedInfo("Silinmiş Oyun", "game");
        state.SteamOwned["300"] = new OwnedInfo("Bir DLC", "dlc");
        state.Support["steamowned:200"] = "full";
        var installed = new[] { new Game { Title = "Kurulu Oyun", Id = "steam:100", SteamAppId = "100" } };
        var usage = new Dictionary<string, SteamLocal.AppUsage> { ["200"] = new("200", 125, 1700000000) };

        var list = SteamOwnedService.BuildFromCache(state, installed, usage);

        var g = Assert.Single(list);
        Assert.Equal("Silinmiş Oyun", g.Title);
        Assert.Equal("steam://install/200", g.Target);
        Assert.Equal(GameAvailability.NotInstalled, g.Availability);
        Assert.Equal(PadSupport.Full, g.Support);
        Assert.Equal(125 * 60, g.PlaySeconds);
        Assert.Equal("İNDİR", g.BadgeText);
    }
}

public class UiSoundTests
{
    [Fact]
    public void Wav_ProducesValidRiffHeaderAndExpectedLength()
    {
        var wav = KlycKonsol.Input.UiSound.Wav((1000, 100), (500, 50));
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        var samples = 22050 * 100 / 1000 + 22050 * 50 / 1000;
        Assert.Equal(44 + samples * 2, wav.Length);
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
    }

    [Fact]
    public void Wav_StartsAndEndsSilent_SoThereAreNoClicks()
    {
        var wav = KlycKonsol.Input.UiSound.Wav(0.5, (1000, 60));
        Assert.Equal(0, BitConverter.ToInt16(wav, 44));
        Assert.True(Math.Abs((int)BitConverter.ToInt16(wav, wav.Length - 2)) < 1500);
    }
}

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v0.6.0", "0.6.0")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V2.0.1-beta.1", "2.0.1")]
    public void ParseTag_HandlesPrefixAndSuffix(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateService.ParseTag(tag));

    [Fact]
    public void ParseTag_ReturnsNullForGarbage()
    {
        Assert.Null(UpdateService.ParseTag("nightly"));
        Assert.Null(UpdateService.ParseTag(null));
    }

    [Fact]
    public void ParseRelease_ReadsStableReleaseAndSkipsDraftsAndPrereleases()
    {
        var ok = UpdateService.ParseRelease("""{"tag_name":"v9.9.9","html_url":"https://github.com/x/y/releases/tag/v9.9.9","body":"notlar","draft":false,"prerelease":false}""")!;
        Assert.Equal("v9.9.9", ok.Tag);
        Assert.True(UpdateService.IsNewer(ok));
        Assert.Null(UpdateService.ParseRelease("""{"tag_name":"v9.9.9","draft":true}"""));
        Assert.Null(UpdateService.ParseRelease("""{"tag_name":"v9.9.9","prerelease":true}"""));
    }

    [Fact]
    public void IsNewer_FalseForCurrentOrOlder()
    {
        Assert.False(UpdateService.IsNewer(new UpdateInfo("v0.0.1", new Version(0, 0, 1), "", "")));
        Assert.False(UpdateService.IsNewer(new UpdateInfo("cur", UpdateService.Current, "", "")));
    }
}

public class BluetoothTests
{
    [Fact]
    public void Order_DropsUnnamedAndDuplicates_ConnectedThenPairedFirst()
    {
        var list = new[]
        {
            new KlycKonsol.Input.BtDevice("a", "Kulaklık", true, false, true, -60),
            new KlycKonsol.Input.BtDevice("b", "Kol", true, true, true, -70),
            new KlycKonsol.Input.BtDevice("c", "Yeni Hoparlör", false, false, true, -40),
            new KlycKonsol.Input.BtDevice("d", "d", false, false, true, -30),   // adı kimliğine eşit = adsız
            new KlycKonsol.Input.BtDevice("e", "", false, false, true, -20),
            new KlycKonsol.Input.BtDevice("b", "Kol (kopya)", true, true, true, -70),
        };
        var ordered = KlycKonsol.Input.BluetoothService.Order(list);
        Assert.Equal(new[] { "Kol", "Kulaklık", "Yeni Hoparlör" }, ordered.Select(x => x.Name));
    }

    [Fact]
    public void StatusText_IsTurkishAndCoversCommonResults()
    {
        Assert.Equal("Eşleşti", KlycKonsol.Input.BluetoothService.StatusText(Windows.Devices.Enumeration.DevicePairingResultStatus.Paired));
        Assert.Contains("PIN", KlycKonsol.Input.BluetoothService.StatusText(Windows.Devices.Enumeration.DevicePairingResultStatus.AuthenticationFailure));
    }
}

public class GuardTests
{
    [Fact]
    public void ParseArgs_NoGuardFlag_IsNone() =>
        Assert.Equal(KlycKonsol.Input.GuardStart.None, KlycKonsol.Input.GuardLauncher.ParseArgs(new[] { "KlycKonsol.exe", "--debugcmd" }).Kind);

    [Fact]
    public void ParseArgs_Resume_ReadsFirstSeen()
    {
        var a = KlycKonsol.Input.GuardLauncher.ParseArgs(new[] { "k.exe", "--from-guard", "--firstseen", "1700000000" });
        Assert.Equal(KlycKonsol.Input.GuardStart.Resume, a.Kind);
        Assert.Equal(1700000000, a.FirstSeen);
    }

    [Fact]
    public void ParseArgs_GameExited_ReadsGamePlayedAndSilent()
    {
        var a = KlycKonsol.Input.GuardLauncher.ParseArgs(new[] { "k.exe", "--from-guard", "--game-exited", "--game", "epic:abc", "--played", "321", "--silent" });
        Assert.Equal(KlycKonsol.Input.GuardStart.GameExited, a.Kind);
        Assert.Equal("epic:abc", a.GameId);
        Assert.Equal(321, a.PlayedSeconds);
        Assert.True(a.Silent);
    }

    [Fact]
    public void BuildArguments_QuotesPathsAndStripsQuotes()
    {
        var info = new KlycKonsol.Input.GuardInfo("steam:1", @"D:\Games\My Game", 1700000000, false, 1700000100);
        var s = KlycKonsol.Input.GuardLauncher.BuildArguments(@"C:\Program Files\K\KlycKonsol.exe", info);
        Assert.Contains(@"--konsol ""C:\Program Files\K\KlycKonsol.exe""", s);
        Assert.Contains(@"--dir ""D:\Games\My Game""", s);
        Assert.Contains("--autoreturn 0", s);
        Assert.Contains("--firstseen 1700000100", s);
        Assert.DoesNotContain("--dir \"\"", KlycKonsol.Input.GuardLauncher.BuildArguments("k.exe", null));
    }

    [Fact]
    public void RecordPlay_IgnoresShortSessionsAndSteam()
    {
        var st = new KlycKonsol.Library.AppState();
        Assert.Equal(0, KlycKonsol.Input.SessionFinisher.RecordPlay(st, "epic:x", 10));
        Assert.Equal(0, KlycKonsol.Input.SessionFinisher.RecordPlay(st, "steam:5", 500));
        Assert.Equal(500, KlycKonsol.Input.SessionFinisher.RecordPlay(st, "epic:x", 500));
        Assert.Equal(800, KlycKonsol.Input.SessionFinisher.RecordPlay(st, "epic:x", 300));
    }
}

using KlycKonsol.Library;
using KlycKonsol.Library.Stores;
using KlycKonsol.Models;
using Xunit;

namespace KlycKonsol.Tests;

public class StoreScannerTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "klyc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- Xbox -----------------------------------------------------------

    [Fact]
    public void Xbox_ParsesRealSilksongManifest()
    {
        var info = XboxScanner.Parse(Fixture("silksong.appxmanifest.xml"), Fixture("silksong.MicrosoftGame.config"), "Hollow Knight- Silksong");
        Assert.NotNull(info);
        Assert.Equal("TeamCherry.HollowKnightSilksong", info!.IdentityName);
        Assert.Equal("Hollow.Knight.Silksong", info.AppId);
        Assert.Equal("Hollow Knight: Silksong", info.Title);
        Assert.Equal("Square480x480Logo.png", info.LogoRelPath);
    }

    [Fact]
    public void Xbox_ParsesMinecraftWithoutShellVisuals_UsesManifestLogo()
    {
        var info = XboxScanner.Parse(Fixture("minecraft.appxmanifest.xml"), Fixture("minecraft.MicrosoftGame.config"), "Minecraft Launcher");
        Assert.NotNull(info);
        Assert.Equal("Microsoft.4297127D64EC6", info!.IdentityName);
        Assert.Equal("Minecraft", info.AppId);
        Assert.False(string.IsNullOrWhiteSpace(info.Title));
        Assert.DoesNotContain("ms-resource", info.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xbox_ScanBuildsLaunchTargetFromFamilyName()
    {
        var root = NewTempDir();
        try
        {
            var content = Path.Combine(root, "XboxGames", "Hollow Knight- Silksong", "Content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "appxmanifest.xml"), Fixture("silksong.appxmanifest.xml"));
            File.WriteAllText(Path.Combine(content, "MicrosoftGame.Config"), Fixture("silksong.MicrosoftGame.config"));

            var games = XboxScanner.Scan(new[] { root }, id => id == "TeamCherry.HollowKnightSilksong" ? "TeamCherry.HollowKnightSilksong_y4jvztpgccj42" : null);

            var g = Assert.Single(games);
            Assert.Equal("xbox:TeamCherry.HollowKnightSilksong", g.Id);
            Assert.Equal(@"shell:AppsFolder\TeamCherry.HollowKnightSilksong_y4jvztpgccj42!Hollow.Knight.Silksong", g.Target);
            Assert.Equal(content, g.InstallDir);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Xbox_SkipsPackagesNotRegistered()
    {
        var root = NewTempDir();
        try
        {
            var content = Path.Combine(root, "XboxGames", "Ghost", "Content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "appxmanifest.xml"), Fixture("silksong.appxmanifest.xml"));
            Assert.Empty(XboxScanner.Scan(new[] { root }, _ => null));
        }
        finally { Directory.Delete(root, true); }
    }

    // ---- Riot -----------------------------------------------------------

    [Fact]
    public void Riot_ParsesInstallPathFromRealYaml()
    {
        Assert.Equal(@"C:\Riot Games\VALORANT\live", RiotScanner.ParseInstallPath(Fixture("valorant.product_settings.yaml")));
    }

    [Fact]
    public void Riot_ScanReturnsOnlyInstalledProductsWithLaunchArgs()
    {
        var pd = NewTempDir();
        var install = Path.Combine(pd, "installed-valorant");
        try
        {
            Directory.CreateDirectory(install);
            var client = Path.Combine(pd, "RiotClientServices.exe");
            File.WriteAllText(client, "");
            File.WriteAllText(Path.Combine(pd, "RiotClientInstalls.json"), "{\"rc_live\":\"" + client.Replace("\\", "/") + "\"}");

            void Product(string folder, string path)
            {
                var d = Path.Combine(pd, "Metadata", folder);
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, folder + ".product_settings.yaml"), "product_install_full_path: \"" + path.Replace("\\", "/") + "\"\n");
            }
            Product("valorant.live", install);
            Product("league_of_legends.live", Path.Combine(pd, "not-installed"));   // metadata var, oyun yok
            Product("valorant.pbe", install);                                       // .pbe yok sayılır

            var games = RiotScanner.Scan(pd);
            var g = Assert.Single(games);
            Assert.Equal("riot:valorant", g.Id);
            Assert.Equal("VALORANT", g.Title);
            Assert.Equal(client, g.Target);
            Assert.Equal("--launch-product=valorant --launch-patchline=live", g.Args);
        }
        finally { Directory.Delete(pd, true); }
    }

    // ---- EA -------------------------------------------------------------

    [Fact]
    public void Ea_ParsesRealInstallerData()
    {
        var info = EaScanner.Parse(Fixture("bf1.installerdata.xml"));
        Assert.NotNull(info);
        Assert.Contains("Battlefield", info!.Title);
        Assert.Equal("1026023", info.ContentId);
    }

    [Fact]
    public void Ea_ResolveExe_FindsRelativeAndByName()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "game.exe"), "");
            Assert.Equal(Path.Combine(dir, "game.exe"), EaScanner.ResolveExe("game.exe", dir));

            Directory.CreateDirectory(Path.Combine(dir, "Bin", "x64"));
            File.WriteAllText(Path.Combine(dir, "Bin", "x64", "deep.exe"), "");
            Assert.EndsWith("deep.exe", EaScanner.ResolveExe(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Nope\Key\Install Dir]\Bin\x64\deep.exe", dir));
            Assert.Null(EaScanner.ResolveExe("missing.exe", dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Ea_Scan_UsesInstallerDataFromRoots()
    {
        var root = NewTempDir();
        try
        {
            var game = Path.Combine(root, "Test Game");
            Directory.CreateDirectory(Path.Combine(game, "__Installer"));
            File.WriteAllText(Path.Combine(game, "tg.exe"), "");
            File.WriteAllText(Path.Combine(game, "__Installer", "installerdata.xml"),
                "<DiPManifest><contentIDs><contentID>OFB-EAST:123</contentID></contentIDs><gameTitles><gameTitle locale=\"en_US\">Test Game™</gameTitle></gameTitles>" +
                "<runtime><launcher><filePath>tg.exe</filePath></launcher></runtime></DiPManifest>");

            var g = Assert.Single(EaScanner.Scan(new[] { root }));
            Assert.Equal("Test Game", g.Title);
            Assert.Equal(Path.Combine(game, "tg.exe"), g.Target);
            Assert.Equal("ea:testgame", g.Id);
        }
        finally { Directory.Delete(root, true); }
    }

    // ---- GOG / Ubisoft / Battle.net (kayıt defteri sahte verilerle) -----

    [Fact]
    public void Gog_FromValuesAndLaunchSelection()
    {
        var v = new Dictionary<string, string?> { ["gameID"] = "1207658924", ["gameName"] = "Cyberpunk", ["path"] = @"D:\GOG\Cyberpunk", ["exe"] = @"D:\GOG\Cyberpunk\bin\game.exe", ["launchParam"] = "-x" };
        var info = GogScanner.FromValues(v)!;
        Assert.Equal("1207658924", info.GameId);

        var direct = GogScanner.ToGames(new[] { info }, f => true, @"C:\Galaxy\GalaxyClient.exe").Single();
        Assert.Equal(@"D:\GOG\Cyberpunk\bin\game.exe", direct.Target);
        Assert.Equal("-x", direct.Args);

        var viaGalaxy = GogScanner.ToGames(new[] { info }, f => f.EndsWith("GalaxyClient.exe") , @"C:\Galaxy\GalaxyClient.exe").Single();
        Assert.Equal(@"C:\Galaxy\GalaxyClient.exe", viaGalaxy.Target);
        Assert.Equal("/command=runGame /gameId=1207658924 /path=\"D:\\GOG\\Cyberpunk\"", viaGalaxy.Args);

        Assert.Null(GogScanner.FromValues(new Dictionary<string, string?> { ["gameName"] = "x" }));
    }

    [Fact]
    public void Ubisoft_ToGamesBuildsUplayLaunchAndSkipsMissingDirs()
    {
        var infos = new[]
        {
            new UbisoftScanner.UbiInfo("720", @"C:\Ubi\Assassins Creed Odyssey/", "Assassin's Creed® Odyssey"),
            new UbisoftScanner.UbiInfo("999", @"C:\Ubi\Gone", null),
        };
        var games = UbisoftScanner.ToGames(infos, d => d.Contains("Odyssey"));
        var g = Assert.Single(games);
        Assert.Equal("uplay://launch/720/0", g.Target);
        Assert.Equal("Assassin's Creed Odyssey", g.Title);
        Assert.Equal("ubi:720", g.Id);
    }

    [Fact]
    public void BattleNet_MapsKnownGamesAndIgnoresClient()
    {
        var infos = new[]
        {
            new BattleNetScanner.BnetInfo("Overwatch 2", @"C:\Games\Overwatch", null),
            new BattleNetScanner.BnetInfo("Battle.net", @"C:\Program Files\Battle.net", null),
            new BattleNetScanner.BnetInfo("Unknown Blizz Game", @"C:\Games\Unk", @"C:\Games\Unk\unk.exe,0"),
            new BattleNetScanner.BnetInfo("Diablo IV", @"C:\Gone", null),
        };
        var games = BattleNetScanner.ToGames(infos, f => f.EndsWith("unk.exe"), d => !d.EndsWith("Gone"));
        Assert.Equal(2, games.Count);
        Assert.Equal("battlenet://Pro", games.Single(g => g.Title == "Overwatch 2").Target);
        Assert.Equal(@"C:\Games\Unk\unk.exe", games.Single(g => g.Title == "Unknown Blizz Game").Target);
    }

    // ---- Tekilleştirme --------------------------------------------------

    static Game G(string id, string title) => new() { Id = id, Title = title };

    [Fact]
    public void Dedupe_PrefersBetterStoreAndResolvesAliases()
    {
        var list = new List<Game>
        {
            G("lnk:pal", "Pal"),
            G("xbox:Pocketpair.Palworld", "Palworld"),
            G("lnk:fifa25", "FIFA 25"),
            G("steam:2669320", "EA SPORTS FC™ 25"),
            G("epic:abc", "Grand Theft Auto V"),
            G("lnk:gta5", "GTA5"),
            G("steam:431730", "Aseprite"),
            G("steam:431730", "Aseprite"),     // aynı kimlik iki kez
        };
        var result = GameScanner.Dedupe(list);
        Assert.Equal(4, result.Count);
        Assert.Contains(result, g => g.Id == "xbox:Pocketpair.Palworld");
        Assert.Contains(result, g => g.Id == "steam:2669320");
        Assert.Contains(result, g => g.Id == "epic:abc");
        Assert.DoesNotContain(result, g => g.Id.StartsWith("lnk:"));
    }

    [Theory]
    [InlineData("EA SPORTS FC™ 25", "easportsfc25")]
    [InlineData("Hollow Knight: Silksong", "hollowknightsilksong")]
    [InlineData("Pal", "palworld")]
    public void CanonicalTitle_NormalizesAndAliases(string input, string expected) =>
        Assert.Equal(expected, StoreUtil.CanonicalTitle(input));
}

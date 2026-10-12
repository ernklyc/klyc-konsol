using System.Text.Json;
using KlycKonsol.Input;
using KlycKonsol.Library;
using KlycKonsol.Models;
using Xunit;

namespace KlycKonsol.Tests;

/// <summary>KLYC Pulse bağlantısı: gerçek veriye dokunmaz (KLYC_DATA_DIR TestIsolation'da, Pulse klasörü burada geçici bir yere yönlenir).</summary>
[Collection("Loc")]
public class PulseLinkTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "klyc-pulselink-" + Guid.NewGuid().ToString("N")[..8]);

    public PulseLinkTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("KLYC_PULSE_DIR", _dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("KLYC_PULSE_DIR", null);
        try { Directory.Delete(_dir, true); } catch { }
    }

    const string FullFeed = """
        {
          "schema": 1, "updatedAt": 1791796300, "pulseVersion": "1.10.0", "running": true,
          "mode": { "key": "oyun", "applying": false },
          "autoGameMode": true, "acceptsCommands": false, "yeniAlan": { "x": 1 },
          "lastReport": { "game": "Hades", "startedAt": 1791790000, "minutes": 62.4, "avgFps": 118, "cpuTempMax": 84, "gpuTempMax": 71,
                          "bottleneck": "gpu", "severity": 1, "findings": ["Ekran kartı oyunu sınırlıyor", "Arka planda Chrome %9 işlemci kullandı"] }
        }
        """;

    [Fact]
    public void ParseFeed_ReadsEverythingAndIgnoresUnknownFields()
    {
        var f = PulseLink.ParseFeed(FullFeed);
        Assert.NotNull(f);
        Assert.Equal(1791796300, f!.UpdatedAt);
        Assert.True(f.Running);
        Assert.Equal("oyun", f.ModeKey);
        Assert.False(f.AcceptsCommands);
        Assert.Equal("Hades", f.LastReport!.Game);
        Assert.Equal(118, f.LastReport.AvgFps);
        Assert.Equal(2, f.LastReport.Findings.Count);
        Assert.Equal(1, f.LastReport.Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{bozuk")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void ParseFeed_BrokenInputIsNoData(string json) => Assert.Null(PulseLink.ParseFeed(json));

    [Fact]
    public void PulseManagesPlan_OnlyWhenAliveAndAutoGameModeOn()
    {
        var now = 2_000_000_000L;
        Assert.True(PulseLink.PulseManagesPlan(new PulseFeed(1, now, "1.10.0", true, "oyun", false, false, null, AutoGameMode: true), now));
        Assert.False(PulseLink.PulseManagesPlan(new PulseFeed(1, now, "1.10.0", true, "oyun", false, false, null, AutoGameMode: false), now));   // Pulse çalışıyor ama oyunda modu değiştirmeyecek
        Assert.False(PulseLink.PulseManagesPlan(new PulseFeed(1, now - 60, "1.10.0", true, "oyun", false, false, null, AutoGameMode: true), now)); // bayat
        Assert.False(PulseLink.PulseManagesPlan(null, now));
        Assert.True(PulseLink.ParseFeed(FullFeed)!.AutoGameMode);
    }

    [Fact]
    public void ParseFeed_MissingAndWrongTypedFieldsAreNotErrors()
    {
        var f = PulseLink.ParseFeed("""{"running": "evet", "updatedAt": "dün", "mode": 5, "lastReport": {"game": 3}}""");
        Assert.NotNull(f);
        Assert.False(f!.Running);
        Assert.Equal(0, f.UpdatedAt);
        Assert.Null(f.ModeKey);
        Assert.Null(f.LastReport);
    }

    [Fact]
    public void ReadFeed_MissingFileIsNull_And_FileIsRead()
    {
        Assert.Null(PulseLink.ReadFeed());
        File.WriteAllText(PulseLink.FeedFile, FullFeed);
        Assert.Equal("oyun", PulseLink.ReadFeed()!.ModeKey);
        File.WriteAllText(PulseLink.FeedFile, "{yarım");
        Assert.Null(PulseLink.ReadFeed());
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 19, true)]
    [InlineData(true, 21, false)]       // bayat
    [InlineData(true, -10, true)]       // küçük saat farkı
    [InlineData(true, -4000, false)]    // gelecekten: güvenilmez
    [InlineData(false, 1, false)]       // Pulse kapandığını yazmış
    public void IsAlive_FreshnessRule(bool running, int ageSeconds, bool expected)
    {
        var now = 2_000_000_000L;
        var f = new PulseFeed(1, now - ageSeconds, "1.10.0", running, "oyun", false, false, null);
        Assert.Equal(expected, PulseLink.IsAlive(f, now));
    }

    [Fact]
    public void IsAlive_NullIsFalse() => Assert.False(PulseLink.IsAlive(null));

    // ---- Oyun başlatırken "Pulse var mı" (sanal saat: test beklemez) ----

    static PulseFeed Feed(long updated, bool running = true) => new(1, updated, "1.10.0", running, "oyun", false, false, null);

    [Fact]
    public async Task Present_FreshFeed_NoWait()
    {
        var waits = 0;
        var ok = await PulseLink.PulseIsPresentAsync(TimeSpan.FromSeconds(3), () => Feed(1000), () => 1005, _ => { waits++; return Task.CompletedTask; });
        Assert.True(ok);
        Assert.Equal(0, waits);
    }

    [Fact]
    public async Task Present_NoFeed_NoWait_NotPresent()
    {
        var waits = 0;
        var ok = await PulseLink.PulseIsPresentAsync(TimeSpan.FromSeconds(3), () => null, () => 1005, _ => { waits++; return Task.CompletedTask; });
        Assert.False(ok);
        Assert.Equal(0, waits);       // Pulse kurulu değilse oyun başlatma gecikmez
    }

    [Fact]
    public async Task Present_PulseClosedItself_NoWait_NotPresent()
    {
        var waits = 0;
        var ok = await PulseLink.PulseIsPresentAsync(TimeSpan.FromSeconds(3), () => Feed(900, running: false), () => 1005, _ => { waits++; return Task.CompletedTask; });
        Assert.False(ok);
        Assert.Equal(0, waits);
    }

    [Fact]
    public async Task Present_StaleThenRefreshed_WaitsUntilFresh()
    {
        long clock = 1000; var reads = 0;
        var ok = await PulseLink.PulseIsPresentAsync(TimeSpan.FromSeconds(3),
            () => { reads++; return reads < 4 ? Feed(900) : Feed(clock); },       // 4. okumada Pulse özeti yeniledi
            () => clock, _ => { clock += 1; return Task.CompletedTask; });
        Assert.True(ok);
        Assert.Equal(4, reads);
    }

    [Fact]
    public async Task Present_VeryOldRunningFeed_NoWait_PulseWasUninstalledOrKilled()
    {
        var waits = 0;
        var ok = await PulseLink.PulseIsPresentAsync(TimeSpan.FromSeconds(3), () => Feed(1000), () => 1000 + 3600, _ => { waits++; return Task.CompletedTask; });
        Assert.False(ok);
        Assert.Equal(0, waits);       // kapatılmadan kalmış eski dosya, oyun başlatmayı her seferinde 3 sn geciktirmesin
    }

    [Fact]
    public async Task Present_StaleForever_GivesUpAfterMaxWait()
    {
        var waited = TimeSpan.Zero;
        var ok = await PulseLink.PulseIsPresentAsync(TimeSpan.FromSeconds(3), () => Feed(900), () => 1000, d => { waited += d; return Task.CompletedTask; });
        Assert.False(ok);
        Assert.InRange(waited.TotalSeconds, 2.9, 3.3);     // en çok ~3 sn bekler, oyun başlatmayı uzun geciktirmez
    }

    // ---- Rapor eşleştirme ----

    [Theory]
    [InlineData("Hades", null, true)]
    [InlineData("HADES", null, true)]
    [InlineData("Başka Oyun", @"C:\Games\Hades\bin", true)]       // başlık uymuyor, klasörde adı geçiyor
    [InlineData("Başka Oyun", @"C:\Games\Other", false)]
    [InlineData("Başka Oyun", null, false)]
    public void ReportFor_MatchesByTitleOrInstallDir(string title, string? dir, bool expected)
    {
        var feed = PulseLink.ParseFeed(FullFeed);
        var got = PulseLink.ReportFor(feed, title, dir);
        Assert.Equal(expected, got != null);
    }

    [Fact]
    public void ReportFor_NoReport_IsNull()
    {
        Assert.Null(PulseLink.ReportFor(null, "Hades", null));
        Assert.Null(PulseLink.ReportFor(Feed(1000), "Hades", null));
    }

    [Fact]
    public void Summarize_ShowsMinutesFpsTempsAndTopTwoFindings()
    {
        var text = PulseLink.Summarize(PulseLink.ParseFeed(FullFeed)!.LastReport!);
        Assert.StartsWith("Pulse raporu · 62 dk · ort. 118 FPS", text);
        Assert.Contains("işlemci en çok 84 °C", text);
        Assert.Contains("• Ekran kartı oyunu sınırlıyor", text);
        Assert.Contains("• Arka planda Chrome", text);
    }

    [Fact]
    public void ShortSummary_FpsAndFirstFinding_OrCleanBill()
    {
        var r = PulseLink.ParseFeed(FullFeed)!.LastReport!;
        Assert.Equal("ort. 118 FPS · Ekran kartı oyunu sınırlıyor", PulseLink.ShortSummary(r));
        Assert.Equal("sorun görülmedi", PulseLink.ShortSummary(r with { AvgFps = null, Findings = new(), Severity = 0 }));
        Assert.Equal("dikkat edilecek şeyler var", PulseLink.ShortSummary(r with { AvgFps = null, Findings = new(), Severity = 2 }));
    }

    [Fact]
    public void StatusText_AliveClosedMissing()
    {
        var now = 1_000_000L;
        Assert.Contains("çalışıyor · v1.10.0 · Oyun modu", PulseLink.StatusText(Feed(now), now));
        Assert.Equal("kapalı", PulseLink.StatusText(Feed(now - 100), now));
        Assert.StartsWith("bulunamadı", PulseLink.StatusText(null));
    }

    // ---- status.json (şema 2) ----

    static Game MakeGame() => new() { Title = "Hades", Source = "Steam", Id = "steam:1145360", Target = "steam://rungameid/1145360", InstallDir = @"C:\Program Files (x86)\Steam\steamapps\common\Hades" };

    [Fact]
    public void BuildStatus_Schema2_WithNestedGame_AndLegacyFields()
    {
        var json = PulseLink.BuildStatus("launching", MakeGame(), "pulse", guard: true, controllerConnected: true, pid: 14660, version: "0.8.0", now: 1791796212);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal(2, r.GetProperty("schema").GetInt32());
        Assert.Equal("launching", r.GetProperty("state").GetString());
        Assert.Equal(1791796212, r.GetProperty("updatedAt").GetInt64());
        Assert.Equal("pulse", r.GetProperty("powerPlanManagedBy").GetString());
        var g = r.GetProperty("game");
        Assert.Equal("Hades", g.GetProperty("title").GetString());
        Assert.Equal("steam:1145360", g.GetProperty("id").GetString());
        Assert.Equal("steam", g.GetProperty("store").GetString());
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\Hades", g.GetProperty("installDir").GetString());
        Assert.Equal("steam:1145360", r.GetProperty("gameId").GetString());      // şema 1 okuyucuları için
        Assert.True(r.GetProperty("guard").GetBoolean());
    }

    [Fact]
    public void BuildStatus_NoGame_GameIsNull()
    {
        var json = PulseLink.BuildStatus("launcher", null, "none", guard: false, controllerConnected: false, pid: 1, version: "0.8.0", now: 5);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("game").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("gameId").ValueKind);
    }

    [Theory]
    [InlineData("Steam", "steam")]
    [InlineData("Epic Games", "epic")]
    [InlineData("Xbox", "xbox")]
    [InlineData("EA app", "ea")]
    [InlineData("Riot", "riot")]
    [InlineData("GOG Galaxy", "gog")]
    [InlineData("Ubisoft Connect", "ubisoft")]
    [InlineData("Battle.net", "battlenet")]
    [InlineData("Kısayol", "custom")]
    [InlineData("Masaüstü kısayolu", "custom")]
    [InlineData("", "custom")]
    public void StoreName_MapsSourceToKey(string source, string expected) => Assert.Equal(expected, PulseLink.StoreName(source));

    [Fact]
    public void WriteAtomic_WritesOverwritesAndLeavesNoTempFile()
    {
        var path = Path.Combine(_dir, "status.json");
        Assert.True(PulseLink.WriteAtomic(path, "{\"a\":1}"));
        Assert.True(PulseLink.WriteAtomic(path, "{\"a\":2}"));
        Assert.Equal("{\"a\":2}", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task WriteAtomic_ReaderNeverSeesPartialFile()
    {
        var path = Path.Combine(_dir, "status.json");
        var json = PulseLink.BuildStatus("playing", MakeGame(), "konsol", true, true, 1, "0.8.0", 1);
        PulseLink.WriteAtomic(path, json);
        var stop = false; var bad = 0; var reads = 0;
        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                try { using var d = JsonDocument.Parse(File.ReadAllText(path)); reads++; }
                catch (IOException) { }
                catch (JsonException) { bad++; }
            }
        });
        for (var i = 0; i < 300; i++) PulseLink.WriteAtomic(path, PulseLink.BuildStatus("playing", MakeGame(), "konsol", true, true, 1, "0.8.0", i));
        Volatile.Write(ref stop, true);
        await reader;
        Assert.Equal(0, bad);
        Assert.True(reads > 0);
    }

    // ---- Güç planı: başkası değiştirdiyse geri yükleme yok ----

    [Theory]
    [InlineData("aaa", "aaa", true)]       // hâlâ Konsol'un seçtiği plan: eski plana dön
    [InlineData("AAA", "aaa", true)]       // büyük/küçük harf farkı önemsiz
    [InlineData("aaa", "bbb", false)]      // Pulse/kullanıcı değiştirmiş: ezme
    [InlineData(null, "bbb", true)]        // eski sürümden kalan oturum: eski davranış
    [InlineData("aaa", null, true)]        // etkin plan okunamadı: eski davranış
    public void ShouldRestore_OnlyWhenActivePlanIsStillOurs(string? applied, string? active, bool expected)
        => Assert.Equal(expected, GameSession.ShouldRestore(applied, active));
}

using System.Text.Json;
using System.Text.RegularExpressions;
using KlycKonsol.Input;
using Xunit;

namespace KlycKonsol.Tests;

public class PulseCommandTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "klyc-pcmd-" + Guid.NewGuid().ToString("N")[..8]);
    public PulseCommandTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    const long Now = 1800000000;

    static string Feed(string extra = "", bool accepts = true, long updated = Now) =>
        "{\"schema\":1,\"updatedAt\":" + updated + ",\"pulseVersion\":\"1.11.0\",\"running\":true,\"mode\":{\"key\":\"oyun\",\"applying\":false},\"autoGameMode\":true,\"acceptsCommands\":" + (accepts ? "true" : "false") + extra + "}";

    // ---- Özet: yeni alanlar ----

    [Fact]
    public void Feed_ParsesModesLiveAndLastCommand()
    {
        var f = PulseLink.ParseFeed(Feed(",\"modeTitle\":\"Oyun\",\"modes\":[{\"key\":\"oyun\",\"title\":\"Oyun\"},{\"key\":\"sessiz\",\"title\":\"Sessiz\"}],\"live\":{\"cpuTemp\":61.5,\"gpuTemp\":55,\"cpuLoad\":12,\"gpuLoad\":3,\"at\":1800000000},\"lastCommand\":{\"id\":\"k123456789abcde\",\"action\":\"mode\",\"ok\":true,\"message\":\"Oyun modu uygulandı.\",\"at\":1800000001}"));
        Assert.NotNull(f);
        Assert.True(f!.AcceptsCommands);
        Assert.Equal("Oyun", f.ModeTitle);
        Assert.Equal(new[] { "oyun", "sessiz" }, f.Modes!.Select(m => m.Key).ToArray());
        Assert.Equal(61.5, f.Live!.CpuTemp);
        Assert.Equal(55, f.Live.GpuTemp);
        Assert.Equal("k123456789abcde", f.LastCommand!.Id);
        Assert.True(f.LastCommand.Ok);
    }

    [Fact]
    public void OldFeedWithoutNewFields_StillParses()
    {
        var f = PulseLink.ParseFeed(Feed(accepts: false));
        Assert.NotNull(f);
        Assert.False(f!.AcceptsCommands);
        Assert.Null(f.Modes); Assert.Null(f.Live); Assert.Null(f.LastCommand); Assert.Null(f.ModeTitle);
    }

    [Fact]
    public void Feed_WrongTypesAreIgnored()
    {
        var f = PulseLink.ParseFeed(Feed(",\"modes\":\"x\",\"live\":5,\"lastCommand\":{\"id\":7},\"modeTitle\":3"));
        Assert.NotNull(f);
        Assert.Null(f!.Modes); Assert.Null(f.Live); Assert.Null(f.LastCommand); Assert.Null(f.ModeTitle);
    }

    // ---- Üst çubuk metni ----

    [Fact]
    public void ChipText_ShowsModeAndFreshTemps()
    {
        var f = PulseLink.ParseFeed(Feed(",\"modeTitle\":\"Oyun\",\"live\":{\"cpuTemp\":61.4,\"gpuTemp\":55,\"at\":1800000000}"))!;
        Assert.Equal("Oyun modu · İşlemci 61° · Ekran kartı 55°", PulseLink.ChipText(f, Now + 3));
    }

    [Fact]
    public void ChipText_StaleLiveValuesAreDropped_ButModeStays()
    {
        var f = PulseLink.ParseFeed(Feed(",\"modeTitle\":\"Sessiz\",\"live\":{\"cpuTemp\":99,\"at\":1800000000}", updated: Now + 100))!;
        Assert.Equal("Sessiz modu", PulseLink.ChipText(f, Now + 100));
    }

    [Fact]
    public void ChipText_NullWhenPulseNotAlive()
    {
        Assert.Null(PulseLink.ChipText(null, Now));
        Assert.Null(PulseLink.ChipText(PulseLink.ParseFeed(Feed()), Now + 500));   // özet bayat
        Assert.Null(PulseLink.ChipText(PulseLink.ParseFeed(Feed().Replace("\"running\":true", "\"running\":false")), Now));
    }

    [Fact]
    public void ChipText_FallsBackToKnownModeNames_AndShowsApplying()
    {
        var plain = PulseLink.ParseFeed(Feed())!;
        Assert.Equal("Oyun modu", PulseLink.ChipText(plain, Now));
        var applying = PulseLink.ParseFeed(Feed().Replace("\"applying\":false", "\"applying\":true"))!;
        Assert.Equal("Mod uygulanıyor…", PulseLink.ChipText(applying, Now));
    }

    [Fact]
    public void IsHot_ThresholdsForCpuAndGpu()
    {
        Assert.True(PulseLink.IsHot(PulseLink.ParseFeed(Feed(",\"live\":{\"cpuTemp\":91,\"at\":1}"))));
        Assert.True(PulseLink.IsHot(PulseLink.ParseFeed(Feed(",\"live\":{\"gpuTemp\":86,\"at\":1}"))));
        Assert.False(PulseLink.IsHot(PulseLink.ParseFeed(Feed(",\"live\":{\"cpuTemp\":70,\"gpuTemp\":60,\"at\":1}"))));
        Assert.False(PulseLink.IsHot(null));
    }

    // ---- Komut yazma ----

    [Fact]
    public void BuildCommand_MatchesPulseContract()
    {
        var json = PulseLink.BuildCommand("k123456789abcde", "mode", "sessiz", Now);
        using var d = JsonDocument.Parse(json);
        var r = d.RootElement;
        Assert.Equal(1, r.GetProperty("schema").GetInt32());
        Assert.Equal("k123456789abcde", r.GetProperty("id").GetString());
        Assert.Equal("mode", r.GetProperty("action").GetString());
        Assert.Equal("sessiz", r.GetProperty("mode").GetString());
        Assert.Equal(Now, r.GetProperty("at").GetInt64());
        Assert.True(json.Length < 1024);   // Pulse en çok 1 KB kabul eder
        Assert.False(JsonDocument.Parse(PulseLink.BuildCommand("k123456789abcde", "boost", null, Now)).RootElement.TryGetProperty("mode", out _));
    }

    [Fact]
    public void SendCommand_WritesAtomically_WithValidUniqueIds()
    {
        var path = Path.Combine(_dir, "pulse-command.json");
        var ids = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var id = PulseLink.SendCommand("boost", null, Now, path);
            Assert.NotNull(id);
            Assert.Matches(new Regex("^[A-Za-z0-9-]{8,64}$"), id!);   // Pulse'ın kimlik kuralı
            Assert.True(ids.Add(id!));
        }
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        using var d = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("boost", d.RootElement.GetProperty("action").GetString());
    }

    [Fact]
    public void BuildStatus_CarriesWantsLiveFlag()
    {
        static bool Flag(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.GetProperty("wantsLive").GetBoolean(); }
        Assert.False(Flag(PulseLink.BuildStatus("launcher", null, "none", true, false, 1, "1.0.0", Now)));
        Assert.True(Flag(PulseLink.BuildStatus("launcher", null, "none", true, false, 1, "1.0.0", Now, wantsLive: true)));
    }

    // ---- Sonuç izleme ----

    [Fact]
    public void Tracker_ReportsMatchingResultOnce_AndIgnoresOthers()
    {
        var t = new PulseCommandTracker();
        Assert.Null(t.Poll(null, Now));   // bekleyen yok
        t.Track("k-abc-123456", Now);
        Assert.True(t.Pending);
        var other = PulseLink.ParseFeed(Feed(",\"lastCommand\":{\"id\":\"baska-0000001\",\"ok\":true,\"message\":\"x\",\"at\":1}"));
        Assert.Null(t.Poll(other, Now + 1));   // başka kimlik: hâlâ bekle
        var mine = PulseLink.ParseFeed(Feed(",\"lastCommand\":{\"id\":\"k-abc-123456\",\"ok\":true,\"message\":\"Sessiz modu uygulandı.\",\"at\":1}"));
        var r = t.Poll(mine, Now + 2);
        Assert.Equal(("Sessiz modu uygulandı.", true), r!.Value);
        Assert.False(t.Pending);
        Assert.Null(t.Poll(mine, Now + 3));   // ikinci kez bildirilmez
    }

    [Fact]
    public void Tracker_FailureResultIsReported()
    {
        var t = new PulseCommandTracker();
        t.Track("k-fail-12345", Now);
        var f = PulseLink.ParseFeed(Feed(",\"lastCommand\":{\"id\":\"k-fail-12345\",\"ok\":false,\"message\":\"Komut reddedildi (sahip).\",\"at\":1}"));
        Assert.Equal(("Komut reddedildi (sahip).", false), t.Poll(f, Now + 1)!.Value);
    }

    [Fact]
    public void Tracker_TimesOutHonestly()
    {
        var t = new PulseCommandTracker();
        t.Track("k-time-12345", Now);
        Assert.Null(t.Poll(PulseLink.ParseFeed(Feed()), Now + PulseCommandTracker.TimeoutSeconds));
        var r = t.Poll(PulseLink.ParseFeed(Feed()), Now + PulseCommandTracker.TimeoutSeconds + 1);
        Assert.NotNull(r);
        Assert.False(r!.Value.Ok);
        Assert.False(t.Pending);
    }
}

public class PulseCommandValueTests
{
    [Fact]
    public void BuildCommand_CarriesValue_OnlyWhenGiven()
    {
        using var d = System.Text.Json.JsonDocument.Parse(PulseLink.BuildCommand("k123456789abcde", "heat", null, 1800000000, "85"));
        Assert.Equal("85", d.RootElement.GetProperty("value").GetString());
        Assert.False(System.Text.Json.JsonDocument.Parse(PulseLink.BuildCommand("k123456789abcde", "clean", null, 1800000000)).RootElement.TryGetProperty("value", out _));
    }

    [Fact]
    public void Feed_ParsesHeatTarget()
    {
        var f = PulseLink.ParseFeed("{\"schema\":1,\"updatedAt\":1,\"running\":true,\"acceptsCommands\":true,\"heatTarget\":80}");
        Assert.Equal(80, f!.HeatTarget);
        Assert.Null(PulseLink.ParseFeed("{\"schema\":1,\"updatedAt\":1,\"running\":true}")!.HeatTarget);
    }

    // ---- Oyun başına Pulse modu ----

    static string Feed(string extra = "") => "{\"schema\":1,\"updatedAt\":1800000000,\"running\":true,\"acceptsCommands\":true" + extra + "}";

    [Fact]
    public void Feed_ParsesGameProfiles_AndOldFeedHasNone()
    {
        var f = PulseLink.ParseFeed(Feed(",\"gameProfiles\":[{\"name\":\"Hades2\",\"mode\":\"sessiz\",\"enabled\":true},{\"name\":\"cs2\",\"mode\":\"oyun\",\"enabled\":false},{\"mode\":\"x\"}]"))!;
        Assert.Equal(2, f.GameProfiles!.Count);
        Assert.Equal(new PulseGameProfile("Hades2", "sessiz", true), f.GameProfiles[0]);
        Assert.False(f.GameProfiles[1].Enabled);
        Assert.Null(PulseLink.ParseFeed(Feed())!.GameProfiles);
    }

    [Fact]
    public void ProfileFor_MatchesByNameOrInstallDir_AndNeverInvents()
    {
        var f = PulseLink.ParseFeed(Feed(",\"gameProfiles\":[{\"name\":\"Hades II\",\"mode\":\"oyun\",\"enabled\":true},{\"name\":\"fc25\",\"mode\":\"oyun\",\"enabled\":true},{\"name\":\"a1\",\"mode\":\"oyun\",\"enabled\":true}]"));
        Assert.Equal("Hades II", PulseLink.ProfileFor(f, "hades ii", null)!.Name);
        Assert.Equal("fc25", PulseLink.ProfileFor(f, "EA SPORTS FC 25", @"C:\Games\EA Sports FC25\Game")!.Name);
        Assert.Null(PulseLink.ProfileFor(f, "Bilinmeyen", @"C:\Games\Other"));
        Assert.Null(PulseLink.ProfileFor(f, "Zzz", @"C:\Games\a1b2"));      // çok kısa ad klasör yolundan eşleşmez
        Assert.Null(PulseLink.ProfileFor(null, "Hades II", null));
        Assert.Null(PulseLink.ProfileFor(PulseLink.ParseFeed(Feed()), "Hades II", null));
    }

    [Fact]
    public void BuildCommand_GameMode_CarriesProfileNameAsValue()
    {
        using var d = System.Text.Json.JsonDocument.Parse(PulseLink.BuildCommand("k123456789abcde", "gamemode", "sessiz", 1800000000, "Hades II"));
        Assert.Equal("gamemode", d.RootElement.GetProperty("action").GetString());
        Assert.Equal("sessiz", d.RootElement.GetProperty("mode").GetString());
        Assert.Equal("Hades II", d.RootElement.GetProperty("value").GetString());
    }

    // ---- Sistem denetimi ----

    [Fact]
    public void Feed_ParsesAudit_AndAuditTextOrdersAttentionFirst()
    {
        var f = PulseLink.ParseFeed(Feed(",\"audit\":[{\"id\":\"vbs\",\"severity\":0,\"title\":\"VBS açık\",\"detail\":\"bilgi\"},{\"id\":\"vram\",\"severity\":1,\"title\":\"VRAM doluyor\",\"detail\":\"FC25\"},{\"id\":\"x\"}]"))!;
        Assert.Equal(2, f.Audit!.Count);
        var text = PulseLink.AuditText(f);
        Assert.True(text.IndexOf("VRAM doluyor") < text.IndexOf("VBS açık"));
        Assert.Contains("FC25", text);
    }

    [Fact]
    public void AuditText_HandlesMissingAndEmpty()
    {
        var missing = PulseLink.AuditText(PulseLink.ParseFeed(Feed()));
        var empty = PulseLink.AuditText(PulseLink.ParseFeed(Feed(",\"audit\":[]")));
        Assert.NotEmpty(missing); Assert.NotEmpty(empty); Assert.NotEqual(missing, empty);   // dil bağımsız: iki durum farklı cümle
        Assert.Null(PulseLink.ParseFeed(Feed())!.Audit);
    }

    // ---- Pulse'u kolla başlatma ----

    [Fact]
    public void StartPulse_RunsTheStartupTask_OnlyWhenItExists()
    {
        var calls = new List<string>();
        var ok = PulseLink.StartPulse(a => { calls.Add(string.Join(' ', a)); return 0; });
        Assert.True(ok.Ok);
        Assert.Equal(new[] { "/Query /TN KLYC-Pulse", "/Run /TN KLYC-Pulse" }, calls);

        calls.Clear();
        var missing = PulseLink.StartPulse(a => { calls.Add(string.Join(' ', a)); return 1; });   // görev yok
        Assert.False(missing.Ok);
        Assert.Single(calls);                       // görev yokken hiçbir şey çalıştırılmaz
        Assert.Contains("Windows ile başlat", missing.Message);

        Assert.False(PulseLink.StartPulse(a => a[0] == "/Run" ? 1 : 0).Ok);   // görev var ama çalıştırılamadı
        Assert.False(PulseLink.StartPulse(_ => throw new InvalidOperationException()).Ok);
    }
}

using KlycKonsol.Input;
using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

/// <summary>Konsol modu (Windows kabuğu) mantığı. GERÇEK kayıt defterine dokunmaz: sahte kayıt kullanılır.</summary>
public class ShellModeTests : IDisposable
{
    sealed class FakeReg : IShellRegistry
    {
        public string? Value;
        public int Writes;
        public string? GetShell() => Value;
        public void SetShell(string v) { Value = v; Writes++; }
        public void DeleteShell() { Value = null; Writes++; }
    }

    readonly string _dir = Path.Combine(Path.GetTempPath(), "klyc-shell-" + Guid.NewGuid().ToString("N")[..8]);

    public ShellModeTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "app"));
        Environment.SetEnvironmentVariable("KLYC_DATA_DIR", Path.Combine(_dir, "data"));   // AppState.Save gerçek klasöre yazmasın
    }

    public void Dispose() { Environment.SetEnvironmentVariable("KLYC_DATA_DIR", null); try { Directory.Delete(_dir, true); } catch { } }

    string MakeApp(bool withStub = true)
    {
        var app = Path.Combine(_dir, "app");
        var konsol = Path.Combine(app, "KlycKonsol.exe"); File.WriteAllText(konsol, "x");
        if (withStub) File.WriteAllText(Path.Combine(app, ShellMode.StubName), "stub");
        return konsol;
    }

    [Fact]
    public void Enable_WritesQuotedStubCommand_BacksUpPrevious_AndDisableRestores()
    {
        var reg = new FakeReg { Value = "explorer.exe" };
        var st = new AppState();
        var konsol = MakeApp();
        var stubDir = Path.Combine(_dir, "stable");
        var (ok, _) = ShellMode.Enable(st, konsol, null, reg, stubDir: stubDir);
        Assert.True(ok);
        Assert.True(ShellMode.IsEnabled(reg));
        Assert.Equal($"\"{Path.Combine(stubDir, "KlycShell.exe")}\" --konsol \"{konsol}\"", reg.Value);
        Assert.True(File.Exists(Path.Combine(stubDir, "KlycShell.exe")));
        Assert.Equal("explorer.exe", st.ShellBackup);

        var (ok2, _) = ShellMode.Disable(st, reg);
        Assert.True(ok2);
        Assert.Equal("explorer.exe", reg.Value);
        Assert.False(ShellMode.IsEnabled(reg));
        Assert.Null(st.ShellBackup);
    }

    [Fact]
    public void Disable_WhenThereWasNoValue_DeletesIt_SoWindowsDefaultReturns()
    {
        var reg = new FakeReg { Value = null };
        var st = new AppState();
        ShellMode.Enable(st, MakeApp(), null, reg, stubDir: Path.Combine(_dir, "stable"));
        Assert.NotNull(reg.Value);
        ShellMode.Disable(st, reg);
        Assert.Null(reg.Value);
    }

    [Fact]
    public void Enable_Twice_DoesNotOverwriteBackupWithOurOwnValue()
    {
        var reg = new FakeReg { Value = "explorer.exe" };
        var st = new AppState(); var konsol = MakeApp(); var sd = Path.Combine(_dir, "stable");
        ShellMode.Enable(st, konsol, null, reg, stubDir: sd);
        ShellMode.Enable(st, konsol, null, reg, stubDir: sd);
        Assert.Equal("explorer.exe", st.ShellBackup);
        ShellMode.Disable(st, reg);
        Assert.Equal("explorer.exe", reg.Value);
    }

    [Fact]
    public void Enable_RefusesAndWritesNothing_WhenStubOrKonsolMissing()
    {
        var reg = new FakeReg { Value = "explorer.exe" };
        var st = new AppState();
        var noStub = ShellMode.Enable(st, MakeApp(withStub: false), null, reg, stubDir: Path.Combine(_dir, "s1"));
        Assert.False(noStub.Ok);
        var noKonsol = ShellMode.Enable(st, Path.Combine(_dir, "app", "YOK.exe"), null, reg, stubSource: Path.Combine(_dir, "app", "KlycShell.exe"), stubDir: Path.Combine(_dir, "s2"));
        Assert.False(noKonsol.Ok);
        Assert.Equal(0, reg.Writes);   // yarım iş yok: kabuk değeri hiç değişmedi
        Assert.Equal("explorer.exe", reg.Value);
    }

    [Fact]
    public void Enable_IncludesElevatedTask_WhenGiven()
    {
        var reg = new FakeReg();
        ShellMode.Enable(new AppState(), MakeApp(), "KLYC Konsol (yonetici)", reg, stubDir: Path.Combine(_dir, "stable"));
        Assert.EndsWith(" --task \"KLYC Konsol (yonetici)\"", reg.Value);
    }

    [Fact]
    public void StableKonsolExe_PrefersVelopackRootLauncher()
    {
        var root = Path.Combine(_dir, "vel"); var cur = Path.Combine(root, "current"); Directory.CreateDirectory(cur);
        File.WriteAllText(Path.Combine(root, "KLYC Konsol.exe"), "x"); File.WriteAllText(Path.Combine(cur, "KlycKonsol.exe"), "x");
        Assert.Equal(Path.Combine(root, "KLYC Konsol.exe"), ShellMode.StableKonsolExe(Path.Combine(cur, "KlycKonsol.exe")));
        var other = Path.Combine(_dir, "plain"); Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "KlycKonsol.exe"), "x");
        Assert.Equal(Path.Combine(other, "KlycKonsol.exe"), ShellMode.StableKonsolExe(Path.Combine(other, "KlycKonsol.exe")));
    }

    [Fact]
    public void IsEnabled_FalseForOtherShells_AndOnErrors()
    {
        Assert.False(ShellMode.IsEnabled(new FakeReg { Value = "explorer.exe" }));
        Assert.False(ShellMode.IsEnabled(new FakeReg { Value = null }));
        Assert.True(ShellMode.IsEnabled(new FakeReg { Value = "\"C:\\x\\klycshell.EXE\" --konsol \"y\"" }));
    }
}

public class ElevatedStartTests
{
    [Fact]
    public void CreateArgs_AreQuotedAndHighestPrivilegeAtLogon()
    {
        var a = ElevatedStart.CreateArgs(@"C:\Apps\KLYC Konsol.exe", @"PC\kisi");
        Assert.Contains("/sc onlogon", a);
        Assert.Contains("/rl highest", a);
        Assert.Contains("/ru \"PC\\kisi\"", a);
        Assert.Contains("/tn \"KLYC Konsol (yonetici)\"", a);
        Assert.Contains("\\\"C:\\Apps\\KLYC Konsol.exe\\\" --startup", a);   // /tr içinde iç tırnaklar kaçışlı
        Assert.EndsWith("/f", a);
    }

    [Fact]
    public void DeleteArgs_TargetsOnlyOurTask() => Assert.Equal("/delete /tn \"KLYC Konsol (yonetici)\" /f", ElevatedStart.DeleteArgs);
}

using KlycKonsol.Input;
using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

public class StartupPolicyTests
{
    [Fact]
    public void StartupFlag_WithDefaultState_WaitsForPad() =>
        Assert.True(StartupPolicy.ShouldWaitForPad(new[] { "KlycKonsol.exe", "--startup" }, new AppState()));

    [Fact]
    public void FlagIsCaseInsensitive() =>
        Assert.True(StartupPolicy.ShouldWaitForPad(new[] { "x", "--STARTUP" }, new AppState()));

    [Fact]
    public void ManualStart_NeverWaits_AlwaysShows() =>
        Assert.False(StartupPolicy.ShouldWaitForPad(new[] { "KlycKonsol.exe" }, new AppState()));

    [Fact]
    public void SettingOff_AlwaysShowsEvenAtStartup() =>
        Assert.False(StartupPolicy.ShouldWaitForPad(new[] { "x", "--startup" }, new AppState { StartupNeedsPad = false }));

    [Fact]
    public void DefaultIsOn_SoMouseOnlyBootsAreNotCoveredByKonsol() => Assert.True(new AppState().StartupNeedsPad);

    [Fact]
    public void RunCommand_QuotesPathAndAddsFlag() =>
        Assert.Equal(@"""C:\Program Files\KLYC Konsol\KlycKonsol.exe"" --startup", StartupPolicy.RunCommand(@"C:\Program Files\KLYC Konsol\KlycKonsol.exe"));

    [Fact]
    public void PadWait_IsShortEnoughNotToAnnoyButLongEnoughForWirelessPads() =>
        Assert.InRange(StartupPolicy.PadWait.TotalSeconds, 5, 15);
}

public class AutostartInitTests
{
    [Fact] public void FirstNormalLaunch_InitializesOnce()
    {
        var s = new AppState();
        Assert.True(StartupPolicy.ShouldInitAutostart(new[] { "KlycKonsol.exe" }, s, isolatedData: false));
        s.AutostartInitialized = true;
        Assert.False(StartupPolicy.ShouldInitAutostart(new[] { "KlycKonsol.exe" }, s, isolatedData: false));   // kullanıcı kapatırsa yeniden açılmaz
    }

    [Theory]
    [InlineData("--debugcmd")] [InlineData("--virtualonly")] [InlineData("--noextras")] [InlineData("--from-guard")] [InlineData("--bench")]
    public void TestAndGuardLaunches_NeverTouchRealRegistry(string flag) =>
        Assert.False(StartupPolicy.ShouldInitAutostart(new[] { "x", flag }, new AppState(), false));

    [Fact] public void IsolatedDataFolder_NeverInitializes() =>
        Assert.False(StartupPolicy.ShouldInitAutostart(new[] { "x" }, new AppState(), isolatedData: true));

    [Fact] public void InitAutostart_WritesStartupCommand_AndTurnsOffWaitForPad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "klyc-as-" + Guid.NewGuid().ToString("N")[..6]);
        Environment.SetEnvironmentVariable("KLYC_DATA_DIR", dir);
        try
        {
            var s = new AppState(); string? name = null, cmd = null;
            Assert.True(StartupPolicy.InitAutostart(s, @"C:\Apps\KlycKonsol.exe", (n, c) => { name = n; cmd = c; }));
            Assert.Equal("KLYC-Konsol", name);
            Assert.Equal("\"C:\\Apps\\KlycKonsol.exe\" --startup", cmd);
            Assert.True(s.AutostartInitialized); Assert.False(s.StartupNeedsPad);
        }
        finally { Environment.SetEnvironmentVariable("KLYC_DATA_DIR", null); try { Directory.Delete(dir, true); } catch { } }
    }
}

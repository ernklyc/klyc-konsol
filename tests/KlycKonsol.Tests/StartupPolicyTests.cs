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

using KlycKonsol.Input;
using Xunit;

namespace KlycKonsol.Tests;

public class DisplayModesTests
{
    static DisplayMode M(int w, int h, int hz) => new(w, h, hz);

    readonly List<DisplayMode> _modes = new()
    {
        M(640, 480, 60), M(1024, 768, 60), M(1280, 720, 60), M(1280, 720, 59), M(1920, 1080, 60), M(1920, 1080, 59), M(1920, 1080, 144), M(1920, 1080, 120),
        M(1920, 1080, 30), M(2560, 1440, 144), M(2560, 1440, 60), M(3840, 2160, 60), M(3840, 2160, 30),
    };

    [Fact]
    public void Resolutions_AreLargeToSmall_Unique_AndSkipTiny()
    {
        var r = DisplayModes.Resolutions(_modes);
        Assert.Equal(new[] { (3840, 2160), (2560, 1440), (1920, 1080), (1280, 720) }, r);
    }

    [Fact]
    public void Rates_CollapseNearDuplicates_DropVeryLow_AndSortDescending() =>
        Assert.Equal(new[] { 144, 120, 60 }, DisplayModes.RatesFor(_modes, 1920, 1080));   // 59 → 60'a katıldı, 30 elendi

    [Fact]
    public void Rates_UnknownResolution_IsEmpty() => Assert.Empty(DisplayModes.RatesFor(_modes, 1234, 567));

    [Fact]
    public void NearestRate_PicksClosest_PrefersHigherOnTie()
    {
        Assert.Equal(60, DisplayModes.NearestRate(new[] { 144, 120, 60 }, 75));
        Assert.Equal(120, DisplayModes.NearestRate(new[] { 144, 120, 60 }, 130));
        Assert.Equal(144, DisplayModes.NearestRate(new[] { 144, 60 }, 102));   // eşit uzaklık: yüksek olan
        Assert.Equal(75, DisplayModes.NearestRate(Array.Empty<int>(), 75));
    }

    [Fact]
    public void ToString_IsReadable() => Assert.Equal("1920×1080 · 144 Hz", M(1920, 1080, 144).ToString());

    /// <summary>Gerçek ekran: yalnızca KLYC_HW_TESTS=1 iken (değişiklik YAPMAZ: yalnızca CDS_TEST ile sınar).</summary>
    [Fact]
    public void RealDisplay_CurrentModeIsListedAndApplicable()
    {
        if (Environment.GetEnvironmentVariable("KLYC_HW_TESTS") != "1") return;
        var cur = DisplayModes.Current();
        Assert.NotNull(cur);
        Assert.Contains(cur!.Value, DisplayModes.Supported());
        Assert.True(DisplayModes.CanApply(cur.Value));
        Assert.True(DisplayModes.Supported().Count >= 2);
    }
}

public class AudioDevicesTests
{
    [Fact]
    public void Outputs_NeverThrows_AndHasAtMostOneDefault()
    {
        var list = AudioDevices.Outputs();
        Assert.True(list.Count(d => d.IsDefault) <= 1);
        Assert.All(list, d => { Assert.False(string.IsNullOrWhiteSpace(d.Id)); Assert.False(string.IsNullOrWhiteSpace(d.Name)); });
    }

    /// <summary>Gerçek aygıt: yalnızca KLYC_HW_TESTS=1 iken; varsayılanı yine aynı aygıta ayarlar (değişiklik yok).</summary>
    [Fact]
    public void RealDevice_SetDefaultToCurrentSucceedsAndStaysDefault()
    {
        if (Environment.GetEnvironmentVariable("KLYC_HW_TESTS") != "1") return;
        var cur = AudioDevices.Outputs().FirstOrDefault(d => d.IsDefault);
        Assert.NotNull(cur);
        Assert.True(AudioDevices.SetDefault(cur!.Id));
        Assert.Equal(cur.Id, AudioDevices.Outputs().First(d => d.IsDefault).Id);
    }

    [Fact]
    public void SetDefault_UnknownDevice_ReturnsFalse() => Assert.False(AudioDevices.SetDefault("{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}"));
}

public class AudioDevicesStaTests
{
    /// <summary>Konsol arayüz iş parçacığı (STA) üzerinde de aygıtlar listelenmeli: gerçek donanımda yalnızca KLYC_HW_TESTS=1 iken.</summary>
    [Fact]
    public void RealDevices_ListedFromStaThreadToo()
    {
        if (Environment.GetEnvironmentVariable("KLYC_HW_TESTS") != "1") return;
        List<AudioDevice>? list = null;
        var t = new Thread(() => list = AudioDevices.Outputs());
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        Assert.NotNull(list);
        Assert.NotEmpty(list!);
        Assert.Single(list!, d => d.IsDefault);
    }
}

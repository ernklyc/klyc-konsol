using KlycKonsol.Input;
using Xunit;

namespace KlycKonsol.Tests;

public class PadBatteryWatchTests
{
    static PadInfo P(int slot, string battery, bool wireless = true, bool connected = true) =>
        new(slot, connected, "Xbox", wireless, battery, 0.24);

    [Fact]
    public void WarnsOncePerLevel_NotRepeatedly()
    {
        var w = new PadBatteryWatch();
        Assert.Empty(w.Update(new[] { P(0, "Dolu") }));
        Assert.Equal(new[] { (0, 1) }, w.Update(new[] { P(0, "Düşük") }));
        Assert.Empty(w.Update(new[] { P(0, "Düşük") }));   // tekrar yok
        Assert.Equal(new[] { (0, 2) }, w.Update(new[] { P(0, "Boş") }));   // daha acil: yeni uyarı
        Assert.Empty(w.Update(new[] { P(0, "Boş") }));
    }

    [Fact]
    public void ResetsAfterCharging_AndWarnsAgainLater()
    {
        var w = new PadBatteryWatch();
        w.Update(new[] { P(0, "Düşük") });
        w.Update(new[] { P(0, "Dolu") });   // şarj edildi
        Assert.Equal(new[] { (0, 1) }, w.Update(new[] { P(0, "Düşük") }));
    }

    [Fact]
    public void WiredAndDisconnectedPads_NeverWarn()
    {
        var w = new PadBatteryWatch();
        Assert.Empty(w.Update(new[] { P(0, "Düşük", wireless: false), P(1, "Boş", connected: false) }));
    }

    [Fact]
    public void Pads_AreTrackedIndependently_AndUnplugClearsState()
    {
        var w = new PadBatteryWatch();
        Assert.Equal(2, w.Update(new[] { P(0, "Düşük"), P(1, "Düşük") }).Count);
        Assert.Empty(w.Update(new[] { P(0, "Düşük") }));        // 1. kol çıkarıldı
        Assert.Equal(new[] { (1, 1) }, w.Update(new[] { P(0, "Düşük"), P(1, "Düşük") }));   // takılınca yeniden uyarı
    }

    [Fact]
    public void Messages_AreDistinctPerSeverity() =>
        Assert.NotEqual(PadBatteryWatch.Message(0, 1), PadBatteryWatch.Message(0, 2));
}

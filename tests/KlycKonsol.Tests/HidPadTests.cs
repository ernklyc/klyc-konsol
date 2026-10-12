using KlycKonsol.Input;
using Xunit;

namespace KlycKonsol.Tests;

public class HidPadTests
{
    static bool[] Pressed(params int[] idx) { var b = new bool[16]; foreach (var i in idx) b[i] = true; return b; }
    static readonly double[] Center = { 0.5, 0.5, 0.5, 0.5, 0, 0 };

    [Theory]
    [InlineData(0x054C, 0x05C4, HidPadKind.DualShock4)]
    [InlineData(0x054C, 0x09CC, HidPadKind.DualShock4)]
    [InlineData(0x054C, 0x0CE6, HidPadKind.DualSense)]
    [InlineData(0x054C, 0x0DF2, HidPadKind.DualSense)]
    [InlineData(0x057E, 0x2009, HidPadKind.SwitchPro)]
    [InlineData(0x1234, 0x0001, HidPadKind.Generic)]
    public void Detect_ByVendorAndProduct(int vendor, int product, HidPadKind expected) =>
        Assert.Equal(expected, HidPadMapper.Detect((ushort)vendor, (ushort)product));

    [Fact]
    public void DualSense_FaceButtonsMapByPosition()
    {
        // Çarpı (alt) = A, Daire (sağ) = B, Kare (sol) = X, Üçgen (üst) = Y
        Assert.Equal(Btn.A, HidPadMapper.Map(HidPadKind.DualSense, Pressed(1), 0, Center).Buttons);
        Assert.Equal(Btn.B, HidPadMapper.Map(HidPadKind.DualSense, Pressed(2), 0, Center).Buttons);
        Assert.Equal(Btn.X, HidPadMapper.Map(HidPadKind.DualSense, Pressed(0), 0, Center).Buttons);
        Assert.Equal(Btn.Y, HidPadMapper.Map(HidPadKind.DualSense, Pressed(3), 0, Center).Buttons);
    }

    [Fact]
    public void DualShock4_ShoulderMenuAndPsButtons()
    {
        var s = HidPadMapper.Map(HidPadKind.DualShock4, Pressed(4, 5, 8, 9, 10, 11, 12), 0, Center);
        Assert.Equal(Btn.LB | Btn.RB | Btn.Back | Btn.Start | Btn.LS | Btn.RS | Btn.Guide, s.Buttons);   // PS tuşu = Guide
    }

    [Fact]
    public void SwitchPro_BottomButtonIsA_NotNintendoA()
    {
        Assert.Equal(Btn.A, HidPadMapper.Map(HidPadKind.SwitchPro, Pressed(0), 0, Center).Buttons);   // B (alt)
        Assert.Equal(Btn.B, HidPadMapper.Map(HidPadKind.SwitchPro, Pressed(1), 0, Center).Buttons);   // A (sağ)
        Assert.Equal(Btn.Guide, HidPadMapper.Map(HidPadKind.SwitchPro, Pressed(12), 0, Center).Buttons); // Home
        Assert.Equal(Btn.Back, HidPadMapper.Map(HidPadKind.SwitchPro, Pressed(8), 0, Center).Buttons);
        Assert.Equal(Btn.Start, HidPadMapper.Map(HidPadKind.SwitchPro, Pressed(9), 0, Center).Buttons);
    }

    [Fact]
    public void SwitchPro_ZlZrAreDigitalTriggers()
    {
        var s = HidPadMapper.Map(HidPadKind.SwitchPro, Pressed(6, 7), 0, Center);
        Assert.Equal(255, s.LT); Assert.Equal(255, s.RT);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0x0001)] [InlineData(2, 0x0001 | 0x0008)] [InlineData(3, 0x0008)] [InlineData(4, 0x0002 | 0x0008)]
    [InlineData(5, 0x0002)] [InlineData(6, 0x0002 | 0x0004)] [InlineData(7, 0x0004)] [InlineData(8, 0x0001 | 0x0004)] [InlineData(99, 0)]
    public void Dpad_FromHatSwitch(int position, int expected) => Assert.Equal((Btn)expected, HidPadMapper.Dpad(position));

    [Fact]
    public void Sticks_CenterIsZero_UpIsPositive_AndExtremesReachFullScale()
    {
        var c = HidPadMapper.Map(HidPadKind.DualSense, Pressed(), 0, Center);
        Assert.InRange(c.LX, -2, 2); Assert.InRange(c.LY, -2, 2);
        var up = HidPadMapper.Map(HidPadKind.DualSense, Pressed(), 0, new[] { 0.5, 0.0, 0.5, 0.5, 0, 0 });   // HID: Y=0 yukarı
        Assert.True(up.LY > 32000);
        var right = HidPadMapper.Map(HidPadKind.DualSense, Pressed(), 0, new[] { 1.0, 0.5, 0.5, 0.5, 0, 0 });
        Assert.True(right.LX > 32000);
        var rdown = HidPadMapper.Map(HidPadKind.DualSense, Pressed(), 0, new[] { 0.5, 0.5, 0.5, 1.0, 0, 0 });
        Assert.True(rdown.RY < -32000);
    }

    [Fact]
    public void AnalogTriggers_AndDigitalFallback()
    {
        Assert.Equal(255, HidPadMapper.Map(HidPadKind.DualSense, Pressed(), 0, new[] { 0.5, 0.5, 0.5, 0.5, 1.0, 0.5 }).LT);
        // Eksen yoksa L2/R2 düğmesi tetik sayılır
        var s = HidPadMapper.Map(HidPadKind.DualShock4, Pressed(6, 7), 0, new[] { 0.5, 0.5, 0.5, 0.5 });
        Assert.Equal(255, s.LT); Assert.Equal(255, s.RT);
    }

    [Fact]
    public void Generic_UsesCommonDirectInputLayout_AndShortArraysDoNotThrow()
    {
        Assert.Equal(Btn.A | Btn.Start, HidPadMapper.Map(HidPadKind.Generic, Pressed(0, 7), 0, Center).Buttons);
        var s = HidPadMapper.Map(HidPadKind.Generic, new bool[2], 0, Array.Empty<double>());   // eksik diziler hata vermez
        Assert.Equal(Btn.Up | 0, HidPadMapper.Map(HidPadKind.Generic, new bool[0], 1, new double[0]).Buttons);
        Assert.Equal(0, s.Buttons == 0 ? 0 : 1);
    }

    [Theory]
    [InlineData(65535u, 0)] [InlineData(0u, 1)] [InlineData(4500u, 2)] [InlineData(9000u, 3)] [InlineData(13500u, 4)] [InlineData(18000u, 5)]
    [InlineData(22500u, 6)] [InlineData(27000u, 7)] [InlineData(31500u, 8)] [InlineData(35900u, 1)]
    public void Pov_ToSwitchPosition(uint pov, int expected) => Assert.Equal(expected, WinMmPads.PovToSwitch(pov));

    [Fact]
    public void Norm_UsesDeviceRange()
    {
        Assert.Equal(0.0, WinMmPads.Norm(0, 0, 255)); Assert.Equal(1.0, WinMmPads.Norm(255, 0, 255));
        Assert.Equal(0.5, WinMmPads.Norm(32767, 0, 65535), 3);
        Assert.Equal(0.5, WinMmPads.Norm(10, 5, 5));          // geçersiz aralık: orta
        Assert.Equal(1.0, WinMmPads.Norm(300, 0, 255));       // taşma kırpılır
    }

    [Fact]
    public void Rescan_WithoutDevices_IsSafe()
    {
        var pads = new WinMmPads();
        pads.Rescan(xinputPresent: true);
        Assert.False(pads.TryRead(out _) && pads.Count == 0);   // cihaz yoksa okuma başarısız; varsa tutarlı
        _ = pads.Describe();
    }
}

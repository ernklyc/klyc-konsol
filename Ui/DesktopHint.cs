using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KlycKonsol.Input;

namespace KlycKonsol.Ui;

/// <summary>Masaüstü modunda ekranın altında duran, tıklanamayan küçük bilgi hapı.</summary>
public sealed class DesktopHint : Window
{
    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;

    public DesktopHint()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x14, 0x17, 0x26)),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(22, 10, 22, 10),
            Child = new TextBlock
            {
                Text = "Masaüstü Modu  ·  Sol çubuk: imleç  ·  Sağ çubuk: kaydır  ·  A/B: tık  ·  Y: klavye  ·  Guide veya Start+Back: Konsol'a dön",
                Foreground = Brushes.White,
                FontSize = 15,
            },
        };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h, GWL_EXSTYLE,
                Native.GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) =>
        {
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = SystemParameters.PrimaryScreenHeight - ActualHeight - 28;
        };
    }
}

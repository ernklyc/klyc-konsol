using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace KlycKonsol.Ui;

/// <summary>Seçili oyunun görselini yumuşakça çapraz geçişle arka plana koyar.</summary>
public partial class BackdropView : UserControl
{
    const int W = 480, H = 270;   // hazır katmanlar küçük çözünürlükte; gradyanlar pürüzsüz olduğu için fark edilmez

    readonly Image[] _slots;
    int _front;
    ImageSource? _current;

    public BackdropView()
    {
        InitializeComponent();
        _slots = new[] { ImgA, ImgB };
        Ground.Source = BakeGround();
        Dim.Source = BakeDim();
    }

    static BitmapSource Bake(Action<DrawingContext> draw)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) draw(dc);
        var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>klyc-box zemini: yakın-siyah + soğuk mavi ışımalar.</summary>
    static BitmapSource BakeGround() => Bake(dc =>
    {
        var full = new Rect(0, 0, W, H);
        dc.DrawRectangle(new LinearGradientBrush(C("#0F121A"), C("#070812"), 90), null, full);
        dc.DrawRectangle(new RadialGradientBrush(C("#336178AD"), C("#006178AD")) { Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = 0.55, RadiusY = 0.8 }, null, full);
        dc.DrawRectangle(new RadialGradientBrush(C("#57212B4D"), C("#00212B4D")) { Center = new Point(1, 1), GradientOrigin = new Point(1, 1), RadiusX = 0.65, RadiusY = 0.9 }, null, full);
    });

    /// <summary>Okunabilirlik için karartma: alt ve sol ağır. Tek katman.</summary>
    static BitmapSource BakeDim() => Bake(dc =>
    {
        var full = new Rect(0, 0, W, H);
        dc.DrawRectangle(new LinearGradientBrush(new GradientStopCollection
        {
            new(C("#990B0D11"), 0), new(C("#220B0D11"), 0.35), new(C("#CC0B0D11"), 0.75), new(C("#F00B0D11"), 1),
        }, 90), null, full);
        dc.DrawRectangle(new LinearGradientBrush(new GradientStopCollection { new(C("#880B0D11"), 0), new(C("#000B0D11"), 0.6) }, new Point(0, 0.5), new Point(1, 0.5)), null, full);
    });

    /// <param name="source">Hero görseli ya da çok küçültülmüş kapak (bulanıklık yerine yumuşak büyütme).</param>
    /// <param name="wide">true: yatay hero (net). false: küçük kapak (yumuşak/bulanık görünür).</param>
    public void Show(ImageSource? source, bool wide)
    {
        if (ReferenceEquals(_current, source)) return;
        _current = source;

        var prev = _slots[_front];
        _front = 1 - _front;
        var next = _slots[_front];

        next.Source = source;
        Panel.SetZIndex(next, 1);
        Panel.SetZIndex(prev, 0);

        var target = source == null ? 0 : wide ? 0.32 : 0.28;
        var dur = TimeSpan.FromMilliseconds(320);
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, target, dur));
        prev.BeginAnimation(OpacityProperty, new DoubleAnimation(0, dur) { BeginTime = TimeSpan.FromMilliseconds(80) });
    }

    /// <summary>Oyun modu: görsel referanslarını bırak.</summary>
    public void Clear()
    {
        _current = null;
        ImgA.Source = null; ImgB.Source = null;
        ImgA.BeginAnimation(OpacityProperty, null); ImgB.BeginAnimation(OpacityProperty, null);
        ImgA.Opacity = 0; ImgB.Opacity = 0;
    }

    /// <summary>Kapak görselini ~40 px'e küçültür; büyütülünce bulanık görünür, efekt gerektirmez.</summary>
    public static ImageSource? Tiny(ImageSource? cover)
    {
        if (cover is not BitmapSource bs || bs.PixelWidth == 0) return cover;
        try
        {
            var scale = 40.0 / bs.PixelWidth;
            var tb = new TransformedBitmap(bs, new ScaleTransform(scale, scale));
            var wb = new WriteableBitmap(tb);
            wb.Freeze();
            return wb;
        }
        catch { return cover; }
    }
}

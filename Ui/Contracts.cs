using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using KlycKonsol.Input;
using KlycKonsol.Library;
using KlycKonsol.Models;

namespace KlycKonsol.Ui;

/// <summary>Sayfaların kabuktan (MainWindow) istediği şeyler.</summary>
public interface IShell
{
    IReadOnlyList<Game> Games { get; }
    AppState State { get; }
    DesktopMode Mouse { get; }
    /// <summary>WebView2 gibi HWND içeren öğelerin konacağı, Viewbox dışındaki katman.</summary>
    Grid WebLayer { get; }
    XInputPad Pad { get; }
    /// <summary>Sol çubuk imleç olarak kullanılıyorsa false: yön tuşu olayı üretmesin.</summary>
    void SetStickNavigation(bool on);

    /// <summary>Dil İngilizceye çevrilince ekrandaki metinleri hemen çevirmeye başlar.</summary>
    void RestartLocalizer();

    /// <summary>Pulse'ın son özeti (Pulse yoksa null); üst çubuk birkaç saniyede bir yeniler.</summary>
    PulseFeed? LatestPulse { get; }
    /// <summary>Pulse'a mod/Hızlandır komutu gönderir ("mode" + mod anahtarı ya da "boost"); Pulse izin vermediyse nedenini bildirir.</summary>
    void SendPulseCommand(string action, string? mode);
    /// <summary>"Üst çubukta Pulse bilgisi" ayarı değişince uygular.</summary>
    void ApplyPulseInfoSetting();

    void Launch(Game game);
    void ToggleFavorite(Game game);
    void SetBackdrop(Game? game);
    void SetHints(params (string Key, string Label)[] hints);
    void FocusNav();
    void SwitchTab(int index);
    void OpenInBrowser(string url);
    void EnterDesktop();
    /// <summary>Konsol'u tamamen kapatır (oyun güç planı / bildirim ayarları geri yüklenir).</summary>
    void Exit();
    void Confirm(string question, Action onYes);
    void Toast(string text);
    void ShowCursor(bool visible);
    /// <summary>Tam ekran video gibi durumlarda üst çubuk ve sayfaları gizler.</summary>
    void SetChromeVisible(bool visible);
    Task ReloadAsync();
    /// <summary>Kütüphane süzgeçleri (kol filtresi vb.) değişince sayfaları yeniden kurar.</summary>
    void RefreshLibraryViews();
}

public interface IPage
{
    void OnEnter();
    void OnLeave();
    /// <summary>Tuşu işlediyse true.</summary>
    bool OnKey(PadKey key);
    void OnTick(PadState state, double dt) { }
    /// <summary>Konsol arka plana inerken (oyun modu): ağır kaynakları bırak.</summary>
    void OnSuspend() { }
    /// <summary>Konsol yeniden öne gelirken.</summary>
    void OnResume() { }
    void OnLibraryChanged();
}

/// <summary>ScrollViewer'ı yumuşakça kaydırır.</summary>
public static class ScrollAnim
{
    /// <summary>Hedef değişmediyse animasyon başlatma (boşuna kare çizdirmez).</summary>
    public static void Slide(System.Windows.Media.TranslateTransform tr, System.Windows.DependencyProperty prop, double target, bool animate, int ms = 240)
    {
        var current = prop == System.Windows.Media.TranslateTransform.XProperty ? tr.X : tr.Y;
        if (Math.Abs(current - target) < 0.5 && !animate) return;
        if (!animate) { tr.BeginAnimation(prop, null); tr.SetValue(prop, target); return; }
        if (Math.Abs(current - target) < 0.5) return;
        tr.BeginAnimation(prop, new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        });
    }

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(ScrollAnim),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    public static void To(ScrollViewer sv, double y, bool animate = true)
    {
        if (!animate)
        {
            sv.BeginAnimation(OffsetProperty, null);
            sv.ScrollToVerticalOffset(y);
            return;
        }
        sv.BeginAnimation(OffsetProperty, new DoubleAnimation(sv.VerticalOffset, y, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}

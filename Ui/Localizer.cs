using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using KlycKonsol.Library;

namespace KlycKonsol.Ui;

/// <summary>
/// İngilizce arayüz için ekrandaki Türkçe metinleri çevirir: görsel ağacı (TextBlock, düğme/içerik) kısa aralıklarla gezip metni
/// <see cref="Loc.Translate"/> ile değiştirir. Kodda yüzlerce yerde metin oluşturulduğu için tek tek her yere çeviri yazmak yerine tek noktadan yapılır.
/// Yalnızca dil İngilizceyken ve Konsol GÖRÜNÜRKEN çalışır (görünmezken zamanlayıcı durur: oyun sırasında hiçbir şey yapmama ilkesi).
/// Bağlı (binding) metinler bağı bozulmadan geçici değerle çevrilir (kaynak yenileyince bir sonraki turda yine çevrilir).
/// </summary>
public sealed class Localizer
{
    readonly DispatcherTimer _timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(400) };
    readonly Func<DependencyObject?> _root;

    public Localizer(Func<DependencyObject?> root)
    {
        _root = root;
        _timer.Tick += (_, _) => Pass();
    }

    public bool Running => _timer.IsEnabled;

    public void Start() { if (Loc.IsEnglish && !_timer.IsEnabled) { Pass(); _timer.Start(); } }
    public void Stop() => _timer.Stop();

    /// <summary>Bir tur: kökten başlayıp çevrilebilen her metni çevirir. Çevrilen metin sayısını döner (testler için).</summary>
    public int Pass()
    {
        if (!Loc.IsEnglish || _root() is not { } root) return 0;
        var n = 0;
        try { Walk(root, ref n); } catch { /* ağaç gezilirken değişti: sonraki turda */ }
        return n;
    }

    static bool _logged;

    static void Walk(DependencyObject o, ref int count)
    {
        if (o is UIElement ue && !ue.IsVisible) return;   // görünmeyen sayfalar/paneller gezilmez (İngilizce açılırken sayfa görününce çevrilir)
        try   // tek bir öğedeki hata geri kalanını çevirmeyi engellemesin
        {
            switch (o)
            {
                case TextBlock tb:
                    if (tb.Inlines.Count > 1 || tb.Inlines.FirstInline is not null and not Run)
                    {
                        foreach (var inline in tb.Inlines) if (inline is Run r && Fix(r.Text) is { } rt) { r.Text = rt; count++; }
                    }
                    else if (Fix(tb.Text) is { } t) { tb.SetCurrentValue(TextBlock.TextProperty, t); count++; }
                    break;
                case ContentControl cc when cc.Content is string s:
                    if (Fix(s) is { } c) { cc.SetCurrentValue(ContentControl.ContentProperty, c); count++; }
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!_logged) { _logged = true; AppLog.Write("dil", "çeviri hatası: " + ex.GetType().Name + " " + ex.Message); }
        }
        var kids = VisualTreeHelper.GetChildrenCount(o);
        for (var i = 0; i < kids; i++) Walk(VisualTreeHelper.GetChild(o, i), ref count);
    }

    /// <summary>Çeviri varsa ve metinden farklıysa yeni metin; yoksa null.</summary>
    static string? Fix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var en = Loc.Translate(text);
        return string.Equals(en, text, StringComparison.Ordinal) ? null : en;
    }
}

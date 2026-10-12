using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace KlycKonsol.Models;

public enum EntryKind { Game, App }

/// <summary>Installed: kurulu; NotInstalled: hesapta var ama kurulu değil (Steam); FreeOffer: şu an ücretsiz verilen oyun (Epic).</summary>
public enum GameAvailability { Installed, NotInstalled, FreeOffer }

/// <summary>Oyunun controller (kol) desteği. Unknown = henüz doğrulanmadı.</summary>
public enum PadSupport { Unknown, None, Partial, Full }

/// <summary>Kütüphanedeki bir oyun ya da uygulama.</summary>
public sealed class Game : INotifyPropertyChanged
{
    public required string Title { get; init; }
    public string Source { get; init; } = "";
    public EntryKind Kind { get; init; } = EntryKind.Game;
    public string? Target { get; init; }
    public string? Args { get; init; }
    public string Id { get; init; } = "";
    public string Glyph { get; init; } = "";
    public string? HeroPath { get; set; }
    public GameAvailability Availability { get; init; } = GameAvailability.Installed;
    /// <summary>Mağaza sayfası (ücretsiz oyun teklifleri için Konsol tarayıcısında açılır).</summary>
    public string? StoreUrl { get; init; }
    /// <summary>İndirilecek kapak görselinin adresi (ücretsiz oyun teklifleri).</summary>
    public string? ImageUrl { get; init; }
    /// <summary>Teklifin bitiş zamanı (unix sn); 0 = bilinmiyor / henüz başlamadı.</summary>
    public long OfferEnds { get; init; }
    /// <summary>Teklif başlangıcı (unix sn); gelecekteyse "yakında".</summary>
    public long OfferStarts { get; init; }
    public bool IsInstalled => Availability == GameAvailability.Installed;
    /// <summary>Kartın köşe etiketi: kurulu olmayanlar için "İNDİR", teklifler için "ÜCRETSİZ" / "YAKINDA".</summary>
    public string BadgeText => Availability switch
    {
        GameAvailability.NotInstalled => "İNDİR",
        GameAvailability.FreeOffer => OfferStarts > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? "YAKINDA" : "ÜCRETSİZ",
        _ => "",
    };
    public Visibility BadgeVisibility => Availability == GameAvailability.Installed ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>Steam mağazasındaki karşılığı (Steam oyunlarında kendi kimliği; diğerlerinde ada göre bulunan). Kol desteği ve görsel için.</summary>
    public string? SteamAppId { get; set; }
    /// <summary>Kapak yalnızca kısayol simgesinden geliyor (düşük kalite): daha iyisi bulunursa değiştirilir.</summary>
    public bool IconCover { get; set; }

    PadSupport _support;
    public PadSupport Support
    {
        get => _support;
        set
        {
            if (_support == value) return;
            _support = value;
            foreach (var n in new[] { nameof(Support), nameof(SupportVisibility), nameof(SupportBrush), nameof(SupportText) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
    /// <summary>Kartta küçük kol simgesi: tam = yeşil, kısmi = sarı.</summary>
    public Visibility SupportVisibility => Kind == EntryKind.Game && _support is PadSupport.Full or PadSupport.Partial ? Visibility.Visible : Visibility.Collapsed;
    public Brush SupportBrush => _support == PadSupport.Full ? (Brush)Application.Current.FindResource("Good") : (Brush)Application.Current.FindResource("Warn");
    public string SupportText => _support switch { PadSupport.Full => "Tam kol desteği", PadSupport.Partial => "Kısmi kol desteği", PadSupport.None => "Kol desteği yok", _ => "Kol desteği kontrol ediliyor…" };
    /// <summary>Kaynağa özel ek bilgi (Epic: katalog öğe kimliği).</summary>
    public string? Meta { get; init; }
    /// <summary>Oyunun kurulum klasörü (oyun kapanınca Konsol'a otomatik dönmek için süreç izlemede kullanılır).</summary>
    public string? InstallDir { get; set; }

    ImageSource? _cover;
    ImageSource? _coverTiny;

    /// <summary>
    /// 2:3'e yakın dikey kapaklar kutuyu doldurur (hafif kırpma). Daha kareye/yataya yakın görseller KESİLMESİN diye
    /// olduğu gibi sığdırılır; boş kalan yerler kapağın bulanık/küçültülmüş hâliyle doldurulur.
    /// </summary>
    public Stretch CoverStretch =>
        _cover is { Width: > 0, Height: > 0 } && _cover.Height / _cover.Width >= 1.25 ? Stretch.UniformToFill : Stretch.Uniform;
    public ImageSource? CoverTiny => _coverTiny;
    public Visibility FillVisibility => _cover != null && CoverStretch == Stretch.Uniform ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Cover
    {
        get => _cover;
        set
        {
            _cover = value;
            _coverTiny = value == null ? null : Ui.BackdropView.Tiny(value);
            foreach (var n in new[] { nameof(Cover), nameof(HasCover), nameof(CoverStretch), nameof(CoverTiny), nameof(FillVisibility), nameof(FallbackVisibility), nameof(GlyphVisibility), nameof(InitialVisibility) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
    public Brush Accent { get; init; } = Palette.For("?");
    public long LastPlayed { get; set; }

    long _playSeconds;
    /// <summary>Toplam oynama süresi (saniye): Steam oyunlarında Steam'in kaydı, diğerlerinde Konsol'un kendi takibi.</summary>
    public long PlaySeconds
    {
        get => _playSeconds;
        set
        {
            _playSeconds = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlaySeconds)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
        }
    }

    /// <summary>Kartın altındaki küçük satır: "Steam · 12 sa 30 dk".</summary>
    public string Subtitle
    {
        get
        {
            if (Availability == GameAvailability.FreeOffer && OfferEnds > 0)
            {
                var d = DateTimeOffset.FromUnixTimeSeconds(OfferEnds).ToLocalTime();
                return OfferStarts > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? $"{Source} · {DateTimeOffset.FromUnixTimeSeconds(OfferStarts).ToLocalTime():d MMM} başlar" : $"{Source} · {d:d MMM}'e kadar";
            }
            var t = Library.PlayTime.Format(_playSeconds);
            return string.IsNullOrEmpty(Source) ? t : t == "" ? Source : $"{Source} · {t}";
        }
    }
    public Visibility SubtitleVisibility => Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool HasCover => Cover != null;
    public Visibility FallbackVisibility => Cover == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => Cover == null && Kind == EntryKind.App ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InitialVisibility => Cover == null && Kind == EntryKind.Game ? Visibility.Visible : Visibility.Collapsed;
    public string Initial => Title.Length > 0 ? Title[..1].ToUpperInvariant() : "?";
    public Visibility SourceVisibility => string.IsNullOrEmpty(Source) ? Visibility.Collapsed : Visibility.Visible;

    bool _selected, _favorite;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value, nameof(IsSelected)); }
    public bool IsFavorite
    {
        get => _favorite;
        set
        {
            if (!Set(ref _favorite, value, nameof(IsFavorite))) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteVisibility)));
        }
    }
    public Visibility FavoriteVisibility => _favorite ? Visibility.Visible : Visibility.Collapsed;

    bool Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class Palette
{
    public static Brush For(string key)
    {
        var h = 0;
        foreach (var ch in key) h = h * 31 + ch;
        var hue = Math.Abs(h) % 360;
        var br = new LinearGradientBrush(FromHsv(hue, 0.45, 0.50), FromHsv((hue + 40) % 360, 0.55, 0.24), 60);
        br.Freeze();
        return br;
    }

    static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}

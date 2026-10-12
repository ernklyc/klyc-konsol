using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KlycKonsol.Input;
using KlycKonsol.Library;

namespace KlycKonsol.Ui;

/// <summary>
/// Ekran ve ses (Ayarlar'dan açılır): ses çıkış aygıtını değiştir, çözünürlük / yenileme hızı seç.
/// Ekran değişikliği güvenli: uygulanınca 15 sn içinde "Koru" denmezse (ya da panel kapatılırsa) eski kipe otomatik dönülür —
/// görüntü bozuk/okunmaz olsa bile kol olmadan geri gelinir.
/// </summary>
public sealed class DevicePanel : Grid
{
    const int ConfirmSeconds = 15;

    readonly IShell _shell;
    readonly ListMenu _menu = new() { Width = 1240, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    List<DisplayMode> _modes = new();
    List<AudioDevice> _audio = new();
    DisplayMode? _current;
    int _resIndex, _rate;
    DisplayMode? _previous;   // onay beklenirken: geri dönülecek kip
    int _left;

    public event Action? Closed;
    public bool IsOpen => Visibility == Visibility.Visible;
    public bool Confirming => _previous != null;

    public DevicePanel(IShell shell)
    {
        _shell = shell;
        Visibility = Visibility.Collapsed;
        _menu.Closed += OnMenuClosed;
        _timer.Tick += (_, _) => { if (--_left <= 0) Revert("timeout"); else _menu.Refresh(); };
        Children.Add(_menu);
    }

    public void Open()
    {
        Visibility = Visibility.Visible;
        Load();
        _menu.Open("Ekran ve ses", BuildRows, select: 1);
    }

    public void Close() => _menu.Close();
    public bool HandleKey(PadKey key) => _menu.HandleKey(key);

    void OnMenuClosed()
    {
        if (Confirming) Revert("panel closed");
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    void Load()
    {
        _modes = DisplayModes.Supported();
        _current = DisplayModes.Current();
        _audio = AudioDevices.Outputs();
        AppLog.Write("ses", $"{_audio.Count} çıkış aygıtı" + (AudioDevices.LastError is { } le ? " — hata: " + le : ""));
        var res = DisplayModes.Resolutions(_modes);
        var cur = _current;
        _resIndex = cur == null ? 0 : Math.Max(0, res.FindIndex(r => r.Width == cur.Value.Width && r.Height == cur.Value.Height));
        _rate = cur?.Hz ?? 60;
    }

    List<(int Width, int Height)> Resolutions() => DisplayModes.Resolutions(_modes);

    DisplayMode Pending()
    {
        var res = Resolutions();
        if (res.Count == 0) return _current ?? new DisplayMode(1920, 1080, 60);
        var (w, h) = res[Math.Clamp(_resIndex, 0, res.Count - 1)];
        var rates = DisplayModes.RatesFor(_modes, w, h);
        return new DisplayMode(w, h, DisplayModes.NearestRate(rates, _rate));
    }

    List<MenuRow> BuildRows()
    {
        if (Confirming)
        {
            return new List<MenuRow>
            {
                new() { Text = "EKRAN AYARI DEĞİŞTİ", Header = true },
                new() { Text = $"Yeni ayar: {_current}", Accent = true },
                new() { Text = "Bu ayarı koru", Right = $"{_left} sn", OnA = Keep },
                new() { Text = "Geri al", Right = "eski ayara dön", OnA = () => Revert("user") },
                new() { Text = "Görüntü düzgün değilse hiçbir şeye basma: süre dolunca eski ayara kendiliğinden dönülür", Header = true },
            };
        }

        var rows = new List<MenuRow> { new() { Text = "SES ÇIKIŞI", Header = true } };
        if (_audio.Count == 0) rows.Add(new MenuRow { Text = "Ses aygıtı bulunamadı", Right = "—" });
        foreach (var d in _audio)
        {
            var dev = d;
            rows.Add(new MenuRow { Text = d.Name, Right = d.IsDefault ? "✓ Kullanımda" : "A: bunu kullan", Accent = d.IsDefault, OnA = () => UseAudio(dev) });
        }

        rows.Add(new MenuRow { Text = "EKRAN", Header = true });
        var res = Resolutions();
        if (res.Count == 0 || _current == null)
        {
            rows.Add(new MenuRow { Text = "Ekran bilgisi okunamadı", Right = "—" });
            return rows;
        }
        var p = Pending();
        var rates = DisplayModes.RatesFor(_modes, p.Width, p.Height);
        rows.Add(new MenuRow { Text = "Şu an", Right = _current.ToString() });
        rows.Add(new MenuRow { Text = "Çözünürlük", Right = p.Resolution, OnLeftRight = d => { _resIndex = (_resIndex + d + res.Count) % res.Count; } });
        rows.Add(new MenuRow { Text = "Yenileme hızı", Right = $"{p.Hz} Hz", OnLeftRight = d => CycleRate(rates, p.Hz, d) });
        rows.Add(new MenuRow
        {
            Text = p == _current ? "Uygula (zaten bu ayarda)" : "Uygula", Accent = p != _current, Right = p == _current ? null : $"{_current} → {p}",
            OnA = () => ApplyPending(p),
        });
        return rows;
    }

    void CycleRate(List<int> rates, int cur, int dir)
    {
        if (rates.Count == 0) return;
        var i = rates.IndexOf(cur); if (i < 0) i = 0;
        _rate = rates[(i + dir + rates.Count) % rates.Count];
    }

    void UseAudio(AudioDevice dev)
    {
        if (dev.IsDefault) { _shell.Toast("Zaten bu aygıt kullanılıyor"); return; }
        if (AudioDevices.SetDefault(dev.Id)) _shell.Toast("Ses çıkışı: " + dev.Name);
        else _shell.Toast("Ses aygıtı değiştirilemedi");
        _audio = AudioDevices.Outputs();
    }

    void ApplyPending(DisplayMode next)
    {
        var before = _current;
        if (before == null || next == before) return;
        if (!DisplayModes.CanApply(next)) { _shell.Toast("Bu ekran bu ayarı desteklemiyor"); return; }
        if (!DisplayModes.Apply(next)) { _shell.Toast("Ekran ayarı değiştirilemedi"); return; }
        AppLog.Write("ekran", $"{before} → {next} uygulandı (onay bekleniyor)");
        _previous = before;
        _current = DisplayModes.Current() ?? next;
        _left = ConfirmSeconds;
        _timer.Start();
        _menu.Refresh();
    }

    void Keep()
    {
        _timer.Stop();
        AppLog.Write("ekran", $"{_current} korundu");
        _previous = null;
        _shell.Toast("Ekran ayarı korundu");
        Load();
        _menu.Refresh();
    }

    void Revert(string why)
    {
        _timer.Stop();
        var prev = _previous;
        _previous = null;
        if (prev is { } p)
        {
            var ok = DisplayModes.Apply(p);
            AppLog.Write("ekran", $"{p} geri yüklendi ({why}): {(ok ? "tamam" : "BAŞARISIZ")}");
            _shell.Toast(ok ? "Ekran eski ayarına döndü" : "Eski ekran ayarı geri yüklenemedi");
        }
        if (IsOpen) { Load(); _menu.Refresh(); }
    }
}

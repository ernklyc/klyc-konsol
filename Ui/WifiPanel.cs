using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KlycKonsol.Input;

namespace KlycKonsol.Ui;

/// <summary>
/// Wi‑Fi paneli (Ayarlar'dan açılır): görünen ağlar, sinyal, kayıtlı/bağlı durumu.
/// ▲▼ seç · A bağlan (şifreli yeni ağda ekran klavyesi) · X bağlantıyı kes · Y yenile · Start unut · B kapat.
/// </summary>
public sealed class WifiPanel : Grid
{
    const int Visible = 7;

    readonly IShell _shell;
    readonly StackPanel _rows = new();
    readonly TextBlock _status = new() { FontSize = 22, Margin = new Thickness(6, 14, 6, 0), TextWrapping = TextWrapping.Wrap };
    readonly OnScreenKeyboard _osk = new() { VerticalAlignment = VerticalAlignment.Bottom };
    List<WifiNetwork> _nets = new();
    WifiNetwork? _pending;   // şifre bekleyen ağ
    int _sel, _top;
    bool _busy;

    public event Action? Closed;
    public bool IsOpen => Visibility == Visibility.Visible;
    public bool KeyboardOpen => _osk.IsOpen;

    public WifiPanel(IShell shell)
    {
        _shell = shell;
        Visibility = Visibility.Collapsed;

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0D, 0x10, 0x18)),
            CornerRadius = new CornerRadius(30), Padding = new Thickness(36, 28, 36, 28),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Width = 1240,
            BorderBrush = (Brush)Application.Current.FindResource("CardStroke"), BorderThickness = new Thickness(1.5),
        };
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Wi‑Fi", FontSize = 40, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock { Text = "Kayıtlı ağlara tek tuşla, yeni ağlara şifre girerek bağlan.", FontSize = 20, Foreground = (Brush)Application.Current.FindResource("TextDim"), Margin = new Thickness(0, 2, 0, 16) });
        root.Children.Add(_rows);
        root.Children.Add(_status);
        card.Child = root;
        Children.Add(card);

        _osk.Typed += _ => { };
        _osk.Backspace += () => { };
        _osk.Submit += () => { var net = _pending; var pw = _osk.Text; _osk.Close(); if (net != null) _ = ConnectNewAsync(net, pw); };
        _osk.Closed += () => { _shell.Mouse.Block(); _pending = null; Render(); };
        Children.Add(_osk);
    }

    public string Hints => _osk.IsOpen ? "A Yaz   X Sil   Y Büyük harf   Start Bağlan   B Vazgeç"
        : "▲▼ Ağ seç   A Bağlan   X Bağlantıyı kes   Y Yenile   Start Unut   B Kapat";

    public void Open()
    {
        Visibility = Visibility.Visible;
        _sel = 0; _top = 0; _nets = new();
        _status.Text = "Ağlar taranıyor…";
        Render();
        _ = RefreshAsync();
    }

    public void Close()
    {
        if (_osk.IsOpen) _osk.Close();
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!await WifiService.HasAdapterAsync()) { _nets = new(); _status.Text = "Bu bilgisayarda Wi‑Fi bağdaştırıcısı bulunamadı."; Render(); return; }
            var keep = _nets.ElementAtOrDefault(_sel)?.Ssid;
            _nets = await WifiService.ScanAsync();
            _sel = Math.Max(0, _nets.FindIndex(n => n.Ssid == keep));
            var cur = _nets.FirstOrDefault(n => n.Connected);
            _status.Text = _nets.Count == 0 ? "Çevrede ağ bulunamadı." : cur != null ? $"Bağlı: {cur.Ssid}" : "Bağlı değil";
            Render();
        }
        catch { _status.Text = "Ağlar okunamadı."; Render(); }
        finally { _busy = false; }
    }

    async Task ConnectAsync(WifiNetwork net)
    {
        if (_busy) return;
        _busy = true;
        _status.Text = $"{net.Ssid} ağına bağlanılıyor…";
        try
        {
            var ok = await WifiService.ConnectAsync(net.Ssid);
            _shell.Toast(ok ? $"{net.Ssid} ağına bağlanıldı" : $"{net.Ssid} ağına bağlanılamadı");
        }
        catch { _shell.Toast("Bağlantı kurulamadı"); }
        finally { _busy = false; }
        await RefreshAsync();
    }

    async Task ConnectNewAsync(WifiNetwork net, string password)
    {
        if (_busy) return;
        _busy = true;
        _status.Text = $"{net.Ssid} ağına bağlanılıyor…";
        try
        {
            if (!await WifiService.AddProfileAsync(net, password))
            {
                _shell.Toast(WifiService.BuildProfileXml(net.Ssid, net.Auth, "x") == null ? "Bu ağ türü (kurumsal / WEP) desteklenmiyor — Windows ayarlarını kullan" : "Ağ profili eklenemedi");
                return;
            }
            var ok = await WifiService.ConnectAsync(net.Ssid);
            if (!ok) { await WifiService.ForgetAsync(net.Ssid); _shell.Toast("Bağlanılamadı — şifre yanlış olabilir"); }   // yanlış şifreyle kayıt kalmasın
            else _shell.Toast($"{net.Ssid} ağına bağlanıldı");
        }
        catch { _shell.Toast("Bağlantı kurulamadı"); }
        finally { _busy = false; }
        await RefreshAsync();
    }

    void Render()
    {
        _rows.Children.Clear();
        if (_nets.Count == 0) return;
        if (_sel < _top) _top = _sel;
        if (_sel >= _top + Visible) _top = _sel - Visible + 1;

        for (var i = _top; i < Math.Min(_nets.Count, _top + Visible); i++)
        {
            var n = _nets[i];
            var on = i == _sel;
            var fg = on ? (Brush)Application.Current.FindResource("Ink") : (Brush)Application.Current.FindResource("Text");
            var dim = on ? (Brush)Application.Current.FindResource("Ink") : (Brush)Application.Current.FindResource("TextDim");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 30, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) });
            var name = new TextBlock { Text = n.Ssid, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var tags = new List<string>();
            if (n.Connected) tags.Add("Bağlı"); else if (n.Known) tags.Add("Kayıtlı");
            if (!n.IsOpen) tags.Add("🔒");
            tags.Add($"%{n.Signal}");
            var right = new TextBlock { Text = string.Join("   ", tags), FontSize = 22, Foreground = n.Connected && !on ? (Brush)Application.Current.FindResource("Good") : dim, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            _rows.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 14, 22, 14), Margin = new Thickness(0, 3, 0, 3), Child = grid,
                Background = on ? (Brush)Application.Current.FindResource("Lit") : (Brush)Application.Current.FindResource("Card"),
            });
        }
        if (_nets.Count > Visible)
            _rows.Children.Add(new TextBlock { Text = $"{_sel + 1} / {_nets.Count}", FontSize = 18, Foreground = (Brush)Application.Current.FindResource("TextDim"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 8, 0) });
    }

    public bool HandleKey(PadKey key)
    {
        if (!IsOpen) return false;
        if (_osk.IsOpen)
        {
            _osk.HandleKey(key);
            return true;
        }
        if (_busy) return true;

        switch (key)
        {
            case PadKey.Up: _sel = Math.Max(0, _sel - 1); Render(); break;
            case PadKey.Down: _sel = Math.Min(Math.Max(0, _nets.Count - 1), _sel + 1); Render(); break;
            case PadKey.B: Close(); break;
            case PadKey.Y: _status.Text = "Ağlar taranıyor…"; _ = RefreshAsync(); break;
            case PadKey.X: _ = DisconnectAsync(); break;
            case PadKey.Start:
                if (_nets.ElementAtOrDefault(_sel) is { Known: true } k) { _ = ForgetAsync(k); }
                break;
            case PadKey.A:
                if (_nets.ElementAtOrDefault(_sel) is not { } n) break;
                if (n.Connected) { _shell.Toast("Zaten bağlısın"); break; }
                if (n.Known || n.IsOpen) { _ = ConnectNewOrKnownAsync(n); }
                else { _pending = n; _osk.Open($"“{n.Ssid}” ağının şifresi", "", showPreview: true); }
                break;
        }
        return true;
    }

    async Task ConnectNewOrKnownAsync(WifiNetwork n)
    {
        if (!n.Known && n.IsOpen && !await WifiService.AddProfileAsync(n, "")) { _shell.Toast("Ağ profili eklenemedi"); return; }
        await ConnectAsync(n);
    }

    async Task DisconnectAsync()
    {
        if (_busy) return;
        _busy = true;
        try { await WifiService.DisconnectAsync(); _shell.Toast("Wi‑Fi bağlantısı kesildi"); } catch { }
        finally { _busy = false; }
        await RefreshAsync();
    }

    async Task ForgetAsync(WifiNetwork n)
    {
        if (_busy) return;
        _busy = true;
        try { await WifiService.ForgetAsync(n.Ssid); _shell.Toast($"{n.Ssid} unutuldu"); } catch { }
        finally { _busy = false; }
        await RefreshAsync();
    }
}

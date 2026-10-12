using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KlycKonsol.Input;

namespace KlycKonsol.Ui;

public partial class NavBar : UserControl
{
    static readonly string[] Names = { "Kütüphane", "Tarayıcı", "Ayarlar" };
    readonly List<(Border box, TextBlock text)> _tabs = new();
    int _active;
    bool _focused;

    public event Action<int>? TabClicked;
    public event Action? HideClicked, ExitClicked;
    /// <summary>Bağlı kolların özetini verir (pil, sayı); NavBar 10 sn'de bir sorar.</summary>
    public Func<PadInfo[]>? PadInfoProvider { get; set; }
    readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    int _tick;

    public NavBar()
    {
        InitializeComponent();
        for (var i = 0; i < Names.Length; i++)
        {
            var idx = i;
            var text = new TextBlock { Text = Names[i], FontSize = 28, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border { Padding = new Thickness(40, 14, 40, 14), CornerRadius = new CornerRadius(36), Child = text, Cursor = System.Windows.Input.Cursors.Hand };
            box.MouseLeftButtonUp += (_, _) => TabClicked?.Invoke(idx);
            Tabs.Children.Add(box);
            _tabs.Add((box, text));
        }
        Refresh();

        BadgeLB.MouseLeftButtonUp += (_, _) => TabClicked?.Invoke(Math.Max(0, _active - 1));
        BadgeRB.MouseLeftButtonUp += (_, _) => TabClicked?.Invoke(Math.Min(Names.Length - 1, _active + 1));

        BtnHide.MouseLeftButtonUp += (_, _) => HideClicked?.Invoke();
        BtnExit.MouseLeftButtonUp += (_, _) => ExitClicked?.Invoke();

        _clockTimer.Tick += (_, _) =>
        {
            UpdateClock();
            ++_tick;
            if (_tick % 10 == 1) UpdatePad();
            if (_tick % 4 == 1 || PulsePending?.Invoke() == true) UpdatePulse();   // Pulse özeti ~8 sn'de bir yenilenir; komut beklenirken sık bak
        };
        _clockTimer.Start();
        UpdateClock();
    }

    public void SetActive(int index) { _active = index; Refresh(); }

    /// <summary>Odak üst çubukta mı (yön tuşlarıyla sekme değiştirilir).</summary>
    public void SetFocused(bool focused) { _focused = focused; Refresh(); }

    void Refresh()
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            var on = i == _active;
            _tabs[i].box.Background = on ? (Brush)FindResource("Lit") : Brushes.Transparent;
            _tabs[i].text.Foreground = on ? (Brush)FindResource("Ink") : (Brush)FindResource("Text");
            _tabs[i].text.Opacity = on ? 1 : 0.78;
        }
        Pill.BorderBrush = _focused ? Brushes.White : (Brush)FindResource("CardStroke");
        Pill.BorderThickness = new Thickness(_focused ? 3 : 1.5);
    }

    /// <summary>Konsol gizliyken saat/pil zamanlayıcısı durur (oyun sırasında sıfır yük).</summary>
    public void Suspend() => _clockTimer.Stop();
    public void Resume() { UpdateClock(); UpdatePad(); UpdatePulse(); _clockTimer.Start(); }

    /// <summary>Pulse özetini okur (konsol-feed.json); Pulse yoksa null. Üst çubuktaki Pulse bilgisi ve komut sonucu izleme bunu kullanır.</summary>
    public Func<PulseFeed?>? PulseProvider { get; set; }
    /// <summary>Pulse'a komut gönderildi ve sonuç bekleniyor mu (öyleyse özet her saniye okunur).</summary>
    public Func<bool>? PulsePending { get; set; }
    /// <summary>Pulse özeti her okunduğunda (Pulse yoksa null).</summary>
    public event Action<PulseFeed?>? PulseFeedRead;
    /// <summary>Pulse bilgisini üst çubukta göster (Ayarlar'dan kapatılabilir).</summary>
    public bool ShowPulse { get; set; } = true;

    void UpdatePulse()
    {
        var feed = PulseProvider?.Invoke();
        PulseFeedRead?.Invoke(feed);
        var text = ShowPulse ? PulseLink.ChipText(feed) : null;
        if (text == null) { PulseChip.Visibility = Visibility.Collapsed; return; }
        PulseChip.Visibility = Visibility.Visible;
        PulseText.Text = Library.Loc.Translate(text);
        var hot = PulseLink.IsHot(feed);
        PulseGlyph.Foreground = (Brush)FindResource(hot ? "Warn" : "Accent");
        PulseText.Foreground = (Brush)FindResource(hot ? "Warn" : "TextDim");
    }

    readonly PadBatteryWatch _battery = new();
    /// <summary>Kablosuz kolun pili azaldığında (bir kez) uyarı metni verir.</summary>
    public event Action<string>? BatteryWarning;

    public void SetPad(bool connected) => UpdatePad();

    void UpdatePad()
    {
        var infos = PadInfoProvider?.Invoke() ?? Array.Empty<PadInfo>();
        foreach (var (slot, sev) in _battery.Update(infos)) BatteryWarning?.Invoke(PadBatteryWatch.Message(slot, sev));
        var on = infos.Where(i => i.Connected).ToList();
        if (on.Count == 0) { PadText.Text = Library.Loc.T("Controller yok"); PadGlyph.Foreground = (Brush)FindResource("TextDim"); return; }

        var text = on.Count == 1 ? "Controller bağlı" : $"{on.Count} controller";
        var wireless = on.Where(i => i.Wireless && i.Battery is "Boş" or "Düşük" or "Orta" or "Dolu").ToList();
        var low = wireless.FirstOrDefault(i => i.Battery is "Boş" or "Düşük");
        if (low != null) text += $" · pil {low.Battery.ToLowerInvariant()}";
        PadText.Text = Library.Loc.Translate(text);
        PadGlyph.Foreground = (Brush)FindResource(low != null ? "Warn" : "Good");
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        Clock.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("d MMMM, dddd", Library.Loc.IsEnglish ? CultureInfo.CurrentCulture : new CultureInfo("tr-TR"));

        if (Native.Battery() is { } b)
        {
            BatteryPanel.Visibility = Visibility.Visible;
            BatteryText.Text = $"%{b.percent}";
            BatteryGlyph.Text = b.charging ? "" : b.percent > 75 ? "" : b.percent > 40 ? "" : "";
        }
        else BatteryPanel.Visibility = Visibility.Collapsed;
    }
}

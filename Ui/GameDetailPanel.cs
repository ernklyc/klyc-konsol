using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KlycKonsol.Input;
using KlycKonsol.Library;
using KlycKonsol.Models;

namespace KlycKonsol.Ui;

/// <summary>
/// Oyun detay sayfası: kapak, açıklama, tür, çıkış tarihi, oynama süresi, ekran görüntüleri ve oyuna özel ayarlar.
/// ▲▼ satır · ◀▶ düğme / ayar değeri · A seç · X favori · B kapat.
/// </summary>
public sealed class GameDetailPanel : Border
{
    readonly IShell _shell;
    readonly Action _onChanged;
    readonly Border _cover = new() { Width = 270, Height = 405, CornerRadius = new CornerRadius(20) };
    readonly TextBlock _title = new() { FontSize = 52, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _meta = new() { FontSize = 24, Margin = new Thickness(2, 4, 0, 0) };
    readonly TextBlock _desc = new() { FontSize = 24, TextWrapping = TextWrapping.Wrap, MaxHeight = 128, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 14, 0, 0), LineHeight = 32 };
    readonly TextBlock _pulse = new() { FontSize = 21, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 0, 0), Visibility = Visibility.Collapsed };
    readonly WrapPanel _chips = new() { Margin = new Thickness(0, 14, 0, 0) };
    readonly StackPanel _shots = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
    readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 8) };
    readonly StackPanel _rows = new();
    Game? _game;
    int _row;        // 0 = düğmeler, 1.. = ayar satırları
    int _btn;        // 0 Oyna, 1 Favori
    CancellationTokenSource? _cts;

    static readonly string[] PowerValues = { "auto", "off" };
    static readonly string[] ReturnValues = { "default", "on", "off" };
    static readonly string[] SupportValues = { "auto", "full", "none" };

    public bool IsOpen => Visibility == Visibility.Visible;
    public event Action? Closed;

    public GameDetailPanel(IShell shell, Action onChanged)
    {
        _shell = shell;
        _onChanged = onChanged;
        Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0D, 0x10, 0x18));
        CornerRadius = new CornerRadius(32);
        Padding = new Thickness(40, 34, 40, 30);
        BorderBrush = Res("CardStroke");
        BorderThickness = new Thickness(1.5);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Width = 1560;

        _meta.Foreground = Res("TextDim");
        _desc.Foreground = new SolidColorBrush(Color.FromRgb(0xD5, 0xDB, 0xE8));

        var right = new StackPanel { Margin = new Thickness(40, 0, 0, 0) };
        right.Children.Add(_title);
        right.Children.Add(_meta);
        right.Children.Add(_desc);
        right.Children.Add(_chips);
        right.Children.Add(_pulse);
        right.Children.Add(_shots);
        right.Children.Add(_buttons);
        right.Children.Add(_rows);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel();
        left.Children.Add(_cover);
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Child = grid;
    }

    static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    GameOptions Opt => _shell.State.GameOptions.TryGetValue(_game!.Id, out var o) ? o : (_shell.State.GameOptions[_game.Id] = new GameOptions());

    public void Open(Game g)
    {
        _game = g;
        _row = 0; _btn = 0;
        _cover.Background = g.Accent;
        _cover.Child = new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = g.Cover == null ? null : new ImageBrush(g.Cover) { Stretch = g.CoverStretch },
        };
        _title.Text = g.Title;
        _desc.Text = "";
        _shots.Children.Clear();
        Visibility = Visibility.Visible;
        BuildMeta(null);
        ShowPulseReport(g);
        RenderAll();

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var cached = GameMetaService.TryLoadCached(g);
        if (cached != null) BuildMeta(cached);
        else if (g.Kind == EntryKind.Game) _desc.Text = "Bilgiler yükleniyor…";
        _ = LoadMetaAsync(g, cts.Token, cached != null);
    }

    async Task LoadMetaAsync(Game g, CancellationToken ct, bool hadCache)
    {
        if (g.Kind != EntryKind.Game) { BuildMeta(null); return; }
        try
        {
            var meta = hadCache ? null : await GameMetaService.FetchAsync(g, ct);
            if (ct.IsCancellationRequested || _game != g) return;
            if (meta != null) BuildMeta(meta);
            else if (!hadCache) { _desc.Text = ""; }
        }
        catch { }
    }

    /// <summary>KLYC Pulse kuruluysa ve son oyun raporu bu oyuna aitse tek satır özet (renk: yeşil sorun yok, sarı dikkat, kırmızı sorun var). Yoksa satır gizli.</summary>
    void ShowPulseReport(Game g)
    {
        var r = g.Kind == EntryKind.Game ? PulseLink.ReportFor(PulseLink.ReadFeed(), g.Title, g.InstallDir) : null;
        if (r is null) { _pulse.Visibility = Visibility.Collapsed; return; }
        _pulse.Text = PulseLink.Summarize(r);
        _pulse.Foreground = r.Severity switch
        {
            >= 2 => new SolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x6A)),
            1 => new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)),
            _ => new SolidColorBrush(Color.FromRgb(0x6C, 0xD4, 0x8A)),
        };
        _pulse.Visibility = Visibility.Visible;
    }

    public void Close()
    {
        _cts?.Cancel();
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    // ---- Görünüm --------------------------------------------------------

    Border Pill(string text, Brush? fg = null)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(20), Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(0, 0, 10, 8),
            Background = Res("Card"), BorderBrush = Res("CardStroke"), BorderThickness = new Thickness(1.5),
            Child = new TextBlock { Text = text, FontSize = 21, Foreground = fg ?? Res("Text") },
        };
    }

    void BuildMeta(GameMeta? m)
    {
        var g = _game!;
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(g.Source)) parts.Add(g.Source);
        if (m?.Released is { } rel) parts.Add(rel);
        if (m?.Genres is { Length: > 0 } gen) parts.Add(string.Join(", ", gen.Take(3)));
        if (m?.Developers is { Length: > 0 } dev) parts.Add(dev[0]);
        _meta.Text = string.Join("  ·  ", parts);

        if (m?.Description != null) _desc.Text = m.Description;
        else if (m == null && g.Kind == EntryKind.App) _desc.Text = "Uygulama";

        BuildChips();

        _shots.Children.Clear();
        foreach (var file in (m?.Screenshots ?? Array.Empty<string>()).Take(3))
        {
            var img = GameScanner.LoadImage(file, 520);
            if (img == null) continue;
            _shots.Children.Add(new Border
            {
                Width = 300, Height = 169, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 14, 0),
                Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill },
            });
        }
    }

    /// <summary>Kol desteği / oynama süresi / son oynama çipleri (tuşlara basınca da güncellenir; ağır görselleri yeniden kurmaz).</summary>
    void BuildChips()
    {
        var g = _game!;
        _chips.Children.Clear();
        if (g.Availability == GameAvailability.FreeOffer)
        {
            _chips.Children.Add(Pill(g.BadgeText == "YAKINDA" ? "Yakında ücretsiz" : "Şu an ücretsiz", Res("Good")));
            if (g.OfferEnds > 0) _chips.Children.Add(Pill(g.Subtitle.Replace("Epic · ", "")));
            return;
        }
        _chips.Children.Add(Pill(g.SupportText, g.Support switch { PadSupport.Full => Res("Good"), PadSupport.Partial => Res("Warn"), _ => Res("TextDim") }));
        _chips.Children.Add(Pill(g.PlaySeconds > 0 ? $"Oynama: {PlayTime.Format(g.PlaySeconds)}" : "Henüz oynanmadı"));
        if (g.LastPlayed > 0) _chips.Children.Add(Pill($"Son: {AgoText(g.LastPlayed)}"));
    }

    static string AgoText(long unix)
    {
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        if (span.TotalMinutes < 2) return "az önce";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} dk önce";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} sa önce";
        if (span.TotalDays < 2) return "dün";
        if (span.TotalDays < 60) return $"{(int)span.TotalDays} gün önce";
        return $"{(int)(span.TotalDays / 30)} ay önce";
    }

    void RenderAll()
    {
        var g = _game!;
        _buttons.Children.Clear();
        var play = g.Availability switch { GameAvailability.NotInstalled => "Yükle", GameAvailability.FreeOffer => "Mağazada aç", _ => "Oyna" };
        string[] names = { play, g.IsFavorite ? "★ Favorilerde" : "☆ Favori" };
        for (var i = 0; i < names.Length; i++)
        {
            var focused = _row == 0 && _btn == i;
            _buttons.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(34), Height = 68, Padding = new Thickness(34, 0, 34, 0), Margin = new Thickness(0, 0, 14, 0),
                Background = focused ? Res("Lit") : (i == 0 ? new SolidColorBrush(Color.FromArgb(0x55, 0x61, 0x78, 0xAD)) : Res("Card")),
                BorderBrush = focused ? Brushes.White : Res("CardStroke"), BorderThickness = new Thickness(focused ? 3 : 1.5),
                Child = new TextBlock { Text = names[i], FontSize = 28, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Foreground = focused ? Res("Ink") : Res("Text") },
            });
        }

        _rows.Children.Clear();
        if (g.Kind != EntryKind.Game || !g.IsInstalled) return;
        var o = Opt;
        AddRow(1, "Güç planı", o.PowerPlan == "off" ? "Değiştirme" : PulseLink.PulseManagesPlan(PulseLink.ReadFeed()) ? "Otomatik (Pulse yönetiyor)" : "Otomatik (Yüksek performans)");
        AddRow(2, "Oyun kapanınca Konsol'a dön", o.AutoReturn switch { "on" => "Açık", "off" => "Kapalı", _ => "Genel ayar" });
        var sup = _shell.State.SupportOverride.TryGetValue(g.Id, out var sv) ? sv : "auto";
        AddRow(3, "Kol desteği", sup switch { "full" => "Var (elle)", "none" => "Yok (elle)", _ => "Otomatik" });
    }

    void AddRow(int index, string label, string value)
    {
        var on = _row == index;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = on ? Res("Ink") : Res("Text") });
        var val = new TextBlock { Text = (on ? "◀  " : "") + value + (on ? "  ▶" : ""), FontSize = 24, VerticalAlignment = VerticalAlignment.Center, Foreground = on ? Res("Ink") : Res("TextDim") };
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);
        _rows.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 12, 24, 12), Margin = new Thickness(0, 3, 0, 3), Child = grid,
            Background = on ? Res("Lit") : Res("Card"), BorderBrush = on ? Brushes.White : Res("CardStroke"), BorderThickness = new Thickness(on ? 3 : 1.5),
        });
    }

    public string Hints => "▲▼ Satır   ◀▶ Seç / değiştir   A Onayla   X Favori   B Kapat";

    // ---- Tuşlar ---------------------------------------------------------

    public bool HandleKey(PadKey key)
    {
        if (!IsOpen) return false;
        var g = _game!;
        var maxRow = g.Kind == EntryKind.Game && g.IsInstalled ? 3 : 0;
        switch (key)
        {
            case PadKey.Up: _row = Math.Max(0, _row - 1); break;
            case PadKey.Down: _row = Math.Min(maxRow, _row + 1); break;
            case PadKey.Left: if (_row == 0) _btn = Math.Max(0, _btn - 1); else Cycle(-1); break;
            case PadKey.Right: if (_row == 0) _btn = Math.Min(1, _btn + 1); else Cycle(1); break;
            case PadKey.X: _shell.ToggleFavorite(g); _onChanged(); break;
            case PadKey.B: Close(); return true;
            case PadKey.A:
                if (_row == 0)
                {
                    if (_btn == 0) { Close(); _shell.Launch(g); return true; }
                    _shell.ToggleFavorite(g); _onChanged();
                }
                else Cycle(1);
                break;
        }
        BuildChips();
        RenderAll();
        return true;
    }

    void Cycle(int dir)
    {
        var g = _game!;
        var o = Opt;
        switch (_row)
        {
            case 1: o.PowerPlan = Next(PowerValues, o.PowerPlan, dir); break;
            case 2: o.AutoReturn = Next(ReturnValues, o.AutoReturn, dir); break;
            case 3:
                var cur = _shell.State.SupportOverride.TryGetValue(g.Id, out var sv) ? sv : "auto";
                var next = Next(SupportValues, cur, dir);
                if (next == "auto")
                {
                    _shell.State.SupportOverride.Remove(g.Id);
                    g.Support = _shell.State.Support.TryGetValue(g.Id, out var auto) ? ControllerSupportService.ParseName(auto) : PadSupport.Unknown;
                }
                else
                {
                    _shell.State.SupportOverride[g.Id] = next;
                    g.Support = next == "full" ? PadSupport.Full : PadSupport.None;
                }
                _onChanged();
                break;
        }
        _shell.State.Save();
    }

    static string Next(string[] values, string current, int dir)
    {
        var i = Math.Max(0, Array.IndexOf(values, current));
        return values[(i + dir + values.Length) % values.Length];
    }
}

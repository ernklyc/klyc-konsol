using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KlycKonsol.Input;

namespace KlycKonsol.Ui;

public sealed class MenuRow
{
    public string Text { get; init; } = "";
    public string? Right { get; init; }
    public bool Header { get; init; }
    public bool Accent { get; init; }
    public Action? OnA { get; init; }
    public Action? OnX { get; init; }
    public Action<int>? OnLeftRight { get; init; }
}

/// <summary>
/// Genel amaçlı denetleyici dostu liste menüsü: başlık satırları, seçilebilir satırlar, A / X / ◀ ▶ eylemleri.
/// Satırları <paramref name="build"/> üretir; eylemden sonra <see cref="Refresh"/> ile yeniden kurulur (seçim korunur).
/// </summary>
public sealed class ListMenu : Border
{
    const int Visible = 9;

    readonly StackPanel _root = new();
    readonly TextBlock _title = new() { FontSize = 38, FontWeight = FontWeights.Bold, Margin = new Thickness(6, 0, 0, 14) };
    readonly StackPanel _list = new();
    Func<List<MenuRow>>? _build;
    List<MenuRow> _rows = new();
    int _sel, _top;

    public event Action? Closed;
    public bool IsOpen => Visibility == Visibility.Visible;

    public ListMenu()
    {
        Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0D, 0x10, 0x18));
        CornerRadius = new CornerRadius(26);
        Padding = new Thickness(36, 26, 36, 22);
        BorderBrush = (Brush)Application.Current.FindResource("CardStroke");
        BorderThickness = new Thickness(1.5);
        _root.Children.Add(_title);
        _root.Children.Add(_list);
        Child = _root;
    }

    /// <summary>Başlığın üstüne eklenecek özel içerik (ör. logo).</summary>
    public void SetHeader(UIElement header) => _root.Children.Insert(0, header);

    public void Open(string title, Func<List<MenuRow>> build, int select = -1)
    {
        _title.Text = title;
        _title.Visibility = title.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _build = build;
        _rows = build();
        _sel = select >= 0 ? select : FirstSelectable(0, 1);
        _top = 0;
        Visibility = Visibility.Visible;
        Render();
    }

    public void Close()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    /// <summary>Satırları yeniden üretir; seçili sıra korunur (satır sayısı azaldıysa sığdırılır).</summary>
    public void Refresh()
    {
        if (_build == null) return;
        _rows = _build();
        if (_sel >= _rows.Count) _sel = Math.Max(0, _rows.Count - 1);
        if (_rows.Count > 0 && _rows[_sel].Header) { var n = FirstSelectable(_sel, 1); _sel = _rows[n].Header ? FirstSelectable(_sel, -1) : n; }
        Render();
    }

    int FirstSelectable(int from, int dir)
    {
        for (var i = from; i >= 0 && i < _rows.Count; i += dir)
            if (!_rows[i].Header) return i;
        return Math.Clamp(from, 0, Math.Max(0, _rows.Count - 1));
    }

    void Render()
    {
        _list.Children.Clear();
        if (_sel < _top) _top = _sel;
        if (_sel >= _top + Visible) _top = _sel - Visible + 1;
        _top = Math.Max(0, Math.Min(_top, Math.Max(0, _rows.Count - Visible)));

        var res = Application.Current;
        for (var i = _top; i < Math.Min(_rows.Count, _top + Visible); i++)
        {
            var r = _rows[i];
            if (r.Header)
            {
                _list.Children.Add(new TextBlock { Text = r.Text, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = (Brush)res.FindResource("TextDim"), Margin = new Thickness(10, i == _top ? 0 : 10, 0, 4) });
                continue;
            }
            var on = i == _sel;
            var fg = on ? (Brush)res.FindResource("Ink") : r.Accent ? (Brush)res.FindResource("Accent") : (Brush)res.FindResource("Text");
            var dim = on ? (Brush)res.FindResource("Ink") : (Brush)res.FindResource("TextDim");
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = r.Text, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            if (r.Right != null)
            {
                var right = new TextBlock { Text = (on && r.OnLeftRight != null ? "◀  " : "") + r.Right + (on && r.OnLeftRight != null ? "  ▶" : ""), FontSize = 22, Foreground = dim, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
            }
            _list.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 11, 22, 11), Margin = new Thickness(0, 3, 0, 3), Child = g,
                Background = on ? (Brush)res.FindResource("Lit") : (Brush)res.FindResource("Card"),
                BorderBrush = on ? Brushes.White : (Brush)res.FindResource("CardStroke"), BorderThickness = new Thickness(on ? 3 : 1.5),
            });
        }
        if (_rows.Count > Visible)
            _list.Children.Add(new TextBlock { Text = $"{_sel + 1} / {_rows.Count}", FontSize = 16, Foreground = (Brush)res.FindResource("TextDim"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 8, 0) });
    }

    public bool HandleKey(PadKey key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case PadKey.Up: Move(-1); break;
            case PadKey.Down: Move(1); break;
            case PadKey.Left: Act(r => r.OnLeftRight?.Invoke(-1)); break;
            case PadKey.Right: Act(r => r.OnLeftRight?.Invoke(1)); break;
            case PadKey.A: Act(r => r.OnA?.Invoke()); break;
            case PadKey.X: Act(r => r.OnX?.Invoke()); break;
            case PadKey.B: case PadKey.Start: Close(); break;
        }
        return true;
    }

    void Move(int dir)
    {
        var i = _sel + dir;
        while (i >= 0 && i < _rows.Count && _rows[i].Header) i += dir;
        if (i < 0 || i >= _rows.Count) return;
        _sel = i;
        Render();
    }

    void Act(Action<MenuRow> action)
    {
        if (_sel < 0 || _sel >= _rows.Count) return;
        var before = _rows;
        action(_rows[_sel]);
        if (IsOpen && ReferenceEquals(before, _rows)) Refresh();   // eylem menüyü kapatmadıysa güncel hâli göster
    }
}

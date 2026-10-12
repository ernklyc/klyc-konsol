using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KlycKonsol.Input;
using KlycKonsol.Library;

namespace KlycKonsol.Ui;

/// <summary>
/// Controller ile kullanılan ekran klavyesi (Türkçe Q düzeni).
/// Yön tuşları: gez · A: yaz · X: sil · Y: Shift · Start: Tamam · B: kapat.
/// </summary>
public sealed class OnScreenKeyboard : Border
{
    sealed record Key(string Label, string? Text, Action? Special, double Width = 1);

    static CultureInfo Tr => Library.Loc.IsEnglish ? CultureInfo.InvariantCulture : TurkishCulture;
    static readonly CultureInfo TurkishCulture = new("tr-TR");
    const double Unit = 104, KeyH = 88, Gap = 10;

    readonly List<List<(Key key, Border box, TextBlock tb)>> _rows = new();
    readonly TextBlock _preview = new() { FontSize = 38, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _label = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    readonly Border _previewBox = new();
    bool _shift, _showPreview;
    int _r, _c;
    string _text = "";

    public event Action<string>? Typed;
    public event Action? Backspace;
    public event Action? Submit;
    public event Action? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;
    public string Text => _text;

    public OnScreenKeyboard()
    {
        Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Color.FromArgb(0xFC, 0x10, 0x13, 0x1C));
        BorderBrush = (Brush)Application.Current.FindResource("CardStroke");
        BorderThickness = new Thickness(1.5, 1.5, 1.5, 0);
        CornerRadius = new CornerRadius(34, 34, 0, 0);
        Padding = new Thickness(40, 26, 40, 30);
        HorizontalAlignment = HorizontalAlignment.Center;

        var stack = new StackPanel();
        _label.Foreground = (Brush)Application.Current.FindResource("TextDim");
        stack.Children.Add(_label);

        _previewBox.Background = (Brush)Application.Current.FindResource("Card");
        _previewBox.BorderBrush = (Brush)Application.Current.FindResource("CardStroke");
        _previewBox.BorderThickness = new Thickness(1.5);
        _previewBox.CornerRadius = new CornerRadius(20);
        _previewBox.Padding = new Thickness(26, 10, 26, 10);
        _previewBox.Margin = new Thickness(0, 10, 0, 18);
        _previewBox.Height = 72;
        _previewBox.Child = _preview;
        stack.Children.Add(_previewBox);

        foreach (var row in Layout())
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, Gap) };
            var list = new List<(Key, Border, TextBlock)>();
            foreach (var key in row)
            {
                var tb = new TextBlock { Text = key.Label, FontSize = 34, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (key.Special != null && key.Label.Length > 1) tb.FontSize = 26;
                var box = new Border
                {
                    Width = key.Width * Unit - Gap, Height = KeyH, Margin = new Thickness(0, 0, Gap, 0), CornerRadius = new CornerRadius(18),
                    BorderThickness = new Thickness(1.5), Child = tb,
                };
                panel.Children.Add(box);
                list.Add((key, box, tb));
            }
            _rows.Add(list);
            stack.Children.Add(panel);
        }
        Child = stack;
    }

    IEnumerable<IEnumerable<Key>> Layout()
    {
        Key C(string s) => new(s, s, null);
        yield return "1234567890".Select(c => C(c.ToString())).Append(new Key("⌫", null, DoBackspace, 1.4));
        var en = Loc.IsEnglish;   // İngilizce arayüzde İngilizce (QWERTY) harfler; Türkçe harfler (ı ğ ü ş ö ç) yalnızca Türkçede
        yield return (en ? "qwertyuiop-_" : "qwertyuıopğü").Select(c => C(c.ToString()));
        yield return (en ? "asdfghjkl',;" : "asdfghjklşi,").Select(c => C(c.ToString()));
        yield return new[] { new Key("⇧", null, ToggleShift, 1.4) }.Concat((en ? "zxcvbnm.-/!?" : "zxcvbnmöç.-").Select(c => C(c.ToString())));
        yield return new[]
        {
            new Key("@", "@", null, 1), new Key("Boşluk", " ", null, 5), new Key(".com", ".com", null, 1.6),
            new Key("/", "/", null, 1), new Key("Tamam", null, DoSubmit, 2),
        };
    }

    public void Open(string label, string initial, bool showPreview)
    {
        _label.Text = label;
        _text = initial;
        _showPreview = showPreview;
        _shift = false;
        _previewBox.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        _label.Visibility = Visibility.Visible;
        _r = 1; _c = 4;
        Visibility = Visibility.Visible;
        RefreshPreview();
        RefreshKeys();
    }

    public void Close()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    void DoBackspace()
    {
        if (_showPreview && _text.Length > 0) _text = _text[..^1];
        RefreshPreview();
        Backspace?.Invoke();
    }

    void DoSubmit() => Submit?.Invoke();

    void ToggleShift() { _shift = !_shift; RefreshKeys(); }

    void Type(string s)
    {
        if (_shift) { s = s.ToUpper(Tr); _shift = false; RefreshKeys(); }
        if (_showPreview) _text += s;
        RefreshPreview();
        Typed?.Invoke(s);
    }

    void RefreshPreview() => _preview.Text = _text + "▏";

    void RefreshKeys()
    {
        for (var r = 0; r < _rows.Count; r++)
            for (var c = 0; c < _rows[r].Count; c++)
            {
                var (key, box, tb) = _rows[r][c];
                var sel = r == _r && c == _c;
                var isShift = key.Label == "⇧" && _shift;
                var primary = key.Label == "Tamam";

                box.Background = sel ? (Brush)FindResource("Lit")
                    : isShift ? (Brush)FindResource("Accent")
                    : primary ? new SolidColorBrush(Color.FromArgb(0x55, 0x61, 0x78, 0xAD))
                    : (Brush)FindResource("Card");
                box.BorderBrush = sel ? Brushes.White : (Brush)FindResource("CardStroke");
                tb.Foreground = sel ? (Brush)FindResource("Ink") : (Brush)FindResource("Text");
                if (key.Text is { Length: 1 } && key.Special == null)
                    tb.Text = _shift ? key.Text.ToUpper(Tr) : key.Text;
            }
    }

    double CenterX(int r, int c)
    {
        var x = 0.0;
        for (var i = 0; i < c; i++) x += _rows[r][i].key.Width;
        return x + _rows[r][c].key.Width / 2;
    }

    double RowWidth(int r) => _rows[r].Sum(k => k.key.Width);

    void Move(int dr, int dc)
    {
        if (dc != 0)
        {
            _c = Math.Clamp(_c + dc, 0, _rows[_r].Count - 1);
        }
        else
        {
            var nr = Math.Clamp(_r + dr, 0, _rows.Count - 1);
            if (nr != _r)
            {
                // Satırlar ortalı; yatay konuma en yakın tuşa in.
                var cx = CenterX(_r, _c) - RowWidth(_r) / 2;
                var best = 0;
                var bestDist = double.MaxValue;
                for (var i = 0; i < _rows[nr].Count; i++)
                {
                    var d = Math.Abs(CenterX(nr, i) - RowWidth(nr) / 2 - cx);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                _r = nr; _c = best;
            }
        }
        RefreshKeys();
    }

    public bool HandleKey(PadKey key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case PadKey.Up: Move(-1, 0); break;
            case PadKey.Down: Move(1, 0); break;
            case PadKey.Left: Move(0, -1); break;
            case PadKey.Right: Move(0, 1); break;
            case PadKey.A:
                var k = _rows[_r][_c].key;
                if (k.Special != null) k.Special();
                else if (k.Text != null) Type(k.Text);
                break;
            case PadKey.X: DoBackspace(); break;
            case PadKey.Y: ToggleShift(); break;
            case PadKey.Start: DoSubmit(); break;
            case PadKey.B: Close(); break;
        }
        return true;
    }
}

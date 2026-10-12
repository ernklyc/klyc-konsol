using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KlycKonsol.Input;
using KlycKonsol.Library;

namespace KlycKonsol.Ui;

/// <summary>
/// Controller'lar paneli (Ayarlar'dan açılır): bağlı kollar (Xbox 360 / One / Series / XInput uyumlu), kablosuz pil durumu,
/// canlı tuş testi, titreşim testi ve çubuk kayması (drift) kalibrasyonu.
/// ▲▼ kol seç · A titret · X kalibre et · Y ölü bölgeyi sıfırla · B kapat.
/// </summary>
public sealed class ControllerPanel : Border
{
    readonly XInputPad _pad;
    readonly Persist _persist;
    readonly StackPanel _slots = new();
    readonly TextBlock _live = new() { FontSize = 24, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 14, 6, 0), LineHeight = 38 };
    readonly TextBlock _status = new() { FontSize = 22, Margin = new Thickness(6, 8, 6, 0) };
    int _sel;
    bool _busy;
    PadInfo[] _infos = Array.Empty<PadInfo>();
    DateTime _nextInfo;

    public delegate void Persist(int slot, double deadzone);
    public event Action? Closed;
    public bool IsOpen => Visibility == Visibility.Visible;

    public ControllerPanel(XInputPad pad, Persist persist)
    {
        _pad = pad;
        _persist = persist;
        Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0D, 0x10, 0x18));
        CornerRadius = new CornerRadius(30);
        Padding = new Thickness(36, 28, 36, 28);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Width = 1240;
        BorderBrush = (Brush)Application.Current.FindResource("CardStroke");
        BorderThickness = new Thickness(1.5);

        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Controller'lar", FontSize = 40, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock { Text = "Xbox (XInput) kollar ile PlayStation (DualShock 4 / DualSense) ve Switch Pro kolları aynı anda kullanılabilir.", FontSize = 20, Foreground = (Brush)Application.Current.FindResource("TextDim"), Margin = new Thickness(0, 2, 0, 16) });
        root.Children.Add(_slots);
        root.Children.Add(new TextBlock { Text = "CANLI TEST", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.FindResource("TextDim"), Margin = new Thickness(6, 22, 0, 0) });
        root.Children.Add(_live);
        root.Children.Add(_status);
        Child = root;
    }

    public void Open()
    {
        _sel = Math.Max(0, Array.FindIndex(_pad.Describe(), i => i.Connected));
        _status.Text = "";
        Visibility = Visibility.Visible;
        _nextInfo = DateTime.MinValue;
        Refresh(force: true);
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }

    public string Hints => _busy ? "Kalibrasyon sürüyor — çubuklara dokunma…" : "▲▼ Kol seç   A Titret   X Kalibre et   Y Sıfırla   B Kapat";

    void Refresh(bool force = false)
    {
        if (force || DateTime.Now >= _nextInfo) { _infos = _pad.Describe(); _nextInfo = DateTime.Now.AddSeconds(2); RenderSlots(); }

        var s = _pad.State;
        string P(Btn b, string n) => s.Buttons.HasFlag(b) ? $"[{n}]" : "";
        var pressed = string.Join(" ", new[]
        {
            P(Btn.A, "A"), P(Btn.B, "B"), P(Btn.X, "X"), P(Btn.Y, "Y"), P(Btn.LB, "LB"), P(Btn.RB, "RB"),
            P(Btn.Up, "▲"), P(Btn.Down, "▼"), P(Btn.Left, "◀"), P(Btn.Right, "▶"),
            P(Btn.Start, "Start"), P(Btn.Back, "Back"), P(Btn.Guide, "Guide"), P(Btn.LS, "LS"), P(Btn.RS, "RS"),
        }.Where(x => x != ""));
        _live.Text = (pressed == "" ? Loc.T("Bir tuşa bas…") : pressed) +
                     $"\n{Loc.T("Sol çubuk")} ({s.LX / 327:+0;-0;0}, {s.LY / 327:+0;-0;0})   {Loc.T("Sağ çubuk")} ({s.RX / 327:+0;-0;0}, {s.RY / 327:+0;-0;0})   LT {s.LT * 100 / 255}%   RT {s.RT * 100 / 255}%";
    }

    void RenderSlots()
    {
        _slots.Children.Clear();
        foreach (var i in _infos)
        {
            var on = i.Slot == _sel;
            var title = i.Connected
                ? $"Controller {i.Slot + 1}  ·  {Loc.T(i.Kind)}  ·  {Loc.T(i.Wireless ? "Kablosuz" : "Kablolu")}  ·  {Loc.T("Pil")}: {Loc.T(i.Battery)}  ·  {Loc.T("Ölü bölge")}: %{(int)Math.Round(i.Deadzone * 100)}"
                : Loc.F("Controller {0}  ·  bağlı değil", i.Slot + 1);
            var text = new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = on ? (Brush)Application.Current.FindResource("Ink") : i.Connected ? (Brush)Application.Current.FindResource("Text") : (Brush)Application.Current.FindResource("TextDim") };
            _slots.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 14, 22, 14), Margin = new Thickness(0, 3, 0, 3), Child = text,
                Background = on ? (Brush)Application.Current.FindResource("Lit") : (Brush)Application.Current.FindResource("Card"),
            });
        }
    }

    /// <summary>Konsol her karede çağırır (canlı test).</summary>
    public void OnTick()
    {
        if (IsOpen) Refresh();
    }

    public bool HandleKey(PadKey key)
    {
        if (!IsOpen) return false;
        if (_busy) return true;
        switch (key)
        {
            case PadKey.Up: _sel = Math.Max(0, _sel - 1); RenderSlots(); break;
            case PadKey.Down: _sel = Math.Min(3, _sel + 1); RenderSlots(); break;
            case PadKey.B: Close(); break;
            case PadKey.A:
                if (_infos.Length > _sel && _infos[_sel].Connected) _ = _pad.RumbleAsync(_sel);
                else _status.Text = "Bu yuva boş.";
                break;
            case PadKey.Y:
                _pad.Deadzone[_sel] = XInputPad.DefaultDeadzone;
                _persist(_sel, XInputPad.DefaultDeadzone);
                _status.Text = $"Controller {_sel + 1}: ölü bölge varsayılana (%{(int)(XInputPad.DefaultDeadzone * 100)}) döndü.";
                Refresh(force: true);
                break;
            case PadKey.X: _ = CalibrateAsync(); break;
        }
        return true;
    }

    async Task CalibrateAsync()
    {
        if (_infos.Length <= _sel || !_infos[_sel].Connected) { _status.Text = "Bu yuva boş."; return; }
        _busy = true;
        _status.Text = $"Controller {_sel + 1} kalibre ediliyor: çubuklara DOKUNMA (1,5 sn)…";
        var dz = await _pad.CalibrateAsync(_sel);
        _persist(_sel, dz);
        _busy = false;
        _status.Text = $"Controller {_sel + 1}: ölü bölge %{(int)Math.Round(dz * 100)} olarak ayarlandı (çubuk kayması telafi edildi).";
        Refresh(force: true);
    }
}

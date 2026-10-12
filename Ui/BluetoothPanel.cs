using System.Windows;
using System.Windows.Controls;
using KlycKonsol.Input;

namespace KlycKonsol.Ui;

/// <summary>Bluetooth paneli: aç/kapat, eşleşmiş ve yakındaki cihazlar, A eşleştir, X eşlemeyi kaldır, Y yeniden tara.</summary>
public sealed class BluetoothPanel : Grid
{
    readonly IShell _shell;
    readonly ListMenu _menu = new() { Width = 1240, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    List<BtDevice> _devices = new();
    bool? _on;
    string _status = "";
    bool _busy, _noAdapter;

    public event Action? Closed;
    public bool IsOpen => Visibility == Visibility.Visible;

    public BluetoothPanel(IShell shell)
    {
        _shell = shell;
        Visibility = Visibility.Collapsed;
        _menu.Closed += () => { Visibility = Visibility.Collapsed; Closed?.Invoke(); };
        Children.Add(_menu);
    }

    public void Open()
    {
        Visibility = Visibility.Visible;
        _devices = new(); _status = "Taranıyor…"; _noAdapter = false;
        _menu.Open("Bluetooth", BuildRows, select: 1);
        _ = RefreshAsync();
    }

    public void Close() => _menu.Close();

    public bool HandleKey(PadKey key)
    {
        if (key == PadKey.Y && !_busy) { _status = "Taranıyor…"; _ = RefreshAsync(); return true; }
        return _menu.HandleKey(key);
    }

    async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        _menu.Refresh();
        try
        {
            _on = await BluetoothService.IsOnAsync();
            if (_on == null) { _noAdapter = true; _devices = new(); _status = "Bu bilgisayarda Bluetooth bağdaştırıcısı bulunamadı."; }
            else if (_on == false) { _devices = new(); _status = "Bluetooth kapalı."; }
            else
            {
                _devices = await BluetoothService.ScanAsync();
                _status = _devices.Count == 0 ? "Cihaz bulunamadı — cihazı eşleşme moduna al ve Y ile yeniden tara." : "";
            }
        }
        catch (Exception ex) { _status = "Taranamadı: " + ex.Message; }
        finally { _busy = false; }
        if (IsOpen) _menu.Refresh();
    }

    async Task RunAsync(string startText, Func<Task<PairOutcome>> work)
    {
        if (_busy) return;
        _busy = true;
        _status = startText;
        _menu.Refresh();
        try
        {
            var r = await work();
            _shell.Toast(r.Message);
        }
        catch (Exception ex) { _shell.Toast("İşlem başarısız: " + ex.Message); }
        finally { _busy = false; }
        await RefreshAsync();
    }

    List<MenuRow> BuildRows()
    {
        var rows = new List<MenuRow>();
        if (_status.Length > 0) rows.Add(new MenuRow { Text = _status, Header = true });
        if (!_noAdapter)
            rows.Add(new MenuRow
            {
                Text = "Bluetooth", Right = _on == true ? "Açık" : _on == false ? "Kapalı" : "…",
                OnA = () => { if (_on is { } on) _ = RunAsync(on ? "Kapatılıyor…" : "Açılıyor…", async () => new PairOutcome(await BluetoothService.SetOnAsync(!on), !on ? "Bluetooth açıldı" : "Bluetooth kapatıldı")); },
            });

        var paired = _devices.Where(d => d.Paired).ToList();
        var nearby = _devices.Where(d => !d.Paired && d.CanPair).ToList();
        if (paired.Count > 0)
        {
            rows.Add(new MenuRow { Text = "EŞLEŞMİŞ CİHAZLAR  (X: eşlemeyi kaldır)", Header = true });
            foreach (var d in paired)
            {
                var dev = d;
                rows.Add(new MenuRow
                {
                    Text = dev.Name, Right = dev.Connected ? "Bağlı" : "Eşleşmiş · kapalı / menzil dışı",
                    OnA = () => _shell.Toast(dev.Connected ? $"{dev.Name} bağlı" : $"{dev.Name}: cihazı aç, Windows kendiliğinden bağlar"),
                    OnX = () => _ = RunAsync($"{dev.Name} eşlemesi kaldırılıyor…", () => BluetoothService.UnpairAsync(dev)),
                });
            }
        }
        if (nearby.Count > 0)
        {
            rows.Add(new MenuRow { Text = "YAKINDAKİ CİHAZLAR  (A: eşleştir)", Header = true });
            foreach (var d in nearby)
            {
                var dev = d;
                rows.Add(new MenuRow
                {
                    Text = dev.Name, Right = dev.Signal is { } s ? $"sinyal {s} dBm" : "eşleşmeye hazır",
                    Accent = true,
                    OnA = () => _ = RunAsync($"{dev.Name} ile eşleşiliyor…", () => BluetoothService.PairAsync(dev, pin => _shell.Toast($"{dev.Name} PIN: {pin} — cihazda bu PIN'i gir"))),
                });
            }
        }
        if (!_noAdapter) rows.Add(new MenuRow { Text = "Yeniden tara  (Y)", OnA = () => { if (!_busy) { _status = "Taranıyor…"; _ = RefreshAsync(); } } });
        return rows;
    }
}

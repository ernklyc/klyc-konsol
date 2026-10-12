using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KlycKonsol.Input;
using KlycKonsol.Library;

namespace KlycKonsol.Ui;

/// <summary>Hakkında: logo, sürüm, destek (Buy Me a Coffee), proje bağlantıları, güncelleme denetimi ve kol gerektirmeyen kısayollar.</summary>
public sealed class AboutPanel : Grid
{
    public const string CoffeeUrl = "https://buymeacoffee.com/13tpsxlcea";
    public const string RepoUrl = "https://github.com/" + UpdateService.Repo;
    public const string PulseUrl = "https://github.com/ernklyc/klyc-pulse";

    readonly IShell _shell;
    readonly ListMenu _menu = new() { Width = 1240, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    string _updateText = "Denetlenmedi";
    UpdateInfo? _update;
    bool _checking;
    bool _installing;
    string _installText = "";
    static readonly Lazy<bool> Installed = new(() => SelfUpdate.IsInstalled());

    public event Action? Closed;
    public bool IsOpen => Visibility == Visibility.Visible;

    public AboutPanel(IShell shell)
    {
        _shell = shell;
        Visibility = Visibility.Collapsed;
        _menu.SetHeader(BuildHeader());
        _menu.Closed += () => { Visibility = Visibility.Collapsed; Closed?.Invoke(); };
        Children.Add(_menu);
    }

    static UIElement BuildHeader()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        try
        {
            var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Width = 112, Height = 112, Margin = new Thickness(0, 0, 28, 0) };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            row.Children.Add(logo);
        }
        catch { }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new TextBlock { Text = "KLYC", FontSize = 48, FontWeight = FontWeights.Black });
        name.Children.Add(new TextBlock { Text = " KONSOL", FontSize = 48, FontWeight = FontWeights.Black, Foreground = (Brush)Application.Current.FindResource("Accent") });
        text.Children.Add(name);
        text.Children.Add(new TextBlock { Text = $"Sürüm {AppInfo.Version} · MIT lisansı", FontSize = 22, Foreground = (Brush)Application.Current.FindResource("TextDim") });
        text.Children.Add(new TextBlock { Text = "Controller ile PC'yi konsol gibi kullan — oyunlar, tarayıcı, ayarlar tek yerde.", FontSize = 20, Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)Application.Current.FindResource("TextDim") });
        row.Children.Add(text);
        return row;
    }

    public void Open()
    {
        Visibility = Visibility.Visible;
        _menu.Open("", BuildRows, select: 1);
        if (_update == null && !_checking) _ = CheckAsync();
    }

    public void Close() => _menu.Close();

    public bool HandleKey(PadKey key) => _menu.HandleKey(key);

    void OpenUrl(string url)
    {
        _menu.Close();
        _shell.OpenInBrowser(url);
    }

    async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        _updateText = "Denetleniyor…";
        _menu.Refresh();
        try
        {
            var (info, err) = await UpdateService.CheckAsync();
            if (err != null) _updateText = err;
            else if (info != null && UpdateService.IsNewer(info)) { _update = info; _updateText = $"Yeni sürüm: {info.Tag}"; }
            else _updateText = "Güncelsin";
        }
        catch { _updateText = "Denetlenemedi"; }
        finally { _checking = false; }
        if (IsOpen) _menu.Refresh();
    }

    /// <summary>"Sorun bildir": kişisel veri içermeyen günlük paketini masaüstüne yazar ve klasörde gösterir. Hiçbir şey gönderilmez.</summary>
    void CreateSupportBundle()
    {
        try
        {
            var screen = $"{SystemParameters.PrimaryScreenWidth:0}x{SystemParameters.PrimaryScreenHeight:0}";
            var pads = _shell.Pad.Describe().Count(p => p.Connected);
            var info = SupportBundle.BuildInfo(AppInfo.Version, Installed.Value, System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, System.Globalization.CultureInfo.CurrentUICulture.Name, screen, pads,
                PulseLink.StatusText(PulseLink.ReadFeed()), SupportBundle.DescribeState(_shell.State), SupportBundle.DescribeLibrary(_shell.Games), DateTime.Now);
            var path = SupportBundle.Create(info);
            AppLog.Write("sorun bildir", "paket hazırlandı: " + Path.GetFileName(path));
            _menu.Close();
            _shell.Toast("Sorun paketi masaüstüne yazıldı: " + Path.GetFileName(path));
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
            _shell.EnterDesktop();
        }
        catch (Exception ex) { AppLog.Write("sorun bildir", "hata: " + ex.Message); _shell.Toast("Paket hazırlanamadı: " + ex.Message); }
    }

    /// <summary>Kurulu (Setup.exe) Konsol: paketi indirir, kendini kapatıp günceller ve yeniden açılır. Paket bulunamazsa sürüm sayfası açılır.</summary>
    async Task InstallAsync()
    {
        if (_installing || _update == null) return;
        _installing = true;
        try
        {
            _installText = "Hazırlanıyor…"; _menu.Refresh();
            var info = await SelfUpdate.CheckAsync();
            if (info is null) { AppLog.Write("güncelleme", "yeni sürüm var ama kurulum paketi bulunamadı: sürüm sayfası açılıyor"); _installText = ""; OpenUrl(_update.Url); return; }
            await SelfUpdate.ApplyAsync(info, p => Dispatcher.Invoke(() => { _installText = $"İndiriliyor %{p}"; if (IsOpen) _menu.Refresh(); }));
        }
        catch (Exception ex)
        {
            AppLog.Write("güncelleme", "kurulamadı: " + ex.Message);
            _installText = "Kurulamadı (sürüm sayfasından indirebilirsin)";
        }
        finally { _installing = false; if (IsOpen) _menu.Refresh(); }
    }

    List<MenuRow> BuildRows()
    {
        var rows = new List<MenuRow>
        {
            new() { Text = "DESTEK", Header = true },
            new() { Text = "☕  Bir kahve ısmarla", Accent = true, Right = "buymeacoffee.com", OnA = () => OpenUrl(CoffeeUrl) },
            new() { Text = "PROJE", Header = true },
            new() { Text = "GitHub — KLYC Konsol", Right = UpdateService.Repo, OnA = () => OpenUrl(RepoUrl) },
            new() { Text = "KLYC Pulse — kardeş proje (performans, ısı, bakım)", Right = PulseLink.StatusText(PulseLink.ReadFeed()), OnA = () => OpenUrl(PulseUrl) },
            _update != null
                ? (Installed.Value
                    ? new MenuRow { Text = $"⬇  Yeni sürüm var: {_update.Tag} — indir ve kur", Accent = true, Right = _installText, OnA = () => { _ = InstallAsync(); } }
                    : new MenuRow { Text = $"⬇  Yeni sürüm var: {_update.Tag} — sürüm sayfasını aç", Accent = true, OnA = () => OpenUrl(_update.Url) })
                : new MenuRow { Text = "Güncellemeleri denetle", Right = _updateText, OnA = () => { _ = CheckAsync(); } },
            new() { Text = "Her gün yeni sürümü kendiliğinden sor", Right = _shell.State.CheckUpdates ? "Açık" : "Kapalı", OnA = () => { _shell.State.CheckUpdates = !_shell.State.CheckUpdates; _shell.State.Save(); } },
            new() { Text = "Sorun bildir — günlük paketini masaüstüne hazırla", Right = "kişisel veri içermez, kendiliğinden gönderilmez", OnA = CreateSupportBundle },
            new() { Text = "GitHub'da sorun aç", Right = "ernklyc/klyc-konsol/issues", OnA = () => OpenUrl(RepoUrl + "/issues/new") },
            new() { Text = "Veri klasörünü aç (ayarlar, kapaklar, kayıtlar)", OnA = () => { _menu.Close(); try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Dir}\"") { UseShellExecute = true }); } catch { } _shell.EnterDesktop(); } },
            new() { Text = "KOL OLMADAN KULLANIM", Header = true },
            new() { Text = "Ctrl + Alt + K", Right = "Konsol'u aç / gizle (oyun içindeyken de çalışır)" },
            new() { Text = "Ok tuşları · Enter · Esc · Tab", Right = "gezin · seç · geri · menü" },
            new() { Text = "Fare", Right = "tıkla · çift tıkla başlat · tekerlek kaydır · sağ üstte — ve ⏻" },
            new() { Text = "Alt + F4  ya da  bildirim alanı simgesi → Konsol'dan çık", Right = "kapat" },
        };
        return rows;
    }
}

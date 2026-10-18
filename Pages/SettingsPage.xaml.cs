using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KlycKonsol.Input;
using KlycKonsol.Library;
using KlycKonsol.Ui;
using Microsoft.Win32;

namespace KlycKonsol.Pages;

public sealed class SettingItem : INotifyPropertyChanged
{
    public string Title { get; init; } = "";
    public string Caption { get; set; } = "";
    public string Glyph { get; init; } = "";
    public bool IsHeader { get; init; }
    public Action? OnA { get; init; }
    public Action<int>? OnLeftRight { get; init; }

    string _value = "";
    double _bar = -1;
    bool _selected;
    public string Value { get => _value; set { _value = value; Raise(nameof(Value)); } }
    /// <summary>0..1 arası çubuk; -1 = çubuk yok.</summary>
    public double Bar { get => _bar; set { _bar = value; Raise(nameof(BarWidth)); Raise(nameof(BarVisibility)); } }
    public bool IsSelected { get => _selected; set { _selected = value; Raise(nameof(IsSelected)); } }

    public double BarWidth => Math.Max(0, _bar) * 260;
    public Visibility BarVisibility => _bar >= 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HeaderVisibility => IsHeader ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RowVisibility => IsHeader ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class SettingsPage : UserControl, IPage
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "KLYC-Konsol";

    readonly IShell _shell;
    readonly ObservableCollection<SettingItem> _items = new();
    int _index;
    SettingItem _volume = null!, _autostart = null!, _startupPad = null!, _gameBar = null!, _padOnly = null!, _partial = null!, _hidden = null!, _folders = null!, _autoReturn = null!, _powerPlan = null!, _muteToasts = null!, _sounds = null!, _haptics = null!, _language = null!, _guard = null!, _sort = null!, _pulseInfo = null!, _pulseAudit = null!, _pulseMode = null!, _pulseBoost = null!, _pulseClean = null!, _pulseAuto = null!, _pulseHeat = null!, _pulseFan = null!, _shellMode = null!, _desktopWin = null!, _autoLogin = null!, _elevated = null!;
    readonly ControllerPanel _panel;
    readonly WifiPanel _wifi;
    readonly AboutPanel _about;
    readonly BluetoothPanel _bt;
    readonly DevicePanel _devices;
    SettingItem _brightness = null!, _media = null!;

    public SettingsPage(IShell shell)
    {
        _shell = shell;
        InitializeComponent();
        List.ItemsSource = _items;
        _panel = new ControllerPanel(_shell.Pad, (slot, dz) => { _shell.State.Deadzones[slot] = dz; _shell.State.Save(); });
        _panel.Closed += () => { UpdateHints(); };
        Root.Children.Add(_panel);
        _wifi = new WifiPanel(_shell);
        _wifi.Closed += () => { UpdateHints(); };
        Root.Children.Add(_wifi);
        _about = new AboutPanel(_shell);
        _about.Closed += () => { UpdateHints(); };
        Root.Children.Add(_about);
        _bt = new BluetoothPanel(_shell);
        _bt.Closed += () => { UpdateHints(); };
        Root.Children.Add(_bt);
        _devices = new DevicePanel(_shell);
        _devices.Closed += () => { UpdateHints(); };
        Root.Children.Add(_devices);
        Build();
    }

    void Build()
    {
        _volume = new SettingItem
        {
            Title = "Ses", Glyph = "", Caption = "◀ ▶ ile ayarla · A: sessize al",
            OnLeftRight = d => { SetVolume(d * 0.05f); },
            OnA = () => { SystemAudio.Muted = !SystemAudio.Muted; RefreshVolume(); },
        };
        _brightness = new SettingItem
        {
            Title = "Parlaklık", Glyph = "", Caption = "◀ ▶ ile ayarla (dahili ekran)",
            OnLeftRight = d => SetBrightness(d * 10),
            OnA = () => { },
        };
        _media = new SettingItem
        {
            Title = "Medya", Glyph = "", Caption = "◀ önceki · A oynat/duraklat · ▶ sonraki (arka planda çalan müzik/video)",
            OnLeftRight = d => Native.Tap(d < 0 ? Native.VK_MEDIA_PREV : Native.VK_MEDIA_NEXT),
            OnA = () => Native.Tap(Native.VK_MEDIA_PLAY_PAUSE),
        };
        _guard = new SettingItem
        {
            Title = "Oyun sırasında Konsol'u kapat", Glyph = "",
            Caption = "Oyun başlayınca Konsol tamamen kapanır, yerine ~1 MB'lık küçük bekçi kalır (Guide / Ctrl+Alt+K ile geri açılır) — önerilir",
            OnA = () =>
            {
                if (!GuardLauncher.Available) { _shell.Toast("Bekçi (KlycGuard.exe) bulunamadı — Konsol arka planda düşük güçte kalır"); return; }
                _shell.State.UseGuard = !_shell.State.UseGuard; _shell.State.Save(); RefreshToggles();
            },
        };
        _sounds = new SettingItem
        {
            Title = "Arayüz sesleri", Glyph = "", Caption = "Gezinirken ve seçerken kısa sesler (oyun sırasında kapalıdır)",
            OnA = () => { _shell.State.UiSounds = !_shell.State.UiSounds; _shell.State.Save(); UiSound.Enabled = _shell.State.UiSounds; if (UiSound.Enabled) UiSound.Play(Cue.Select); RefreshToggles(); },
        };
        _haptics = new SettingItem
        {
            Title = "Titreşim geri bildirimi", Glyph = "", Caption = "A / B tuşlarında kolda hafif bir titreşim (kablosuz kolda pili biraz harcar)",
            OnA = () => { _shell.State.Haptics = !_shell.State.Haptics; _shell.State.Save(); RefreshToggles(); if (_shell.State.Haptics) _shell.Pad.Pulse(); },
        };
        _autostart = new SettingItem
        {
            Title = "Windows ile başlat", Glyph = "", Caption = "Bilgisayar açılınca doğrudan Konsol açılsın",
            OnA = ToggleAutostart,
        };

        _language = new SettingItem
        {
            Title = "Dil / Language", Glyph = "",
            Caption = "Otomatik: Windows diline göre · Türkçe ya da İngilizce'ye geçmek için A",
            OnA = CycleLanguage,
        };

        _sort = new SettingItem
        {
            Title = "Kütüphane sıralaması", Glyph = "",
            Caption = "◀ ▶ ya da A ile değiştir · Akıllı: favoriler, sonra son oynananlar. Ayrıca Kütüphane'de \"Son oynananlar\" sekmesi var",
            OnA = () => CycleSort(1), OnLeftRight = CycleSort,
        };

        _pulseInfo = new SettingItem
        {
            Title = "Üst çubukta Pulse bilgisi", Glyph = "",
            Caption = "KLYC Pulse çalışıyorsa seçili mod ve (açıksa) işlemci / ekran kartı ısısı üst çubukta görünür. Kapalıyken Pulse Konsol için sensör okumaz",
            OnA = () => { _shell.State.ShowPulseInfo = !_shell.State.ShowPulseInfo; _shell.State.Save(); _shell.ApplyPulseInfoSetting(); RefreshToggles(); },
        };
        _pulseMode = new SettingItem
        {
            Title = "Pulse modu", Glyph = "",
            Caption = "◀ ▶ ile mod seç, A ile uygula (KLYC Pulse'ta \"Konsol'dan mod değiştirmeye izin ver\" açık olmalı)",
            OnLeftRight = d => StepPulseMode(d),
            OnA = ApplyPulseMode,
        };
        _pulseBoost = new SettingItem
        {
            Title = "Hızlandır", Glyph = "",
            Caption = "Pulse'un Hızlandır'ını çalıştırır: zararsız geçici dosyaları temizler, belleği rahatlatır, mod ayarlarını doğrular (hiçbir uygulamayı kapatmaz)",
            OnA = () => _shell.SendPulseCommand("boost", null),
        };

        _pulseClean = new SettingItem
        {
            Title = "Temizlik", Glyph = "",
            Caption = "Pulse'un zararsız geçici dosya temizliğini çalıştırır (belgelerine, oyunlarına, indirilenlerine dokunmaz)",
            OnA = () => _shell.SendPulseCommand("clean", null),
        };
        _pulseAuto = new SettingItem
        {
            Title = "Oyun açılınca otomatik mod", Glyph = "",
            Caption = "A ile aç/kapat: oyun açılınca Pulse modu kendiliğinden Oyun'a alır, oyun bitince geri döner",
            OnA = () => _shell.SendPulseCommand("autogame", null, _shell.LatestPulse?.AutoGameMode == true ? "off" : "on"),
        };
        _pulseHeat = new SettingItem
        {
            Title = "Isı hedefi", Glyph = "",
            Caption = "◀ ▶ ile seç, A ile uygula: işlemci/ekran kartı bu sıcaklığı geçerse Pulse hızı küçük adımlarla kısar (Kapalı · 75 · 80 · 85 · 90 °C)",
            OnLeftRight = d => StepHeat(d), OnA = ApplyHeat,
        };
        _pulseAudit = new SettingItem
        {
            Title = "Sistem denetimi", Glyph = "",
            Caption = "Pulse'un salt okunur denetimi: sayfa dosyası, ekran kartı belleği, ısı, arka plan yükü… Neyi neden söylediğini gösterir, hiçbir ayarı değiştirmez",
            OnA = () => _shell.ShowInfo("Sistem denetimi", PulseLink.AuditText(_shell.LatestPulse)),
        };
        _pulseFan = new SettingItem
        {
            Title = "Fan desteği", Glyph = "",
            Caption = "◀ kapat · ▶ aç: bilgisayar destekliyorsa Pulse fanı hızlandırır",
            OnLeftRight = d => _shell.SendPulseCommand("fan", null, d > 0 ? "on" : "off"),
            OnA = () => _shell.SendPulseCommand("fan", null, "on"),
        };

        _shellMode = new SettingItem
        {
            Title = "Konsol modu (Windows masaüstü yerine)", Glyph = "",
            Caption = "Bilgisayar açılınca (oturum açılınca) Windows masaüstü yerine doğrudan Konsol gelir — konsol gibi. Sorun olursa kendiliğinden masaüstüne döner; istediğinde buradan kapatırsın",
            OnA = ToggleShellMode,
        };
        _desktopWin = new SettingItem
        {
            Title = "Windows masaüstüne geç", Glyph = "",
            Caption = "Masaüstünü ve görev çubuğunu açar, kol fare olur (Konsol'a dönmek: Guide ya da Ctrl+Alt+K)",
            OnA = () => _shell.EnterDesktop(),
        };
        _autoLogin = new SettingItem
        {
            Title = "Şifresiz otomatik oturum açma", Glyph = "",
            Caption = "Windows'un hesap penceresini açar: \"Kullanıcıların bu bilgisayarı kullanması için...\" kutusunu kaldırıp şifreni bir kez girersin. Windows güvenlik ayarıdır, Konsol şifre girmez",
            OnA = OpenAutoLogin,
        };
        _elevated = new SettingItem
        {
            Title = "Yönetici olarak başlat (UAC'siz)", Glyph = "",
            Caption = "Kolla yönetici onay penceresine cevap verilemez; Konsol yönetici yetkisiyle açılırsa başlattıkların da sormaz. Dikkat: tarayıcı ve oyunlar da yönetici yetkisiyle çalışır. Açıp kapatmak bir kez klavye/fareyle onay ister",
            OnA = ToggleElevated,
        };

        _startupPad = new SettingItem
        {
            Title = "Açılışta kol yoksa gizli bekle", Glyph = "",
            Caption = "Windows ile başlarken kol takılı değilse Konsol ekranı örtmez (fare/klavyeyle kullanırsın); kolu takıp Guide'a basınca açılır. Kapalıysa her açılışta ekranı kaplar",
            OnA = () => { _shell.State.StartupNeedsPad = !_shell.State.StartupNeedsPad; _shell.State.Save(); RefreshToggles(); },
        };

        _gameBar = new SettingItem
        {
            Title = "Xbox tuşu Game Bar'ı açsın", Glyph = "",
            Caption = "Kapalı olursa orta (Guide) tuşu sadece Konsol'u açıp kapatır — önerilir",
            OnA = ToggleGameBar,
        };

        _padOnly = new SettingItem
        {
            Title = "Sadece kol destekli oyunlar", Glyph = "",
            Caption = "Kütüphane'de yalnızca controller ile oynanabilen oyunlar görünsün (Steam verisi)",
            OnA = () => { _shell.State.ControllerOnly = !_shell.State.ControllerOnly; _shell.State.Save(); RefreshToggles(); _shell.RefreshLibraryViews(); },
        };
        _partial = new SettingItem
        {
            Title = "Kısmi kol desteğini de göster", Glyph = "",
            Caption = "Bazı menülerde klavye/fare gerektiren ama oynanabilen oyunlar",
            OnA = () => { _shell.State.IncludePartial = !_shell.State.IncludePartial; _shell.State.Save(); RefreshToggles(); _shell.RefreshLibraryViews(); },
        };

        _powerPlan = new SettingItem
        {
            Title = "Oyun başlarken güç planı", Glyph = "",
            Caption = "Prizdeyken Yüksek/Nihai performansa geçer, oyun bitince eskisine döner (oyuna özel kapatılabilir). KLYC Pulse çalışıyorsa güç planını Pulse yönetir, Konsol dokunmaz",
            OnA = () => { _shell.State.PowerPlanOnGame = !_shell.State.PowerPlanOnGame; _shell.State.Save(); RefreshToggles(); },
        };
        _muteToasts = new SettingItem
        {
            Title = "Oyun sırasında bildirimleri sustur", Glyph = "",
            Caption = "Windows bildirimleri oyun boyunca kapanır, oyun bitince geri açılır",
            OnA = () => { _shell.State.MuteNotificationsOnGame = !_shell.State.MuteNotificationsOnGame; _shell.State.Save(); RefreshToggles(); },
        };
        _autoReturn = new SettingItem
        {
            Title = "Oyun kapanınca Konsol'a dön", Glyph = "",
            Caption = "Oyunu kapatınca Konsol kendiliğinden öne gelir (oyun sırasında ek yük yok)",
            OnA = () => { _shell.State.AutoReturn = !_shell.State.AutoReturn; _shell.State.Save(); RefreshToggles(); },
        };
        _hidden = new SettingItem
        {
            Title = "Gizlenen oyunlar", Glyph = "",
            Caption = "Kütüphane menüsünden (Start) gizlediklerin; A ile hepsini geri getir",
            OnA = () => { _shell.State.Hidden.Clear(); _shell.State.Save(); RefreshToggles(); _shell.RefreshLibraryViews(); _shell.Toast("Gizlenen oyunlar geri getirildi"); },
        };
        _folders = new SettingItem
        {
            Title = "Oyun kısayol klasörü", Glyph = "",
            Caption = "Masaüstü'ndeki Game gibi klasörlerdeki .url / .lnk kısayolları otomatik eklenir; A ile klasörü aç",
            OnA = OpenGameFolder,
        };

        _items.Add(new SettingItem { Title = "SİSTEM", IsHeader = true });
        _items.Add(new SettingItem { Title = "Controller'lar", Glyph = "", Caption = "Bağlı kollar (Xbox 360 dahil), pil, titreşim testi, çubuk kayması kalibrasyonu", OnA = () => { _panel.Open(); UpdateHints(); } });
        _items.Add(_volume);
        _items.Add(_brightness);
        _items.Add(_media);
        _items.Add(_sounds);
        _items.Add(_haptics);
        _items.Add(_language);
        _items.Add(new SettingItem { Title = "Masaüstü Modu", Glyph = "", Caption = "Controller fare ve klavye olur (Guide: Konsol'a dön)", OnA = _shell.EnterDesktop });
        _items.Add(new SettingItem { Title = "Wi‑Fi", Glyph = "", Caption = "Ağları listele, bağlan, şifre gir (ekran klavyesiyle)", OnA = () => { _wifi.Open(); UpdateHints(); } });
        _items.Add(new SettingItem { Title = "Bluetooth", Glyph = "", Caption = "Cihaz ara, eşleştir (kol, kulaklık, hoparlör), eşlemeyi kaldır", OnA = () => { _bt.Open(); UpdateHints(); } });
        _items.Add(new SettingItem { Title = "Ekran ve ses", Glyph = "", Caption = "Ses çıkışını değiştir (kulaklık, hoparlör, TV) · çözünürlük ve yenileme hızı (15 sn içinde onaylamazsan eski ayara döner)", OnA = () => { _devices.Open(); UpdateHints(); } });
        _items.Add(_autostart);
        _items.Add(_shellMode);
        _items.Add(_desktopWin);
        _items.Add(_autoLogin);
        _items.Add(_elevated);
        _items.Add(_startupPad);
        _items.Add(_gameBar);

        _items.Add(new SettingItem { Title = "KÜTÜPHANE", IsHeader = true });
        _items.Add(_autoReturn);
        _items.Add(_guard);
        _items.Add(_powerPlan);
        _items.Add(_muteToasts);
        _items.Add(_sort);
        _items.Add(_padOnly);
        _items.Add(_partial);
        _items.Add(_folders);
        _items.Add(_hidden);
        _items.Add(new SettingItem { Title = "Kütüphaneyi yenile", Glyph = "", Caption = "Steam / Epic oyunlarını yeniden tara", OnA = () => { _ = _shell.ReloadAsync(); _shell.Toast("Kütüphane yenileniyor…"); } });
        _items.Add(new SettingItem { Title = "Oyun / uygulama ekle", Glyph = "", Caption = "library.json dosyasını Not Defteri ile aç", OnA = OpenLibraryFile });

        _items.Add(new SettingItem { Title = "PULSE", IsHeader = true });
        _items.Add(_pulseInfo);
        _items.Add(_pulseMode);
        _items.Add(_pulseBoost);
        _items.Add(_pulseClean);
        _items.Add(_pulseAuto);
        _items.Add(_pulseHeat);
        _items.Add(_pulseFan);
        _items.Add(_pulseAudit);

        _items.Add(new SettingItem { Title = "GÜÇ", IsHeader = true });
        _items.Add(new SettingItem { Title = "Uyku", Glyph = "", Caption = "Bilgisayarı uyut", OnA = () => Native.SetSuspendState(false, false, false) });
        _items.Add(new SettingItem { Title = "Yeniden Başlat", Glyph = "", Caption = "Bilgisayarı yeniden başlat", OnA = () => _shell.Confirm("Bilgisayar yeniden başlatılsın mı?", () => Process.Start("shutdown", "/r /t 0")) });
        _items.Add(new SettingItem { Title = "Kapat", Glyph = "", Caption = "Bilgisayarı kapat", OnA = () => _shell.Confirm("Bilgisayar kapatılsın mı?", () => Process.Start("shutdown", "/s /t 0")) });
        _items.Add(new SettingItem { Title = "Hakkında", Glyph = "", Caption = "Sürüm " + AppInfo.Version + " · destek, bağlantılar, güncelleme, kol olmadan kullanım", Value = AppInfo.Version, OnA = () => { _about.Open(); UpdateHints(); } });
        _items.Add(new SettingItem { Title = "Konsol'dan çık", Glyph = "", Caption = "KLYC Konsol'u kapat", OnA = () => _shell.Confirm("KLYC Konsol kapatılsın mı?", _shell.Exit) });

        _index = 1;
    }

    public void OnLibraryChanged() { }

    void UpdateHints()
    {
        if (_devices.IsOpen) _shell.SetHints(("▲▼", "Seç"), ("◀ ▶", "Değiştir"), ("A", "Uygula / seç"), ("B", "Geri"));
        else if (_bt.IsOpen) _shell.SetHints(("▲▼", "Seç"), ("A", "Eşleştir / aç"), ("X", "Eşlemeyi kaldır"), ("Y", "Yeniden tara"), ("B", "Geri"));
        else if (_about.IsOpen) _shell.SetHints(("▲▼", "Seç"), ("A", "Aç"), ("B", "Geri"));
        else if (_wifi.KeyboardOpen) _shell.SetHints(("A", "Yaz"), ("X", "Sil"), ("Y", "Büyük harf"), ("Start", "Bağlan"), ("B", "Vazgeç"));
        else if (_wifi.IsOpen) _shell.SetHints(("▲▼", "Ağ seç"), ("A", "Bağlan"), ("X", "Bağlantıyı kes"), ("Y", "Yenile"), ("Start", "Unut"), ("B", "Kapat"));
        else if (_panel.IsOpen) _shell.SetHints(("▲▼", "Kol seç"), ("A", "Titret"), ("X", "Kalibre et"), ("Y", "Sıfırla"), ("B", "Kapat"));
        else _shell.SetHints(("A", "Seç"), ("◀ ▶", "Ayarla"), ("B", "Sekmeler"), ("LB RB", "Sekme değiştir"));
    }

    public void OnTick(PadState state, double dt) => _panel.OnTick();

    public void OnEnter()
    {
        UpdateHints();
        RefreshVolume();
        RefreshBrightness();
        _autostart.Value = IsAutostart() ? "Açık" : "Kapalı";
        _gameBar.Value = GameBarOpensOnGuide() ? "Açık" : "Kapalı";
        RefreshToggles();
        Select(animate: false);
        _shell.SetBackdrop(null);
    }

    public void OnLeave() { if (_panel.IsOpen) _panel.Close(); if (_wifi.IsOpen) _wifi.Close(); if (_about.IsOpen) _about.Close(); if (_bt.IsOpen) _bt.Close(); if (_devices.IsOpen) _devices.Close(); }

    public bool OnKey(PadKey key)
    {
        if (_devices.IsOpen) { var dv = _devices.HandleKey(key); UpdateHints(); return dv; }
        if (_bt.IsOpen) { var bt = _bt.HandleKey(key); UpdateHints(); return bt; }
        if (_about.IsOpen) { var ab = _about.HandleKey(key); UpdateHints(); return ab; }
        if (_wifi.IsOpen) { var w = _wifi.HandleKey(key); UpdateHints(); return w; }
        if (_panel.IsOpen) { var r = _panel.HandleKey(key); if (_panel.IsOpen) UpdateHints(); return r; }
        switch (key)
        {
            case PadKey.Up:
                if (!Step(-1)) _shell.FocusNav();
                return true;
            case PadKey.Down: Step(1); return true;
            case PadKey.Left: _items[_index].OnLeftRight?.Invoke(-1); return true;
            case PadKey.Right: _items[_index].OnLeftRight?.Invoke(1); return true;
            case PadKey.A: _items[_index].OnA?.Invoke(); return true;
            case PadKey.B: _shell.FocusNav(); return true;
        }
        return false;
    }

    bool Step(int dir)
    {
        var i = _index;
        do { i += dir; } while (i >= 0 && i < _items.Count && _items[i].IsHeader);
        if (i < 0 || i >= _items.Count) return false;
        _index = i;
        if (PulseRows.Contains(_items[_index])) RefreshPulse();   // Pulse özeti güncel gösterilsin
        Select();
        return true;
    }

    void Select(bool animate = true)
    {
        for (var i = 0; i < _items.Count; i++) _items[i].IsSelected = i == _index;

        // Seçili satır görünür kalsın: gerçek satır konumundan hesapla (liste kendi yüksekliğinden taşar, bu yüzden Height verilir).
        List.Height = _items.Sum(i => i.IsHeader ? 70 : 110) + 40;
        double y = 0;
        if (List.ItemContainerGenerator.ContainerFromIndex(_index) is FrameworkElement el && el.IsArrangeValid)
        {
            try { y = el.TranslatePoint(new Point(0, 0), List).Y; } catch { }
        }
        else
        {
            for (var i = 0; i < _index; i++) y += _items[i].IsHeader ? 70 : 110;
        }
        var maxOffset = Math.Max(0, List.Height - Port.ActualHeight);
        var offset = Math.Clamp(y - 240, 0, maxOffset);
        ScrollAnim.Slide((TranslateTransform)List.RenderTransform, TranslateTransform.YProperty, -offset, false);
    }

    // ---- Eylemler -------------------------------------------------------

    void SetVolume(float delta)
    {
        var v = SystemAudio.Volume;
        if (v == null) return;
        if (SystemAudio.Muted && delta > 0) SystemAudio.Muted = false;
        SystemAudio.Volume = Math.Clamp(v.Value + delta, 0f, 1f);
        RefreshVolume();
    }

    int? _brightnessCache;

    /// <summary>WMI sorgusu ~30 ms sürer: arayüz iş parçacığında değil arka planda yapılır.</summary>
    void SetBrightness(int delta)
    {
        if (_brightnessCache is not { } cur) return;
        var next = Math.Clamp(cur + delta, 5, 100);
        _brightnessCache = next;
        ShowBrightness(next);
        _ = Task.Run(() => Brightness.Set(next));
    }

    void RefreshBrightness() => _ = RefreshBrightnessAsync();

    async Task RefreshBrightnessAsync()
    {
        var v = await Task.Run(() => Brightness.Level);
        _brightnessCache = v;
        ShowBrightness(v);
    }

    void ShowBrightness(int? v)
    {
        if (v is { } l) { _brightness.Bar = l / 100.0; _brightness.Value = $"%{l}"; }
        else { _brightness.Bar = -1; _brightness.Value = "Desteklenmiyor"; _brightness.Caption = "Bu ekran Windows'tan ayarlanamıyor (harici monitörde ekranın kendi tuşlarını kullan)"; }
    }

    void RefreshVolume()
    {
        var v = SystemAudio.Volume;
        if (v == null) { _volume.Value = "—"; _volume.Bar = -1; return; }
        var muted = SystemAudio.Muted;
        _volume.Bar = muted ? 0 : v.Value;
        _volume.Value = muted ? "Sessiz" : $"%{(int)Math.Round(v.Value * 100)}";
    }

    static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) != null;
    }

    void ToggleAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
            if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
            else k.SetValue(RunName, StartupPolicy.RunCommand(Environment.ProcessPath!));
        }
        catch { _shell.Toast("Ayar değiştirilemedi"); }
        _autostart.Value = IsAutostart() ? "Açık" : "Kapalı";
    }

    // ---- Pulse ile birlikte ----------------------------------------------

    string? _pendingPulseMode;   // ◀ ▶ ile seçilen, henüz uygulanmamış mod anahtarı

    void StepPulseMode(int dir)
    {
        var modes = _shell.LatestPulse is { AcceptsCommands: true, Modes: { Count: > 0 } m } && PulseLink.IsAlive(_shell.LatestPulse) ? m : null;
        if (modes == null) { RefreshPulse(); return; }
        var cur = _pendingPulseMode ?? _shell.LatestPulse!.ModeKey;
        var i = Math.Max(0, modes.FindIndex(x => x.Key == cur));
        _pendingPulseMode = modes[(i + dir + modes.Count) % modes.Count].Key;
        RefreshPulse();
    }

    void ApplyPulseMode()
    {
        var feed = _shell.LatestPulse;
        var key = _pendingPulseMode ?? feed?.ModeKey;
        if (key == null) { _shell.SendPulseCommand("mode", null); return; }   // neden bildirilir (Pulse yok / izin kapalı)
        _shell.SendPulseCommand("mode", key);
        _pendingPulseMode = null;
    }

    IEnumerable<SettingItem> PulseRows => new[] { _pulseMode, _pulseBoost, _pulseClean, _pulseAuto, _pulseHeat, _pulseFan, _pulseAudit };

    static readonly int[] HeatSteps = { 0, 75, 80, 85, 90 };
    int? _pendingHeat;

    void StepHeat(int dir)
    {
        var feed = _shell.LatestPulse;
        if (!PulseLink.IsAlive(feed) || feed is not { AcceptsCommands: true }) { RefreshPulse(); return; }
        var cur = _pendingHeat ?? feed.HeatTarget ?? 0;
        var i = Math.Max(0, Array.IndexOf(HeatSteps, cur));
        _pendingHeat = HeatSteps[(i + dir + HeatSteps.Length) % HeatSteps.Length];
        RefreshPulse();
    }

    void ApplyHeat()
    {
        var feed = _shell.LatestPulse;
        var v = _pendingHeat ?? feed?.HeatTarget ?? 0;
        _shell.SendPulseCommand("heat", null, v.ToString());
        _pendingHeat = null;
    }

    static string HeatLabel(int? t) => t is null or 0 ? "Kapalı" : $"{t} °C";

    /// <summary>Pulse satırlarının değer/açıklamasını güncel özete göre kurar.</summary>
    void RefreshPulse()
    {
        _pulseInfo.Value = _shell.State.ShowPulseInfo ? "Açık" : "Kapalı";
        var feed = _shell.LatestPulse;
        if (!PulseLink.IsAlive(feed)) { foreach (var r in PulseRows) r.Value = "Pulse çalışmıyor"; return; }
        if (feed is not { AcceptsCommands: true }) { foreach (var r in PulseRows) r.Value = "İzin kapalı"; return; }
        var curTitle = feed.ModeTitle ?? PulseLink.ModeName(feed.ModeKey);
        var pend = _pendingPulseMode is { } k && k != feed.ModeKey ? feed.Modes?.FirstOrDefault(x => x.Key == k)?.Title ?? k : null;
        _pulseMode.Value = pend == null ? (curTitle.Length > 0 ? curTitle : "—") : $"{curTitle} → {pend}";
        _pulseBoost.Value = "A"; _pulseClean.Value = "A"; _pulseFan.Value = "◀ ▶";
        _pulseAudit.Value = feed.Audit is { } au ? (au.Count(a => a.Severity > 0) is var n and > 0 ? $"{n} bulgu" : "Sorun yok") : "A";
        _pulseAuto.Value = feed.AutoGameMode ? "Açık" : "Kapalı";
        var heatNow = HeatLabel(feed.HeatTarget);
        _pulseHeat.Value = _pendingHeat is { } ph && ph != (feed.HeatTarget ?? 0) ? $"{heatNow} → {HeatLabel(ph)}" : heatNow;
    }

    // ---- Konsol modu / yönetici / otomatik oturum ---------------------------

    bool? _elevatedKnown;

    void ToggleShellMode()
    {
        if (ShellMode.IsEnabled())
        {
            _shell.Confirm("Konsol modu kapatılsın mı? Bir sonraki oturum açışında Windows masaüstü gelir.", () =>
            {
                var (ok, msg) = ShellMode.Disable(_shell.State);
                _shell.Toast(msg); AppLog.Write("kabuk", "kapatıldı: " + ok); RefreshToggles();
            });
            return;
        }
        _shell.Confirm("Konsol modu açılsın mı? Bilgisayar açılınca Windows masaüstü yerine doğrudan Konsol gelir. Sorun olursa ya da Safe Mode'da kendiliğinden masaüstüne döner; istediğinde buradan kapatırsın.", () =>
        {
            var proc = Environment.ProcessPath ?? "";
            var task = ElevatedStart.IsEnabled() ? ElevatedStart.TaskName : null;
            var (ok, msg) = ShellMode.Enable(_shell.State, proc, task);
            _shell.Toast(msg); AppLog.Write("kabuk", "açıldı: " + ok + " " + msg); RefreshToggles();
        });
    }

    void OpenAutoLogin()
    {
        _shell.Confirm("Windows'un hesap penceresi açılacak (kol fare olur). Şifre ekranını atlamak için \"Kullanıcıların bu bilgisayarı kullanması için...\" kutusunu kaldır. Devam?", () =>
        {
            _shell.EnterDesktop();
            try { Process.Start(new ProcessStartInfo("netplwiz.exe") { UseShellExecute = true }); AppLog.Write("kabuk", "netplwiz açıldı"); }
            catch (Exception ex) { _shell.Toast($"Açılamadı: {ex.Message}"); }
        });
    }

    void ToggleElevated()
    {
        var on = _elevatedKnown ?? ElevatedStart.IsEnabled();
        var exe = ShellMode.StableKonsolExe(Environment.ProcessPath ?? "");
        _shell.Confirm(on ? "Yönetici olarak başlatma kapatılsın mı? (Yönetici onayı penceresi çıkar; klavye/fare gerekir)" : "Konsol yönetici yetkisiyle başlatılsın mı? Yönetici onayı penceresi çıkacak (klavye/fare gerekir). Tarayıcı ve oyunlar da yönetici yetkisiyle çalışır.", () =>
        {
            _shell.Toast("Yönetici onayı bekleniyor…");
            _ = Task.Run(() =>
            {
                var ok = on ? ElevatedStart.Disable(exe) : ElevatedStart.Enable(exe);
                Dispatcher.BeginInvoke(() =>
                {
                    _elevatedKnown = ElevatedStart.IsEnabled(); RefreshToggles();
                    _shell.Toast(ok ? (on ? "Yönetici olarak başlatma kapatıldı" : "Konsol bir sonraki oturum açışında yönetici yetkisiyle açılır") : "Yapılamadı (onay verilmedi ya da engellendi)");
                    AppLog.Write("yönetici", (on ? "kapat" : "aç") + " → " + ok);
                });
            });
        });
    }

    void CycleSort(int dir)
    {
        var modes = LibrarySorter.Modes;
        var i = Array.IndexOf(modes, _shell.State.LibrarySort); if (i < 0) i = 0;
        _shell.State.LibrarySort = modes[(i + dir + modes.Length) % modes.Length];
        _shell.State.Save();
        RefreshToggles();
        _shell.RefreshLibraryViews();
    }

    /// <summary>Otomatik → Türkçe → English → Otomatik. Türkçe'den İngilizce'ye geçiş anında uygulanır; geri dönüş (çevrilmiş metinler yerinde kaldığı için) yeniden açılışta.</summary>
    void CycleLanguage()
    {
        var next = _shell.State.Language switch { "tr" => "en", "en" => "auto", _ => "tr" };
        _shell.State.Language = next; _shell.State.Save();
        RefreshToggles();
        var wasEnglish = Loc.IsEnglish;
        Loc.Configure(next);
        if (Loc.IsEnglish && !wasEnglish) _shell.RestartLocalizer();
        else if (wasEnglish != Loc.IsEnglish) _shell.Toast("Dil Konsol yeniden açılınca değişir · Language changes when Konsol restarts");
    }

    void RefreshToggles()
    {
        _sort.Value = Loc.T(LibrarySorter.Label(_shell.State.LibrarySort));
        RefreshPulse();
        _shellMode.Value = ShellMode.IsEnabled() ? "Açık" : "Kapalı";
        _elevated.Value = _elevatedKnown ?? (_elevatedKnown = ElevatedStart.IsEnabled()) is true ? "Açık" : "Kapalı";
        _language.Value = _shell.State.Language switch { "tr" => "Türkçe", "en" => "English", _ => "Otomatik" };
        _guard.Value = !GuardLauncher.Available ? "Yok" : _shell.State.UseGuard ? "Açık" : "Kapalı";
        _sounds.Value = _shell.State.UiSounds ? "Açık" : "Kapalı";
        _haptics.Value = _shell.State.Haptics ? "Açık" : "Kapalı";
        _padOnly.Value = _shell.State.ControllerOnly ? "Açık" : "Kapalı";
        _autoReturn.Value = _shell.State.AutoReturn ? "Açık" : "Kapalı";
        _startupPad.Value = _shell.State.StartupNeedsPad ? "Açık" : "Kapalı";
        _powerPlan.Value = _shell.State.PowerPlanOnGame ? "Açık" : "Kapalı";
        _muteToasts.Value = _shell.State.MuteNotificationsOnGame ? "Açık" : "Kapalı";
        _partial.Value = _shell.State.IncludePartial ? "Açık" : "Kapalı";
        _hidden.Value = _shell.State.Hidden.Count == 0 ? "Yok" : $"{_shell.State.Hidden.Count} oyun";
        var nf = ShortcutScanner.DefaultFolders().Concat(_shell.State.GameFolders).Count(Directory.Exists);
        _folders.Value = nf > 0 ? $"{nf} klasör" : "Bulunamadı";
    }

    static bool GameBarOpensOnGuide() => WindowsSettings.GameBarOpensOnGuide();

    void ToggleGameBar()
    {
        if (!WindowsSettings.SetGameBarOpensOnGuide(!GameBarOpensOnGuide())) _shell.Toast("Ayar değiştirilemedi");
        _gameBar.Value = GameBarOpensOnGuide() ? "Açık" : "Kapalı";
        _shell.Toast(GameBarOpensOnGuide() ? "Xbox tuşu Game Bar'ı açacak" : "Xbox tuşu artık Game Bar'ı açmayacak");
    }

    void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Dir}\"") { UseShellExecute = true }); } catch { }
        _shell.EnterDesktop();
    }

    void OpenGameFolder()
    {
        var dir = ShortcutScanner.DefaultFolders().Concat(_shell.State.GameFolders).FirstOrDefault(Directory.Exists);
        if (dir == null) { _shell.Toast("Masaüstünde 'Game' klasörü bulunamadı"); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); } catch { }
        _shell.EnterDesktop();
    }

    void OpenSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        _shell.EnterDesktop();
    }

    void OpenLibraryFile()
    {
        UserLibrary.Load();
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppPaths.LibraryFile}\"") { UseShellExecute = true }); } catch { }
        _shell.EnterDesktop();
    }
}

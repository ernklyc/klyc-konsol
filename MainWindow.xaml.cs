using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KlycKonsol.Input;
using KlycKonsol.Library;
using KlycKonsol.Models;
using KlycKonsol.Pages;
using KlycKonsol.Ui;

namespace KlycKonsol;

public partial class MainWindow : Window, IShell
{
    enum Mode { Launcher, Away, Desktop }
    enum OverlayMode { None, Busy, Confirm, Summary }

    const int BrowserTab = 1;

    readonly XInputPad _pad = new();
    readonly DesktopMode _mouse = new();
    readonly AppState _state = AppState.Load();
    readonly DesktopHint _desktopHint = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?> _heroCache = new();
    readonly IPage[] _pages;
    List<Game> _games = new();
    List<Game> _installed = new(), _owned = new(), _free = new();
    IReadOnlyDictionary<string, SteamLocal.AppUsage> _usage = new Dictionary<string, SteamLocal.AppUsage>();
    CancellationTokenSource? _extrasCts;
    TrayIcon? _tray;
    bool _exiting, _padSeen, _handoff;
    GuardArgs _guardArgs = new(GuardStart.None, "", 0, 0, false);
    GuardInfo? _adopted;

    /// <summary>Açılıştan 30 sn sonra (Konsol açıksa) günde en fazla bir kez yeni sürüm sorar; hiçbir şey indirmez.</summary>
    async Task AutoCheckUpdateAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            if (!_state.CheckUpdates || _mode != Mode.Launcher) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - _state.LastUpdateCheck < 23 * 3600) return;
            var (info, _) = await UpdateService.CheckAsync();
            _state.LastUpdateCheck = now;
            _state.Save();
            if (info != null && UpdateService.IsNewer(info) && _state.DismissedUpdate != info.Tag)
            {
                _state.DismissedUpdate = info.Tag;   // aynı sürüm için bir kez haber ver
                _state.Save();
                Toast($"Yeni sürüm var: {info.Tag} — Ayarlar → Hakkında");
            }
        }
        catch { }
    }

    // ---- Bekçi (oyun sırasında Konsol'un yerine geçen küçük süreç) ------------------------

    /// <summary>Açılış argümanlarından ya da çalışan bir bekçiden oturumu belirler; bekçi varsa görevini bırakmasını ister.</summary>
    void InitGuardState()
    {
        _guardArgs = GuardLauncher.ParseArgs(Environment.GetCommandLineArgs());
        var alive = GuardLauncher.IsRunning();
        if (_guardArgs.Kind == GuardStart.GameExited) { GameSession.End(_state); GuardLauncher.Save(null); }
        else if (_guardArgs.Kind == GuardStart.Resume || alive)
        {
            var info = GuardLauncher.Load();
            if (info is { Dir.Length: > 0 }) _adopted = info with { FirstSeen = _guardArgs.FirstSeen > 0 ? _guardArgs.FirstSeen : info.FirstSeen };
        }
        if (_guardArgs.Kind != GuardStart.None || alive) GuardLauncher.StopRunning();
    }

    /// <summary>Kütüphane yüklendikten sonra: bekçinin bildirdiği "oyun bitti" ya da sürmekte olan oyun oturumunu işle.</summary>
    void ApplyGuardStart()
    {
        if (_guardArgs.Kind == GuardStart.GameExited)
        {
            var g = _games.FirstOrDefault(x => x.Id == _guardArgs.GameId);
            if (g != null)
            {
                var played = TimeSpan.FromSeconds(_guardArgs.PlayedSeconds);
                if (_guardArgs.PlayedSeconds == 0)
                {
                    // Bekçi oyun sürecini hiç görmedi (ör. Steam/EA uygulaması açılıp kapandı): kullanıcı boşlukta kalmasın, ne olduğunu söyle
                    Toast($"{g.Title} başlamadı ya da hemen kapandı. Steam / EA / Epic uygulamasını açıp oyunu oradan bir kez başlatmayı dene (giriş ya da güncelleme gerekebilir).");
                    return;
                }
                RecordPlayTime(g, played);
                // Bekçiyle başlayan oyunlarda (varsayılan yol) oyun bitince Konsol yeniden açılır; özet paneli ve Pulse raporu burada gösterilir
                if (played >= SummaryMinPlayed) { ShowSessionSummary(g, played); _ = PulseReportAsync(g, played); }
                else Toast($"{g.Title} kapandı");
            }
            else SessionFinisher.RecordPlay(_state, _guardArgs.GameId, _guardArgs.PlayedSeconds);
            return;
        }
        if (_adopted is not { } a) return;

        // Oyun sürüyor: oturumu devral, kapanışını bu süreç izlesin.
        var game = _games.FirstOrDefault(x => x.Id == a.GameId);
        if (game == null) { GameSession.End(_state); _adopted = null; return; }
        _playing = game;
        if (a.AutoReturn && Directory.Exists(a.Dir))
        {
            var w = new GameWatcher(a.Dir, a.FirstSeen > 0 ? DateTimeOffset.FromUnixTimeSeconds(a.FirstSeen).UtcDateTime : null, DateTimeOffset.FromUnixTimeSeconds(a.Started).UtcDateTime);
            w.Exited += () => Dispatcher.BeginInvoke(() => OnGameExited(game, w.PlayTime));
            _watcher = w;
            w.Start();
        }
        WriteStatus();
    }

    /// <summary>Ekran değişti (çözünürlük, monitör takıldı/çıkarıldı): Konsol görünürse yeni ekrana yeniden sığdır.</summary>
    void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_mode != Mode.Launcher || !IsVisible) return;
        WindowState = WindowState.Normal;
        WindowState = WindowState.Maximized;
    });

    /// <summary>Uykudan uyanınca (Konsol görünürse) pencere yine en üstte ve odakta olsun; Windows uyanışta bazen arkaya atar.</summary>
    void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(2500);
            if (_mode != Mode.Launcher || !IsVisible) return;
            Topmost = false; Topmost = true;
            Activate();
            Native.ForceForeground(new WindowInteropHelper(this).Handle);
        });
    }

    /// <summary>Konsol'u kapatıp yerine bekçiyi koy. Bekçi başlamazsa (yoksa/başarısızsa) false döner ve eski düşük güç moduna düşülür.</summary>
    bool TryHandoffToGuard()
    {
        var args = Environment.GetCommandLineArgs();
        if (!_state.UseGuard || !GuardLauncher.Available) return false;
        if (args.Contains("--noguard") || ((args.Contains("--debugcmd") || args.Contains("--bench") || args.Contains("--awaytest")) && !args.Contains("--forceguard"))) return false;

        GuardInfo? info = null;
        if (_playing != null)
        {
            var dir = _watcher != null ? _playing.InstallDir ?? "" : "";
            var opt = _state.GameOptions.GetValueOrDefault(_playing.Id);
            var autoReturn = opt?.AutoReturn switch { "on" => true, "off" => false, _ => _state.AutoReturn };
            info = new GuardInfo(_playing.Id, dir, _adopted?.Started ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(), autoReturn,
                _watcher?.FirstSeenUtc is { } f ? new DateTimeOffset(f).ToUnixTimeSeconds() : _adopted?.FirstSeen ?? 0);
        }
        if (!GuardLauncher.Start(info)) return false;
        _handoff = true;
        return true;
    }

    /// <summary>
    /// Arayüz otomasyon ağacı (UI Automation) kapalı: Konsol kol ile kullanılan bir arayüzdür; bir erişilebilirlik istemcisi
    /// (ekran okuyucu, kayıt/yayın araçları) dinlerken her yerleşim değişikliğinde yüzlerce öğe için nesne üretilip
    /// sekme geçişlerinde 25 ms'lik takılmalar oluyordu.
    /// </summary>
    protected override System.Windows.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;

    void ExitApp() { _exiting = true; Application.Current.Shutdown(); }

    /// <summary>Kullanıcı Konsol'dan çıkıyor: Konsol modundaysa önce Windows masaüstü açılır (yoksa kabuk gözetmeni Konsol'u yeniden başlatırdı). Oyun için bekçiye devirde çağrılmaz.</summary>
    void UserQuit() { try { if (ShellMode.IsEnabled()) ShellMode.EnsureExplorer(); } catch { } ExitApp(); }
    void IShell.Exit() => UserQuit();

    /// <summary>Alt+F4 / görev çubuğundan kapat: pencere yok olup uygulama gizli kalmasın; onayla çık.</summary>
    void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        if (_mode == Mode.Launcher) Confirm("KLYC Konsol kapatılsın mı?", UserQuit);
    }

    /// <summary>Kol takılı / çıkarılınca bilgi ver; kol yokken fare ve klavyeyle de kullanılabildiğini hatırlat.</summary>
    void OnPadConnectionChanged(bool connected)
    {
        if (connected) { _padSeen = true; return; }
        if (_padSeen && _mode == Mode.Launcher)
            Dispatcher.BeginInvoke(() => Toast("Kol bağlantısı koptu — klavye (ok tuşları, Enter, Esc) ve fare çalışır; Ctrl+Alt+K aç/gizle"));
    }
    (string Key, string Label)[] _pageHints = Array.Empty<(string, string)>();

    Mode _mode = Mode.Launcher;
    OverlayMode _overlay = OverlayMode.None;
    Action? _confirmAction;
    int _tab;
    bool _navFocus;
    bool _loading;
    int _backdropToken;
    Game? _playing;
    Game? _launching;                 // başlatma sürerken (süreç henüz yok): durum "launching"
    string _lastStatusSig = "";
    string _planOwner = "none";       // güç planını kim yönetiyor: konsol | pulse | none (status.json'a yazılır)
    GameWatcher? _watcher;
    Localizer? _localizer;   // yalnızca İngilizce arayüzde ve Konsol görünürken çalışır

    public MainWindow()
    {
        InitializeComponent();
        _localizer = new Localizer(() => this);

        _pages = new IPage[]
        {
            new LibraryPage(this), new BrowserPage(this), new SettingsPage(this),
        };
        foreach (var p in _pages)
        {
            var el = (FrameworkElement)p;
            el.Visibility = Visibility.Hidden;   // Collapsed değil: yerleşimi hazır kalsın, geçiş anında takılmasın
            PageHost.Children.Add(el);
        }

        Nav.TabClicked += SwitchTab;
        Nav.BatteryWarning += Toast;
        Nav.PulseProvider = () => PulseLink.ReadFeed();
        Nav.PulsePending = () => _cmdTracker.Pending;
        Nav.PulseFeedRead += OnPulseFeed;
        Nav.ShowPulse = _state.ShowPulseInfo;
        for (var i = 0; i < 4 && i < _state.Deadzones.Length; i++) _pad.Deadzone[i] = _state.Deadzones[i];
        InitGuardState();
        UiSound.Warm();
        if (_adopted == null) GameSession.End(_state);              // önceki oturum çöktüyse güç planı / bildirimleri geri yükle (oyun sürerken devralındıysa DEĞİL)
        _tray = new TrayIcon(() => OnPadKey(PadKey.Guide), UserQuit, () => _mode == Mode.Launcher);
        Application.Current.Exit += (_, _) => { if (!_handoff) GameSession.End(_state); _tray?.Dispose(); };
        // Ekran çözünürlüğü/ikinci monitör değişince ve uykudan uyanınca tam ekran düzeni toparlansın
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Application.Current.Exit += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        };
        Nav.PadInfoProvider = () => _pad.Describe();

        _pad.Pressed += OnPadKey;
        _pad.Tick += OnPadTick;
        _pad.ConnectionChanged += Nav.SetPad;
        _pad.VirtualOnly = Environment.GetCommandLineArgs().Contains("--virtualonly");
        if (!Environment.GetCommandLineArgs().Contains("--nopad")) _pad.Start();

        PreviewKeyDown += OnKeyboardFallback;
        Closing += OnWindowClosing;
        Application.Current.SessionEnding += (_, _) => _exiting = true;   // Windows kapanırken engelleme
        Nav.HideClicked += () => { if (_mode == Mode.Launcher && _overlay != OverlayMode.Busy) { HideOverlay(); HideLauncher(Mode.Away); } };
        Nav.ExitClicked += OpenPowerMenu;
        _pad.ConnectionChanged += OnPadConnectionChanged;
        SizeChanged += (_, _) => { };

        Loaded += async (_, _) =>
        {
            ShowCursor(false);
            _localizer?.Start();
            await ReloadAsync();
            StartLibraryWatch();
            ApplyGuardStart();
            WriteStatus();   // bekçiden açıldıysak eski "playing" durumu kalmasın
            await WarmUpAsync();
            _ = Task.Delay(3500).ContinueWith(_ => Dispatcher.Invoke(MaybePromptGuide));
            _ = AutoCheckUpdateAsync();
            ShowPage(0, animate: false);
            _ = PreloadAsync();
            _ = ResolveSupportAsync();

            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--nobackdrop")) Backdrop.Visibility = Visibility.Collapsed;
            if (args.Contains("--notext")) { }
            if (args.Contains("--perf") || args.Contains("--bench")) PerfMeter.Enable();
            if (args.Contains("--bench")) await RunBenchAsync();
            if (args.Contains("--debugcmd")) StartDebugCommands();
            if (args.Contains("--dumplibrary"))
            {
                var lines = _games.OrderBy(g => g.Source).ThenBy(g => g.Title).Select(g => $"{g.Id}	{g.Title}	{g.Source}	{g.Kind}	{g.Support}	{g.Target}	{g.Args}	{g.InstallDir}	play={g.PlaySeconds}	cover={(g.Cover != null)}	icon={g.IconCover}");
                File.WriteAllLines(Path.Combine(AppPaths.Dir, "library_dump.txt"), lines);
            }
            if (args.Contains("--awaytest")) { await Task.Delay(6000); HideLauncher(Mode.Away); }
            if (StartupPolicy.ShouldWaitForPad(args, _state)) _ = StartupPadWaitAsync();
        };
    }

    /// <summary>
    /// Her sayfayı bir kez (neredeyse görünmez) çizdirir: şablonlar, bitmap önbellekleri ve doku yüklemeleri
    /// ilk sekme geçişinde değil, açılışta yapılsın.
    /// </summary>
    async Task WarmUpAsync()
    {
        PageHost.Opacity = 0.01;
        for (var i = 0; i < _pages.Length; i++)
        {
            if (i == BrowserTab) continue;   // WebView2'yi uyandırma
            for (var j = 0; j < _pages.Length; j++)
                ((FrameworkElement)_pages[j]).Visibility = j == i ? Visibility.Visible : Visibility.Hidden;
            _pages[i].OnEnter();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await Task.Delay(60);
        }
        PageHost.Opacity = 1;
    }

    System.Windows.Threading.DispatcherTimer? _refreshTimer;

    /// <summary>Kol desteği bilgisi geldikçe kütüphane görünümünü (yığılmadan) tazeler.</summary>
    CancellationTokenSource? _bgCts;

    /// <summary>Kurulu oyunlar + (önbellekten) kurulu olmayan Steam oyunları + Epic ücretsiz teklifleri. Var olan nesneler korunur.</summary>
    void RebuildGames()
    {
        var owned = SteamOwnedService.BuildFromCache(_state, _installed, _usage);
        _owned = owned.Select(g => _owned.FirstOrDefault(x => x.Id == g.Id) is { } old ? Reuse(old, g) : g).ToList();
        var free = EpicFreeService.LoadCached();
        _free = free.Select(g => _free.FirstOrDefault(x => x.Id == g.Id) ?? g).ToList();
        _games = _installed.Concat(_owned).Concat(_free).ToList();
    }

    static Game Reuse(Game old, Game fresh) { old.Cover ??= fresh.Cover; old.Support = fresh.Support; old.PlaySeconds = fresh.PlaySeconds; old.LastPlayed = fresh.LastPlayed; return old; }

    /// <summary>Arka plan: Steam oynama verisi, kurulu olmayan oyunların adları/kol desteği, Epic ücretsiz oyunlar ve görselleri.</summary>
    async Task StartExtrasAsync()
    {
        if (Environment.GetCommandLineArgs().Contains("--noextras")) return;   // ölçüm / hata ayıklama
        _extrasCts?.Cancel();
        var cts = _extrasCts = new CancellationTokenSource();
        var ct = cts.Token;
        try
        {
            _usage = await Task.Run(SteamLocal.LoadUsage, ct);
            RebuildGames();
            ScheduleViewRefresh();
            _ = FetchArtAsync(_owned.ToList());   // önbellekteki "yüklü değil" oyunların kapakları

            void FreeChanged() { RebuildGames(); ScheduleViewRefresh(); _ = EpicFreeService.EnsureCoversAsync(_free, ct); }
            _ = EpicFreeService.RefreshAsync(FreeChanged, ct);
            _ = EpicFreeService.EnsureCoversAsync(_free, ct);

            await SteamOwnedService.ResolveAsync(_state, _installed, _usage, () => { RebuildGames(); ScheduleViewRefresh(); }, ct);
            if (!ct.IsCancellationRequested) await FetchArtAsync(_owned.ToList());
        }
        catch (OperationCanceledException) { }
    }

    void ScheduleViewRefresh()
    {
        _refreshTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _refreshTimer.Tick -= OnRefreshTick;
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    async Task ResolveSupportAsync()
    {
        _bgCts?.Cancel();
        var cts = _bgCts = new CancellationTokenSource();
        await ControllerSupportService.ResolveAsync(_installed, _state, _ =>
        {
            _refreshTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _refreshTimer.Tick -= OnRefreshTick;
            _refreshTimer.Tick += OnRefreshTick;
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }, cts.Token);
        // Ada göre Steam karşılığı bulunan oyunlar için eksik kapak/arka planları indir.
        if (!cts.IsCancellationRequested) await FetchArtAsync(_installed);
    }

    void OnRefreshTick(object? s, EventArgs e)
    {
        _refreshTimer?.Stop();
        RefreshLibraryViews();
    }

    public void RefreshLibraryViews()
    {
        foreach (var p in _pages) p.OnLibraryChanged();
    }

    /// <summary>Windows Game Bar orta tuşu da yakalar; ilk açılışta bir kez kapatmayı öner (kullanıcı onaylarsa).</summary>
    void MaybePromptGuide()
    {
        if (_state.GuidePromptShown || _mode != Mode.Launcher || _overlay != OverlayMode.None) return;
        _state.GuidePromptShown = true;
        _state.Save();
        if (!WindowsSettings.GameBarOpensOnGuide()) return;

        Confirm("Xbox (orta) tuşu Windows Game Bar'ını da açıyor. Konsol'u bu tuşla açıp kapatabilmek için Game Bar'ı kapatalım mı?", () =>
        {
            Toast(WindowsSettings.SetGameBarOpensOnGuide(false) ? "Xbox tuşu artık yalnızca Konsol'u açıp kapatır" : "Ayar değiştirilemedi");
        });
    }

    /// <summary>Hero ve küçük kapak görsellerini düşük öncelikli arka plan iş parçacığında önceden çözer.</summary>
    Task PreloadAsync()
    {
        var list = _installed.Where(g => g.Kind == EntryKind.Game).OrderByDescending(g => g.LastPlayed).Take(24).ToList();
        var t = new Thread(() =>
        {
            foreach (var g in list)
            {
                try
                {
                    if (!_tinyCache.ContainsKey(g.Id)) _tinyCache[g.Id] = g.Cover == null ? null : BackdropView.Tiny(g.Cover);
                }
                catch { }
                Thread.Sleep(30);
            }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        t.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test kapısı (yalnızca --debugcmd ile): %AppData%\KLYC-Konsol\cmd.txt içindeki boşlukla ayrılmış PadKey adlarını
    /// ("Right Down A Start ...") ya da "tab:N" komutlarını gerçek controller tuşu gibi işler.
    /// </summary>
    void StartDebugCommands()
    {
        var file = Path.Combine(AppPaths.Dir, "cmd.txt");
        var report = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        report.Tick += (_, _) =>
        {
            Native.GetCursorPos(out var cp);
            var line = $"{DateTime.Now:HH:mm:ss.fff} mode={_mode} navFocus={_navFocus} overlay={_overlay} tab={_tab} ticks={DesktopMode.Ticks} moves={DesktopMode.Moves} fail={DesktopMode.Failures} err={DesktopMode.LastError} cursor=({cp.X},{cp.Y}) set=({DesktopMode.LastX},{DesktopMode.LastY}) L=({_pad.State.LX},{_pad.State.LY})";
            try { File.AppendAllText(Path.Combine(AppPaths.Dir, "debug.log"), line + Environment.NewLine); } catch { }
        };
        report.Start();
        // UI iş parçacığından bağımsız canlılık kaydı (UI durursa bu devam eder)
        _ = new System.Threading.Timer(_ =>
        {
            try
            {
                var pr = Process.GetCurrentProcess();
                File.AppendAllText(Path.Combine(AppPaths.Dir, "debug_bg.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} mode={_mode} navFocus={_navFocus} overlay={_overlay} tab={_tab} powerMode={PowerMode.Active} prio={pr.PriorityClass} padTimerLow={_pad.IsLowPower} ticks={DesktopMode.Ticks}{Environment.NewLine}");
            }
            catch { }
        }, null, 0, 500);
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        t.Tick += async (_, _) =>
        {
            if (!File.Exists(file)) return;
            string text;
            try { text = File.ReadAllText(file); File.Delete(file); } catch { return; }
            foreach (var cmd in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (cmd.StartsWith("tab:") && int.TryParse(cmd[4..], out var n)) SwitchTab(n);
                else if (cmd.StartsWith("url:")) OpenInBrowser(cmd[4..]);
                else if (cmd.StartsWith("pad:")) { InjectDebugPad(cmd[4..]); }
                else if (cmd.StartsWith("stick:")) { InjectDebugStick(cmd[6..]); }
                else if (cmd.StartsWith("wait:") && int.TryParse(cmd[5..], out var ms)) await Task.Delay(ms);
                else if (Enum.TryParse<PadKey>(cmd, true, out var k)) OnPadKey(k);
                await Task.Delay(220);
            }
        };
        t.Start();
    }

    /// <summary>stick:lx,ly,rx,ry,ms — sanal çubuk değerleri (-32767..32767), ms süre boyunca tutulur.</summary>
    async void InjectDebugStick(string spec)
    {
        var p = spec.Split(',');
        if (p.Length < 5) return;
        short V(string x) => short.TryParse(x, out var v) ? v : (short)0;
        _pad.InjectSticks(V(p[0]), V(p[1]), V(p[2]), V(p[3]));
        await Task.Delay(int.TryParse(p[4], out var ms) ? ms : 500);
        _pad.InjectSticks(0, 0, 0, 0);
    }

    /// <summary>pad:LB,Guide gibi komutları gerçek yoklama hattından (birleşik kol) geçirir; 150 ms basılı tutar.</summary>
    async void InjectDebugPad(string names)
    {
        Btn b = 0;
        foreach (var n in names.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (Enum.TryParse<Btn>(n, true, out var one)) b |= one;
        _pad.InjectVirtual(b);
        await Task.Delay(150);
        _pad.InjectVirtual(0);
    }

    /// <summary>Otomatik kıyaslama: gezinti animasyonlarını sürer, kare sürelerini perf.log'a yazar, çıkar.</summary>
    async Task RunBenchAsync()
    {
        await Task.Delay(12000);   // görsellerin inmesini/yerleşmesini bekle

        async Task Press(PadKey k, int times, int gap = 300)
        {
            for (var i = 0; i < times; i++) { OnPadKey(k); await Task.Delay(gap); }
        }

        PerfMeter.Begin("Kütüphane: gezinti");
        await Press(PadKey.Right, 5); await Press(PadKey.Down, 2); await Press(PadKey.Right, 3); await Press(PadKey.Up, 2); await Press(PadKey.Left, 4);
        PerfMeter.Begin("Sekme geçişi");
        foreach (var t in new[] { 2, 0, 2, 0, 2, 0 }) { SwitchTab(t); await Task.Delay(500); }
        PerfMeter.Begin("Ayarlar: gezinti");
        SwitchTab(2);
        await Task.Delay(500);
        await Press(PadKey.Down, 8); await Press(PadKey.Up, 8);
        PerfMeter.End();
        ExitApp();
    }

    // ---- IShell ---------------------------------------------------------

    IReadOnlyList<Game> IShell.Games => _games;
    AppState IShell.State => _state;
    DesktopMode IShell.Mouse => _mouse;
    Grid IShell.WebLayer => WebLayer;
    XInputPad IShell.Pad => _pad;
    public void SetStickNavigation(bool on) => _pad.StickNavigation = on;

    public async Task ReloadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var user = UserLibrary.Load();
            _installed = await Task.Run(() => GameScanner.ScanAll(user, _state));
            RebuildGames();
            foreach (var p in _pages) p.OnLibraryChanged();
            if (IsLoaded) _pages[_tab].OnEnter();
            _ = FetchArtAsync(_installed);
            _ = StartExtrasAsync();
        }
        finally { _loading = false; }
    }

    // ---- Yeni kurulan oyunları kendiliğinden fark et ---------------------

    LibraryWatch.Watcher? _libWatch;
    string _libSig = "";
    DateTime _lastAutoReload = DateTime.MinValue;

    /// <summary>Konsol görünürken Steam/Epic/kısayol klasörlerini izler; gizliyken (oyun sırasında) hiçbir şey çalışmaz.</summary>
    void StartLibraryWatch()
    {
        StopLibraryWatch();
        if (Environment.GetCommandLineArgs().Contains("--noextras")) return;
        try
        {
            var roots = LibraryWatch.Roots(_state);
            _libSig = LibraryWatch.Signature(roots);
            _libWatch = new LibraryWatch.Watcher(roots, () => Dispatcher.BeginInvoke(() => _ = AutoReloadAsync("watcher")));
        }
        catch (Exception ex) { AppLog.Write("kütüphane", "izleyici açılamadı: " + ex.Message); }
    }

    void StopLibraryWatch() { _libWatch?.Dispose(); _libWatch = null; }

    async Task ResumeLibraryWatchAsync()
    {
        try { await CheckLibraryChangedAsync(); }
        catch (Exception ex) { AppLog.Write("kütüphane", "denetim hatası: " + ex.Message); }
        if (_mode == Mode.Launcher) StartLibraryWatch();
    }

    /// <summary>Konsol'a dönünce (oyun yüklenmiş / silinmiş olabilir) imza değiştiyse kütüphaneyi yeniler.</summary>
    async Task CheckLibraryChangedAsync()
    {
        var sig = await Task.Run(() => LibraryWatch.Signature(LibraryWatch.Roots(_state)));
        if (sig != _libSig) await AutoReloadAsync("resume");
    }

    async Task AutoReloadAsync(string why)
    {
        if (_mode != Mode.Launcher || _loading) return;
        if (DateTime.UtcNow - _lastAutoReload < TimeSpan.FromSeconds(30)) return;   // indirme sürerken manifest sık yazılır: sık yenileme
        _lastAutoReload = DateTime.UtcNow;
        var before = _installed.Select(g => g.Id).ToHashSet();
        await ReloadAsync();
        _libSig = await Task.Run(() => LibraryWatch.Signature(LibraryWatch.Roots(_state)));
        var added = _installed.Where(g => g.Kind == EntryKind.Game && !before.Contains(g.Id) && g.IsInstalled).Select(g => g.Title).ToList();
        AppLog.Write("kütüphane", $"otomatik yenileme ({why}): {_installed.Count} kayıt, yeni {added.Count}");
        if (added.Count == 1) Toast("Yeni oyun bulundu: " + added[0]);
        else if (added.Count > 1) Toast($"{added.Count} yeni oyun bulundu");
        if (added.Count > 0) _ = ResolveSupportAsync();
    }

    async Task FetchArtAsync(List<Game> games)
    {
        var any = false;
        await ArtService.FillMissingAsync(games, _ => any = true);
        if (any)
        {
            foreach (var p in _pages) p.OnLibraryChanged();
            _pages[_tab].OnEnter();
        }
    }

    public void SetHints(params (string Key, string Label)[] hints)
    {
        _pageHints = hints;
        if (!_navFocus) RenderHints(hints);
    }

    void RenderHints((string Key, string Label)[] hints)
    {
        Hints.Children.Clear();
        foreach (var (key, label) in hints)
        {
            var color = key switch
            {
                "A" => Color.FromRgb(0x2F, 0xA8, 0x4F),
                "B" => Color.FromRgb(0xD8, 0x45, 0x3B),
                "X" => Color.FromRgb(0x3F, 0x7B, 0xE0),
                "Y" => Color.FromRgb(0xD9, 0xA5, 0x21),
                _ => (Color?)null,
            } ?? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);

            var round = key.Length == 1 && "ABXY".Contains(key);
            var badge = new Border
            {
                Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(round ? 22 : 12),
                MinWidth = 44, Height = 44, Padding = new Thickness(round ? 0 : 14, 0, round ? 0 : 14, 0), Margin = new Thickness(0, 0, 14, 0),
                Child = new TextBlock { Text = key, FontSize = round ? 26 : 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Hints.Children.Add(badge);
            Hints.Children.Add(new TextBlock { Text = label, FontSize = 26, Foreground = (Brush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 44, 0) });
        }
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?> _tinyCache = new();

    public async void SetBackdrop(Game? game)
    {
        // Sade arka plan: seçili oyunun kapağından türetilmiş küçük, yumuşak renk alanı (ağır tam ekran görsel yok).
        var token = ++_backdropToken;
        if (game == null) { Backdrop.Show(null, false); return; }

        if (!_tinyCache.TryGetValue(game.Id, out var tiny))
        {
            var cover = game.Cover;
            tiny = cover == null ? null : await Task.Run(() => BackdropView.Tiny(cover));
            _tinyCache[game.Id] = tiny;
        }
        if (token == _backdropToken) Backdrop.Show(tiny, false);
    }

    public void ToggleFavorite(Game game)
    {
        game.IsFavorite = !game.IsFavorite;
        if (game.IsFavorite) { if (!_state.Favorites.Contains(game.Id)) _state.Favorites.Add(game.Id); }
        else _state.Favorites.Remove(game.Id);
        _state.Save();
        Toast(game.IsFavorite ? $"Favorilere eklendi: {game.Title}" : $"Favorilerden çıkarıldı: {game.Title}");
    }

    public void RestartLocalizer() { _localizer?.Stop(); _localizer?.Start(); }

    // ---- Pulse ile birlikte: üst çubuk bilgisi ve komutlar -----------------

    PulseFeed? _latestPulse;
    readonly PulseCommandTracker _cmdTracker = new();

    public PulseFeed? LatestPulse => _latestPulse;

    void OnPulseFeed(PulseFeed? feed)
    {
        _latestPulse = feed;
        if (_cmdTracker.Poll(feed, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) is { } r)
        {
            AppLog.Write("pulse", $"komut sonucu: {(r.Ok ? "tamam" : "olmadı")} — {r.Message}");
            Toast($"Pulse: {r.Message}");
        }
    }

    public void SendPulseCommand(string action, string? mode, string? value = null)
    {
        var feed = _latestPulse ?? PulseLink.ReadFeed();
        if (!PulseLink.IsAlive(feed))
        {
            // Pulse kapalıysa kolla açılabilsin: Pulse'un kendi başlangıç görevini çalıştır (UAC çıkmaz); komut açılınca A ile yeniden gönderilir
            var (started, msg) = PulseLink.StartPulse();
            AppLog.Write("pulse", $"Pulse çalışmıyor, başlatma denendi: {(started ? "tamam" : "olmadı")} — {msg}");
            Toast(started ? "Pulse başlatılıyor… birkaç saniye sonra tekrar dene" : msg);
            return;
        }
        if (feed is not { AcceptsCommands: true }) { Toast("Pulse'ta komuta izin kapalı: Pulse → Ayarlar → \"Konsol'dan mod değiştirmeye izin ver\""); return; }
        var id = PulseLink.SendCommand(action, mode, value: value);
        if (id == null) { Toast("Komut Pulse'a yazılamadı"); return; }
        _cmdTracker.Track(id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        AppLog.Write("pulse", $"komut gönderildi: {action} {mode} {value}");
        Toast("Pulse'a gönderildi…");
    }

    public void ApplyPulseInfoSetting()
    {
        Nav.ShowPulse = _state.ShowPulseInfo;
        WriteStatus();   // wantsLive güncellensin: kapalıysa Pulse sensör okumaz
    }

    public void Toast(string text)
    {
        ToastText.Text = Loc.Translate(text);
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(150)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(2000)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(2350)));
        ToastBox.BeginAnimation(OpacityProperty, a);
    }

    public void ShowCursor(bool visible) => Cursor = visible ? null : Cursors.None;

    public void SetChromeVisible(bool visible)
    {
        Chrome.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Backdrop.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Confirm(string question, Action onYes)
    {
        _confirmAction = onYes;
        OverlayCover.Visibility = Visibility.Collapsed;
        ShowOverlay(OverlayMode.Confirm, question, "A  Evet        B  Vazgeç");
    }

    // ---- Sekmeler / odak ------------------------------------------------

    void ShowPage(int index, bool animate = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        index = Math.Clamp(index, 0, _pages.Length - 1);
        if (IsLoaded && _tab != index) _pages[_tab].OnLeave();
        for (var i = 0; i < _pages.Length; i++)
            ((FrameworkElement)_pages[i]).Visibility = i == index ? Visibility.Visible : Visibility.Hidden;

        _tab = index;
        _pad.StickNavigation = true;
        Nav.SetActive(index);
        if (index != BrowserTab) { Keyboard.ClearFocus(); Focus(); }
        _mouse.BIsRightClick = index != BrowserTab;

        var page = (FrameworkElement)_pages[index];
        _pages[index].OnEnter();
        if (PerfMeter.Enabled) { var t1 = sw.Elapsed.TotalMilliseconds; Dispatcher.BeginInvoke(() => PerfMeter.Note($"ShowPage({index}): kod={t1:F1} ms, çizim dahil={sw.Elapsed.TotalMilliseconds:F1} ms"), System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
    }

    public void SwitchTab(int index)
    {
        index = Math.Clamp(index, 0, _pages.Length - 1);
        if (index == _tab) return;
        ShowPage(index);
    }

    public void FocusNav()
    {
        _navFocus = true;
        _mouse.Reset();
        Nav.SetFocused(true);
        RenderHints(new[] { ("◀ ▶", "Sekme"), ("A", "Gir"), ("▼", "Gir"), ("Y", "Güç menüsü / çıkış") });
    }

    void LeaveNav()
    {
        _navFocus = false;
        Nav.SetFocused(false);
        _mouse.Block();
        RenderHints(_pageHints);
    }

    // ---- Controller -----------------------------------------------------

    void OnPadTick(PadState s, double dt)
    {
        if (_mode == Mode.Desktop) _mouse.Update(s, dt);
        else if (_mode == Mode.Launcher && !_navFocus && _overlay == OverlayMode.None) _pages[_tab].OnTick(s, dt);
    }

    void OnPadKey(PadKey key)
    {
        var swk = System.Diagnostics.Stopwatch.StartNew();
        try { OnPadKeyCore(key); }
        finally { if (PerfMeter.Enabled) PerfMeter.Note($"tuş {key}: {swk.Elapsed.TotalMilliseconds:F1} ms (UI)"); }
    }

    /// <summary>Gezinme sesi / titreşimi (yalnızca Konsol açıkken; oyun sırasında bu yol hiç çalışmaz).</summary>
    void Feedback(PadKey key)
    {
        if (_state.UiSounds)
        {
            switch (key)
            {
                case PadKey.Up: case PadKey.Down: case PadKey.Left: case PadKey.Right: UiSound.Play(Cue.Move); break;
                case PadKey.A: UiSound.Play(Cue.Select); break;
                case PadKey.B: UiSound.Play(Cue.Back); break;
                case PadKey.LB: case PadKey.RB: UiSound.Play(Cue.Tab); break;
            }
        }
        if (_state.Haptics && key is PadKey.A or PadKey.B) _pad.Pulse();
    }

    void OnPadKeyCore(PadKey key)
    {
        var held = _pad.State.Buttons;
        var combo = (key == PadKey.Start && held.HasFlag(Btn.Back)) || (key == PadKey.Back && held.HasFlag(Btn.Start));
        if (key == PadKey.Guide || combo)
        {
            // Orta tuş = aç/kapat: Konsol açıksa arka plana iner, kapalıysa (oyun/masaüstü) öne gelir.
            if (_mode == Mode.Launcher)
            {
                if (_overlay != OverlayMode.Busy) { HideOverlay(); HideLauncher(Mode.Away); }
            }
            else ShowLauncher();
            return;
        }

        if (_overlay == OverlayMode.Busy) return;
        if (_mode == Mode.Launcher) Feedback(key);
        if (_powerMenu is { IsOpen: true }) { _powerMenu.HandleKey(key); return; }
        if (_overlay == OverlayMode.Summary)
        {
            if (key is PadKey.A or PadKey.B or PadKey.Start) { _summaryGame = null; HideOverlay(); }
            return;
        }
        if (_overlay == OverlayMode.Confirm)
        {
            if (key == PadKey.A) { var act = _confirmAction; HideOverlay(); act?.Invoke(); }
            else if (key == PadKey.B) HideOverlay();
            return;
        }

        switch (_mode)
        {
            case Mode.Desktop: _mouse.HandleKey(key, held); return;
            case Mode.Away: return;
        }

        if (_navFocus)
        {
            switch (key)
            {
                case PadKey.Y: OpenPowerMenu(); break;
                case PadKey.Left: case PadKey.LB: SwitchTab(_tab - 1); break;
                case PadKey.Right: case PadKey.RB: SwitchTab(_tab + 1); break;
                case PadKey.Down: case PadKey.A: LeaveNav(); break;
            }
            return;
        }

        if (_pages[_tab].OnKey(key)) return;

        switch (key)
        {
            case PadKey.LB: SwitchTab(_tab - 1); break;
            case PadKey.RB: SwitchTab(_tab + 1); break;
        }
    }

    /// <summary>Controller yokken klavyeyle de gezilebilsin (test için de).</summary>
    void OnKeyboardFallback(object sender, KeyEventArgs e)
    {
        PadKey? k = e.Key switch
        {
            Key.Up => PadKey.Up, Key.Down => PadKey.Down, Key.Left => PadKey.Left, Key.Right => PadKey.Right,
            Key.Enter => PadKey.A, Key.Escape => PadKey.B, Key.X => PadKey.X, Key.Y => PadKey.Y,
            Key.Q => PadKey.LB, Key.E => PadKey.RB, Key.Tab => PadKey.Start,
            _ => null,
        };
        if (k is { } key) { OnPadKey(key); e.Handled = true; }
    }

    // ---- Başlatma -------------------------------------------------------

    public async void Launch(Game game)
    {
        if (_overlay != OverlayMode.None) return;

        if (game.Availability == GameAvailability.NotInstalled)
        {
            try { Process.Start(new ProcessStartInfo(game.Target!) { UseShellExecute = true }); Toast($"{game.Title}: indirme Steam'de başlatıldı"); }
            catch { Toast("Steam açılamadı"); }
            return;
        }
        if (game.Availability == GameAvailability.FreeOffer)
        {
            if (game.StoreUrl == null) { Toast("Bu teklifin mağaza sayfası bulunamadı"); return; }
            OpenInBrowser(game.StoreUrl);
            return;
        }

        OverlayCover.Visibility = Visibility.Visible;
        OverlayCover.Background = game.Cover != null
            ? new ImageBrush(game.Cover) { Stretch = game.CoverStretch }
            : game.Accent;
        ShowOverlay(OverlayMode.Busy, game.Title, "Başlatılıyor…");

        var opt = _state.GameOptions.GetValueOrDefault(game.Id);
        var isGame = game.Kind == EntryKind.Game;
        var wantPlan = isGame && _state.PowerPlanOnGame && opt?.PowerPlan != "off";

        // Pulse (kardeş proje) çalışıyorsa güç planını yalnızca Pulse yönetir: Konsol dokunmaz (çakışma biter).
        // Konsol durumunu tazele ki Pulse özetini hemen yenilesin, sonra en çok 3 sn bekleyip "Pulse var mı" diye bak.
        _launching = game; _playing = null;
        _planOwner = "none";                 // karar verilene kadar kimse sahiplenmiş görünmesin (Pulse mod koruyucuyu boşuna bekletmesin)
        WriteStatus();
        var pulse = isGame && await PulseLink.PulseIsPresentAsync(TimeSpan.FromSeconds(3)) && PulseLink.PulseManagesPlan(PulseLink.ReadFeed());
        _planOwner = pulse ? "pulse" : wantPlan ? "konsol" : "none";
        var feedNow = PulseLink.ReadFeed();
        AppLog.Write("başlatma", $"{game.Id} \"{game.Title}\": Pulse özeti {(feedNow is null ? "yok" : PulseLink.IsAlive(feedNow) ? "taze" : "bayat")}" +
            (feedNow is null ? "" : $", Pulse otomatik oyun modu {(feedNow.AutoGameMode ? "açık" : "kapalı")}") + $" → güç planını yönetecek: {_planOwner}");
        WriteStatus();
        // powercfg / kayıt defteri çağrıları arayüz iş parçacığını bekletmesin (kapak ekranı takılmasın)
        var usePlan = wantPlan && !pulse; var mute = isGame && _state.MuteNotificationsOnGame;
        await Task.Run(() => GameSession.Begin(_state, usePowerPlan: usePlan, muteToasts: mute));

        try
        {
            var psi = new ProcessStartInfo(game.Target!) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(game.Args)) psi.Arguments = game.Args;
            if (File.Exists(game.Target)) psi.WorkingDirectory = Path.GetDirectoryName(game.Target)!;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            GameSession.End(_state);
            _launching = null; _planOwner = "none";
            WriteStatus();
            OverlayTitle.Text = "Başlatılamadı";
            OverlayHint.Text = ex.Message;
            await Task.Delay(2600);
            HideOverlay();
            return;
        }

        game.LastPlayed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _state.LastPlayed[game.Id] = game.LastPlayed;
        _state.Save();
        _playing = game;
        _launching = null;

        // Oyun kapanınca Konsol kendiliğinden öne gelsin (kurulum klasöründeki süreçler izlenir).
        _watcher?.Dispose();
        _watcher = null;
        var autoReturn = opt?.AutoReturn switch { "on" => true, "off" => false, _ => _state.AutoReturn };
        if (autoReturn && game.Kind == EntryKind.Game && game.InstallDir is { Length: > 0 } dir && Directory.Exists(dir))
        {
            var w = new GameWatcher(dir);
            w.Exited += () => Dispatcher.BeginInvoke(() => OnGameExited(game, w.PlayTime));
            _watcher = w;
            w.Start();
        }

        await Task.Delay(2000);
        HideOverlay();
        foreach (var p in _pages) p.OnLibraryChanged();
        HideLauncher(Mode.Away);
    }

    /// <summary>Verilen adresi Konsol'un tarayıcı sekmesinde açar.</summary>
    public void OpenInBrowser(string url)
    {
        SwitchTab(BrowserTab);
        if (_pages[BrowserTab] is BrowserPage b) b.NavigateTo(url);
    }

    async void OnGameExited(Game game, TimeSpan played)
    {
        RecordPlayTime(game, played);
        await Task.Run(() => GameSession.End(_state));
        _watcher?.Dispose();
        _watcher = null;
        if (_mode != Mode.Away) return;      // kullanıcı zaten Konsol'a dönmüş ya da masaüstü modunda
        ShowLauncher();
        if (played >= SummaryMinPlayed) ShowSessionSummary(game, played);
        else Toast($"{game.Title} kapandı");
        _ = PulseReportAsync(game, played);
    }

    // ---- Güç menüsü: klavye/fareye geçiş, Konsol'u kapat, uyku, yeniden başlat, kapat -------------------

    ListMenu? _powerMenu;

    /// <summary>
    /// Üst çubuktaki güç düğmesi (ya da sekme çubuğundayken Y): tek yerden çıkış seçenekleri. En üstteki "Windows masaüstüne geç" Konsol'u kapatmaz,
    /// yalnızca klavye ve fareyle çalışılacak şekilde gizler (Guide / Ctrl+Alt+K ile geri gelir).
    /// </summary>
    void OpenPowerMenu()
    {
        if (_mode != Mode.Launcher || _overlay != OverlayMode.None) return;
        if (_powerMenu is null)
        {
            _powerMenu = new ListMenu { Width = 980, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            Grid.SetRowSpan(_powerMenu, 3);
            ((Grid)Chrome.Child).Children.Add(_powerMenu);
            _powerMenu.Closed += () => { _powerMenu.Visibility = Visibility.Collapsed; _mouse.Block(); if (_navFocus) FocusNav(); else RenderHints(_pageHints); };
        }
        _powerMenu.Visibility = Visibility.Visible;
        _powerMenu.Open("Güç ve çıkış", () => new List<MenuRow>
        {
            new() { Text = "Windows masaüstüne geç (klavye ve fare)", Right = "Konsol açık kalır, Guide ile döner", OnA = () => { _powerMenu!.Close(); EnterDesktop(); } },
            new() { Text = "Konsol'u kapat", Right = "Windows masaüstü açılır", OnA = () => { _powerMenu!.Close(); UserQuit(); } },
            new() { Text = "Uyku", OnA = () => { _powerMenu!.Close(); Native.SetSuspendState(false, false, false); } },
            new() { Text = "Yeniden başlat", OnA = () => { _powerMenu!.Close(); Confirm("Bilgisayar yeniden başlatılsın mı?", () => Process.Start("shutdown", "/r /t 0")); } },
            new() { Text = "Bilgisayarı kapat", OnA = () => { _powerMenu!.Close(); Confirm("Bilgisayar kapatılsın mı?", () => Process.Start("shutdown", "/s /t 0")); } },
        }, select: 0);
        RenderHints(new[] { ("▲▼", "Seç"), ("A", "Onayla"), ("B", "Vazgeç") });
    }

    /// <summary>Bundan kısa oturumlar (başlatıcı açıp kapama, hemen çıkış) için özet paneli açılmaz; yalnızca kısa bildirim görünür.</summary>
    static readonly TimeSpan SummaryMinPlayed = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("KLYC_SUMMARY_MIN_SECONDS"), out var s) && s >= 0 ? s : 120);   // test için KLYC_SUMMARY_MIN_SECONDS
    Game? _summaryGame;

    /// <summary>
    /// Oyundan çıkınca "Oyun özeti" paneli: bu oturumun süresi, toplam süre ve (Pulse varsa) Pulse raporu. Rapor birkaç sn sonra hazır olur; panel o zaman güncellenir.
    /// A / B ile kapanır. Oyun BİTTİKTEN sonra açılır; oyun sırasındaki sıfır yük ilkesi bozulmaz.
    /// </summary>
    void ShowSessionSummary(Game game, TimeSpan played)
    {
        _summaryGame = game;
        var pulse = PulseLink.ReadFeed() is not null;
        ShowOverlay(OverlayMode.Summary, game.Title, "A  Tamam");
        OverlayCover.Visibility = Visibility.Collapsed;
        _summaryBase = $"{Loc.Translate("Bu oturum")}: {PlayTime.Format((long)played.TotalSeconds)}" + (game.PlaySeconds > 0 ? $"   ·   {Loc.Translate("Toplam")}: {PlayTime.Format(game.PlaySeconds)}" : "");
        OverlayBody.Text = _summaryBase + (pulse ? "\n\n" + Loc.Translate("Pulse raporu hazırlanıyor…") : "");
        OverlayBody.Foreground = (Brush)FindResource("Text");
        OverlayBody.Visibility = Visibility.Visible;
    }
    string _summaryBase = "";

    public void ShowInfo(string title, string body)
    {
        if (_overlay != OverlayMode.None) return;
        _summaryGame = null;
        ShowOverlay(OverlayMode.Summary, title, "A  Tamam");
        OverlayCover.Visibility = Visibility.Collapsed;
        OverlayBody.Text = Loc.Translate(body);
        OverlayBody.Foreground = (Brush)FindResource("Text");
        OverlayBody.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Windows açılışında: kol takılıysa arayüz açık kalır; birkaç saniye içinde kol görünmezse Konsol arka plana çekilir
    /// (bekçi devralır: Guide / Ctrl+Alt+K / simge ile açılır). Fare/klavyeyle açılan bilgisayarın üstünü örtmez.
    /// </summary>
    async Task StartupPadWaitAsync()
    {
        try
        {
            var until = DateTime.UtcNow + StartupPolicy.PadWait;
            while (DateTime.UtcNow < until)
            {
                if (_pad.ConnectedCount > 0) { AppLog.Write("açılış", "Windows ile açıldı, kol var: arayüz açık"); return; }
                await Task.Delay(500);
            }
            var hide = _mode == Mode.Launcher && _overlay == OverlayMode.None && _playing == null;
            AppLog.Write("açılış", hide ? "Windows ile açıldı, 8 sn içinde kol görünmedi: gizli bekleniyor (kolu takıp Guide'a / Ctrl+Alt+K'ya bas)" : "Windows ile açıldı, kol var: arayüz açık");
            if (hide) HideLauncher(Mode.Away);
        }
        catch { }
    }

    /// <summary>
    /// Pulse kuruluysa: oyun bitince Pulse raporunu hazırlar (birkaç sn sürer). Bu oyunun, bu oturuma ait raporu gelince tek satır bildirim gösterilir.
    /// Pulse yoksa ya da özet yoksa hiç beklenmez. Oyun BİTTİKTEN sonra çalışır; oyun sırasındaki sıfır yük ilkesi bozulmaz.
    /// </summary>
    async Task PulseReportAsync(Game game, TimeSpan played)
    {
        try
        {
            var sessionStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)played.TotalSeconds;   // Steam oyunlarında LastPlayed oyun BİTİNCE güncellenir; oturum başlangıcı süreden hesaplanır
            if (PulseLink.ReadFeed() is null) { ClearPendingSummary(game); return; }
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(2000);
                if (_mode != Mode.Launcher) return;          // kullanıcı başka yere geçti ya da yeni oyun başlattı
                var r = PulseLink.ReportFor(PulseLink.ReadFeed(), game.Title, game.InstallDir);
                if (r is not null && PulseLink.ReportMatchesSession(r, sessionStart, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                {
                    AppLog.Write("rapor", $"{game.Title}: Pulse raporu geldi ({r.Minutes:0} dk, önem {r.Severity})");
                    if (_overlay == OverlayMode.Summary && _summaryGame == game)
                    {
                        OverlayBody.Text = _summaryBase + "\n\n" + PulseLink.Summarize(r, 4);
                        OverlayBody.Foreground = r.Severity switch
                        {
                            >= 2 => new SolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x6A)),
                            1 => new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)),
                            _ => (Brush)FindResource("Text"),
                        };
                    }
                    else Toast("Pulse raporu: " + PulseLink.ShortSummary(r));
                    return;
                }
            }
            ClearPendingSummary(game);
        }
        catch { }
    }

    /// <summary>Rapor gelmediyse panelde "hazırlanıyor" yazısı kalmasın.</summary>
    void ClearPendingSummary(Game game)
    {
        if (_overlay == OverlayMode.Summary && _summaryGame == game) OverlayBody.Text = _summaryBase;
    }

    /// <summary>Steam dışı oyunların oynama süresini kaydeder (Steam oyunları için Steam'in kendi kaydı geçerlidir).</summary>
    void RecordPlayTime(Game game, TimeSpan played)
    {
        var total = SessionFinisher.RecordPlay(_state, game.Id, (long)played.TotalSeconds);   // başlatıcı açılıp kapanması (<30 sn) sayılmaz
        if (total > 0) game.PlaySeconds = total;
    }

    void ShowOverlay(OverlayMode kind, string title, string hint)
    {
        _overlay = kind;
        OverlayTitle.Text = Loc.Translate(title);
        OverlayHint.Text = Loc.Translate(hint);
        Overlay.Visibility = Visibility.Visible;
        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    void HideOverlay()
    {
        _overlay = OverlayMode.None;
        Overlay.Visibility = Visibility.Collapsed;
        OverlayBody.Visibility = Visibility.Collapsed;
        _mouse.Block();
    }

    // ---- Pencere / mod geçişleri ---------------------------------------

    public void EnterDesktop()
    {
        _mouse.Reset();
        _mouse.BIsRightClick = true;
        try { if (ShellMode.IsEnabled()) ShellMode.EnsureExplorer(); } catch { }   // Konsol modunda masaüstü (görev çubuğu) yoksa başlat
        _pad.StickNavigation = false;
        HideLauncher(Mode.Desktop);
        _desktopHint.Show();
        ShowCursor(true);
    }

    void HideLauncher(Mode next)
    {
        _mode = next;
        Topmost = false;
        Hide();
        WriteStatus();

        // Konsol görünmezken ağır hiçbir şey çalışmasın: tarayıcı, zamanlayıcılar, görsel önbellekleri.
        _localizer?.Stop();
        StopLibraryWatch();
        Nav.Suspend();
        _bgCts?.Cancel();
        _extrasCts?.Cancel();
        _refreshTimer?.Stop();
        foreach (var p in _pages) p.OnSuspend();
        _tinyCache.Clear();
        Backdrop.Clear();
        // Kurulu olmayan / ücretsiz teklif kapakları diskten geri yüklenebilir: oyun sırasında bellekte tutma.
        foreach (var g in _owned.Concat(_free)) g.Cover = null;

        if (next == Mode.Away && TryHandoffToGuard())
        {
            ExitApp();   // Konsol tamamen kapanır; bekçi (≈0 yük) Guide / Ctrl+Alt+K / simge ile geri açar
            return;
        }

        if (next == Mode.Away)
        {
            // Bekçi yoksa: arayüz iş parçacığı uyanmasın; yalnızca Guide/Start+Back'e bakılır.
            _pad.SetLowPower(true);
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);                      // oyunun açılışına karışma
                if (_mode == Mode.Away) PowerMode.Enter();   // IDLE öncelik + EcoQoS + bellek iadesi
            });
        }
    }

    void ShowLauncher()
    {
        PowerMode.Exit();
        _pad.SetLowPower(false);
        _mouse.Reset();
        _desktopHint.Hide();
        foreach (var p in Process.GetProcessesByName("osk")) { try { p.Kill(); } catch { } }

        _mode = Mode.Launcher;
        _playing = null;
        _launching = null;
        _planOwner = "none";
        if (_watcher == null) GameSession.End(_state);   // oyun izlenemiyorsa Konsol'a dönünce eski ayarlara dön
        WriteStatus();
        _mouse.BIsRightClick = _tab != BrowserTab;
        ShowCursor(_tab == BrowserTab);
        Show();
        WindowState = WindowState.Maximized;
        Topmost = true;
        Activate();
        Native.ForceForeground(new WindowInteropHelper(this).Handle);

        _pad.StickNavigation = true;
        Nav.Resume();
        _localizer?.Start();
        _ = ResumeLibraryWatchAsync();
        foreach (var p in _pages) p.OnResume();
        _pages[_tab].OnEnter();
        _ = ResolveSupportAsync();   // yarım kalan kol desteği sorgularına devam
        _ = StartExtrasAsync();
    }

    /// <summary>
    /// KLYC Pulse için durum dosyası (şema 2, atomik yazım): Konsol ne yapıyor, hangi oyun başlıyor/açık, güç planını kim yönetiyor.
    /// Oyun sırasında Konsol kapalıdır; bu dosya o zaman "son bilinen" durumdur.
    /// </summary>
    void WriteStatus()
    {
        try
        {
            var game = _launching ?? _playing;
            var state = _launching != null ? "launching"
                : _mode switch { Mode.Away => _playing != null ? "playing" : "background", Mode.Desktop => "desktop", _ => "launcher" };
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var json = PulseLink.BuildStatus(state, game, _planOwner, _state.UseGuard, _pad.ConnectedCount > 0, Environment.ProcessId, AppInfo.Version, now, wantsLive: _state.ShowPulseInfo && state == "launcher");
            PulseLink.WriteAtomic(PulseLink.StatusFile, json);
            var sig = $"{state}/{game?.Id}/{_planOwner}";
            if (sig != _lastStatusSig) { _lastStatusSig = sig; AppLog.Write("durum", $"{state}" + (game is null ? "" : $" ({game.Title})") + $", güç planı: {_planOwner}"); }
        }
        catch { }
    }
}

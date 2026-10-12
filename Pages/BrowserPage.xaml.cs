using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KlycKonsol.Input;
using KlycKonsol.Library;
using KlycKonsol.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace KlycKonsol.Pages;

/// <summary>
/// Uygulamanın içindeki gerçek tarayıcı (WebView2 / Edge motoru).
/// Sol çubuk: imleç · Sağ çubuk: kaydır · A: tıkla · B: geri · LB/RB: geri/ileri
/// X: sayfaya yaz · Y: adres/arama · Select(Back): yenile · Start: üst çubuğa geç
/// </summary>
public partial class BrowserPage : UserControl, IPage
{
    const string HomeUrl = "klyc://start";

    const int MaxTabs = 6;

    sealed class BrowserTab
    {
        public WebView2? Web;
        public Task? Init;
        public string Url = HomeUrl;
        public string Title = "";
        /// <summary>YouTube TV kipi: TV kimliği (User-Agent) açık; A = Enter, B = Esc, imleç yok.</summary>
        public bool Tv;
        public string? DefaultUa;
    }

    /// <summary>İndirme listesi satırı (en yeni üstte; yalnızca bu oturum).</summary>
    sealed class DownloadItem
    {
        public string Name = "";
        public string Path = "";
        public long Received;
        public long? Total;
        public string State = "indiriliyor";   // indiriliyor | bitti | hata | iptal
        public CoreWebView2DownloadOperation? Op;
    }

    readonly List<DownloadItem> _downloads = new();
    BrowserTab? CurrentTab => _cur >= 0 && _cur < _tabs.Count ? _tabs[_cur] : null;
    bool InTvMode => _active && CurrentTab is { Tv: true } && _web?.Visibility == Visibility.Visible && !Osk.IsOpen && !Menu.IsOpen && !_inToolbar;

    readonly IShell _shell;
    readonly List<BrowserTab> _tabs = new();
    int _cur;
    CoreWebView2Environment? _env;
    WebView2? _web => _cur >= 0 && _cur < _tabs.Count ? _tabs[_cur].Web : null;
    bool _initStarted, _ready, _active, _fullscreen;
    string? _pendingUrl;   // dışarıdan istenen adres: hazır olunca yeni sekmede açılır
    bool _urlMode;
    bool _inToolbar;
    int _tb;
    Border[] _toolbar = Array.Empty<Border>();

    public BrowserPage(IShell shell)
    {
        _shell = shell;
        InitializeComponent();

        BtnBack.MouseLeftButtonUp += (_, _) => GoBack();
        BtnFwd.MouseLeftButtonUp += (_, _) => { if (_web?.CanGoForward == true) _web.GoForward(); };
        BtnReload.MouseLeftButtonUp += (_, _) => _web?.Reload();
        BtnHome.MouseLeftButtonUp += (_, _) => NavigateHome();
        UrlPill.MouseLeftButtonUp += (_, _) => OpenUrlKeyboard();
        BtnKeys.MouseLeftButtonUp += (_, _) => OpenPageKeyboard();
        BtnMenu.MouseLeftButtonUp += (_, _) => OpenMenu();

        _toolbar = new[] { BtnBack, BtnFwd, BtnReload, BtnHome, UrlPill, BtnMenu, BtnKeys };
        Menu.Closed += () => { SetWebVisible(true); UpdateStickMode(); _shell.Mouse.Block(); UpdateHints(); FocusWeb(); };
        Slot.SizeChanged += (_, _) => PositionWeb();
        Osk.Typed += OnTyped;
        Osk.Backspace += () => { if (!_urlMode) Native.Tap(Native.VK_BACK); };
        Osk.Submit += OnSubmit;
        Osk.Closed += () => { UpdateStickMode(); _shell.Mouse.Block(); UpdateHints(); FocusWeb(); };
    }

    public void OnLibraryChanged() { }

    /// <summary>İçerikte sol çubuk = imleç (yön tuşu olayı yok); araç çubuğu / klavye açıkken menü gezintisi.</summary>
    void UpdateStickMode() => _shell.SetStickNavigation(InTvMode || !(_active && !_inToolbar && !Osk.IsOpen && !Menu.IsOpen));
    int _mouseBusy;

    /// <summary>
    /// Oyun modu: tarayıcıyı tamamen kapat (WebView2 ayrı bir süreç ağacıdır, yüzlerce MB tutar).
    /// Sekmelerin adresleri saklanır; Konsol geri gelince sekmeler (görünen yüklenerek) geri açılır.
    /// </summary>
    public void OnSuspend()
    {
        if (Menu.IsOpen) Menu.Close();
        SaveSession();
        foreach (var t in _tabs)
        {
            if (t.Web == null) continue;
            try { _shell.WebLayer.Children.Remove(t.Web); t.Web.Dispose(); } catch { }
            t.Web = null; t.Init = null;
        }
        _env = null; _ready = false; _initStarted = false;
    }

    public void OnResume() { }

    // ---- Yaşam döngüsü --------------------------------------------------

    public void OnEnter()
    {
        _active = true;
        UpdateStickMode();
        UpdateHints();
        _shell.SetBackdrop(null);
        _shell.ShowCursor(true);
        _shell.Mouse.BIsRightClick = false;
        if (!_initStarted) { _initStarted = true; _ = InitAsync(); }
        SetWebVisible(true);
        Dispatcher.BeginInvoke(() => { PositionWeb(); CenterCursor(); FocusWeb(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void OnLeave()
    {
        _active = false;
        _inToolbar = false;
        _shell.SetStickNavigation(true);
        RefreshToolbar();
        if (Osk.IsOpen) Osk.Close();
        if (Menu.IsOpen) Menu.Close();
        SetWebVisible(false);
        _shell.ShowCursor(false);
        _shell.Mouse.Reset();
        _shell.Mouse.BIsRightClick = true;
    }

    void UpdateHints()
    {
        if (Osk.IsOpen) _shell.SetHints(("A", "Yaz"), ("X", "Sil"), ("Y", "Büyük harf"), ("Start", "Tamam"), ("B", "Kapat"));
        else if (Menu.IsOpen) _shell.SetHints(("▲▼", "Seç"), ("A", "Aç"), ("X", "Kapat / sil"), ("◀ ▶", "Ayarla"), ("B", "Geri"));
        else if (_inToolbar) _shell.SetHints(("◀ ▶", "Gez"), ("A", "Seç"), ("▼", "Sayfaya dön"), ("▲", "Sekmeler"), ("LB RB", "Sekme"));
        else if (InTvMode) _shell.SetHints(("◀▲▼▶", "Gez"), ("A", "Seç"), ("B", "Geri"), ("Y", "Adres / ara"), ("Start", "Araç çubuğu"), ("LB RB", "Sekme"));
        else _shell.SetHints(("A", "Tıkla"), ("B", "Geri"), ("X", "Sayfaya yaz"), ("Y", "Adres / ara"), ("Start", "Araç çubuğu"), ("LB RB", "Sekme"));
    }

    // ---- WebView2 -------------------------------------------------------

    async Task InitAsync()
    {
        try
        {
            _env ??= await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.Dir, "webview"));
            if (_tabs.Count == 0)
            {
                foreach (var u in _shell.State.BrowserTabs.Take(MaxTabs)) _tabs.Add(new BrowserTab { Url = u });
                _cur = Math.Clamp(_shell.State.BrowserCurrent, 0, Math.Max(0, _tabs.Count - 1));
            }
            if (_tabs.Count == 0) _tabs.Add(new BrowserTab());
            if (_pendingUrl != null)
            {
                var u = _pendingUrl;
                _pendingUrl = null;
                if (_tabs.Count < MaxTabs) { _tabs.Add(new BrowserTab { Url = u }); _cur = _tabs.Count - 1; }
                else _tabs[_cur].Url = u;
            }

            _ready = true;
            Status.Visibility = Visibility.Collapsed;
            await SelectAsync(_cur);
        }
        catch (Exception ex)
        {
            Status.Text = "Tarayıcı başlatılamadı (WebView2 Runtime gerekli):\n" + ex.Message;
        }
    }

    /// <summary>Sekmenin WebView2'sini (henüz yoksa) oluşturur; yalnızca görüntülenen sekmeler süreç tutar.</summary>
    Task EnsureWebAsync(BrowserTab tab) => tab.Init ??= CreateWebAsync(tab);

    async Task CreateWebAsync(BrowserTab tab)
    {
        var web = new WebView2 { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, DefaultBackgroundColor = System.Drawing.Color.FromArgb(11, 13, 17), Visibility = Visibility.Collapsed };
        _shell.WebLayer.Children.Add(web);
        tab.Web = web;
        await web.EnsureCoreWebView2Async(_env);

        var core = web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = true;
        web.ZoomFactor = _shell.State.BrowserZoom;

        core.NewWindowRequested += (_, e) => { e.Handled = true; if (_tabs.Count < MaxTabs) AddTab(e.Uri, select: true); else core.Navigate(e.Uri); };
        core.SourceChanged += (_, _) => OnTabChanged(tab);
        core.DocumentTitleChanged += (_, _) => OnTabChanged(tab);
        core.NavigationCompleted += (_, _) => { OnTabChanged(tab); SaveSession(); };
        core.ContainsFullScreenElementChanged += (_, _) => { if (tab == _tabs.ElementAtOrDefault(_cur)) OnFullscreenChanged(core.ContainsFullScreenElement); };
        core.WebResourceRequested += OnStartPageRequest;
        core.AddWebResourceRequestedFilter("https://klyc.start/*", CoreWebView2WebResourceContext.All);
        core.DownloadStarting += OnDownloadStarting;

        // YouTube TV: televizyon arayüzü yalnızca TV/konsol kimliğiyle gelir. Kimlik yalnızca bu sekmede ve yalnızca YouTube/Google adreslerinde kalır.
        tab.DefaultUa = core.Settings.UserAgent;
        if (BrowserModes.IsYouTubeTv(tab.Url)) { tab.Tv = true; core.Settings.UserAgent = BrowserModes.TvUserAgent; }
        core.NavigationStarting += (_, e) => OnNavigationStarting(tab, core, e);

        core.Navigate(Resolve(tab.Url));
    }

    void OnNavigationStarting(BrowserTab tab, CoreWebView2 core, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = e.Uri;
        if (BrowserModes.IsYouTubeTv(uri))
        {
            if (!tab.Tv || core.Settings.UserAgent != BrowserModes.TvUserAgent)
            {
                tab.Tv = true;
                core.Settings.UserAgent = BrowserModes.TvUserAgent;
                e.Cancel = true;   // kimlik değişince sayfa yeniden istenmeli
                Dispatcher.BeginInvoke(() => { try { core.Navigate(uri); } catch { } });
            }
            Dispatcher.BeginInvoke(() => { if (tab == CurrentTab) { UpdateStickMode(); UpdateHints(); } });
        }
        else if (tab.Tv && !BrowserModes.StaysInTv(uri))
        {
            tab.Tv = false;
            core.Settings.UserAgent = tab.DefaultUa ?? core.Settings.UserAgent;   // başka siteye geçince TV kimliğini bırak
            Dispatcher.BeginInvoke(() => { if (tab == CurrentTab) { UpdateStickMode(); UpdateHints(); } });
        }
    }

    // ---- İndirmeler ------------------------------------------------------

    /// <summary>İndirmeyi varsayılan pencere olmadan İndirilenler klasörüne alır (çakışmasız ad); ilerleme menüden izlenir.</summary>
    void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var op = e.DownloadOperation;
            var name = DownloadHelper.Sanitize(System.IO.Path.GetFileName(e.ResultFilePath));
            var path = DownloadHelper.UniquePath(DownloadHelper.DownloadsDir(), name);
            e.ResultFilePath = path;
            e.Handled = true;   // Edge'in kendi indirme penceresi açılmasın (kol ile kullanılamaz)

            var item = new DownloadItem { Name = System.IO.Path.GetFileName(path), Path = path, Op = op, Total = op.TotalBytesToReceive is { } tb0 ? (long)tb0 : null };
            _downloads.Insert(0, item);
            if (_downloads.Count > 20) _downloads.RemoveRange(20, _downloads.Count - 20);
            op.BytesReceivedChanged += (_, _) => { item.Received = op.BytesReceived; item.Total = op.TotalBytesToReceive is { } tb1 ? (long)tb1 : null; if (Menu.IsOpen) Menu.Refresh(); };
            op.StateChanged += (_, _) =>
            {
                item.State = op.State switch
                {
                    CoreWebView2DownloadState.Completed => "bitti",
                    CoreWebView2DownloadState.Interrupted => op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled ? "iptal" : "hata",
                    _ => "indiriliyor",
                };
                if (item.State == "bitti") { item.Received = op.BytesReceived; _shell.Toast($"İndirildi: {item.Name}"); }
                else if (item.State == "hata") _shell.Toast($"İndirme başarısız: {item.Name}");
                if (Menu.IsOpen) Menu.Refresh();
            };
            _shell.Toast($"İndiriliyor: {item.Name} (Tarayıcı menüsünden izle)");
            AppLog.Write("tarayıcı", "indirme başladı: " + item.Name);
        }
        catch (Exception ex) { AppLog.Write("tarayıcı", "indirme hatası: " + ex.Message); }
    }

    static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + (System.IO.Path.GetDirectoryName(path) ?? DownloadHelper.DownloadsDir()) + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    static string Resolve(string url) => url == HomeUrl ? "https://klyc.start/" : url;

    void OnStartPageRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 core) return;
        var html = StartPage.Html(_shell.State.Bookmarks);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
        e.Response = core.Environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: text/html; charset=utf-8");
    }

    void OnTabChanged(BrowserTab tab)
    {
        if (tab.Web?.CoreWebView2 is not { } core) return;
        var src = tab.Web.Source?.ToString() ?? "";
        tab.Url = src.StartsWith("https://klyc.start") ? HomeUrl : src.Length > 0 ? src : tab.Url;
        tab.Title = core.DocumentTitle ?? "";
        if (tab == _tabs.ElementAtOrDefault(_cur)) UpdateUrl();
        UpdateTabCount();
    }

    void SaveSession()
    {
        try
        {
            var st = _shell.State;
            st.BrowserTabs = _tabs.Select(t => t.Url).ToList();
            st.BrowserCurrent = _cur;
            st.Save();
        }
        catch { }
    }

    void UpdateTabCount() => TabCount.Text = _tabs.Count > 1 ? _tabs.Count.ToString() : "";

    void SetWebVisible(bool visible)
    {
        var show = visible && _active && !Menu.IsOpen;
        for (var i = 0; i < _tabs.Count; i++)
            if (_tabs[i].Web != null) _tabs[i].Web!.Visibility = show && i == _cur ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task SelectAsync(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _cur = index;
        var tab = _tabs[index];
        SetWebVisible(false);
        try { await EnsureWebAsync(tab); }
        catch (Exception ex) { Status.Visibility = Visibility.Visible; Status.Text = "Sekme açılamadı:\n" + ex.Message; return; }
        if (_cur != index) return;   // bu arada başka sekmeye geçildi
        SetWebVisible(true);
        PositionWeb();
        UpdateUrl();
        UpdateTabCount();
        FocusWeb();
        SaveSession();
    }

    void AddTab(string url, bool select)
    {
        if (_tabs.Count >= MaxTabs) { _shell.Toast($"En fazla {MaxTabs} sekme açılabilir"); return; }
        _tabs.Add(new BrowserTab { Url = url });
        UpdateTabCount();
        if (select) _ = SelectAsync(_tabs.Count - 1);
    }

    void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        var tab = _tabs[index];
        if (tab.Web != null) { try { _shell.WebLayer.Children.Remove(tab.Web); tab.Web.Dispose(); } catch { } }
        _tabs.RemoveAt(index);
        if (_tabs.Count == 0) _tabs.Add(new BrowserTab());
        if (_cur > index || _cur >= _tabs.Count) _cur = Math.Max(0, _cur - 1);
        _ = SelectAsync(_cur);
    }

    void Navigate(string url)
    {
        if (!_ready || _tabs.Count == 0) { _pendingUrl = url; return; }
        var tab = _tabs[_cur];
        tab.Url = url;
        if (tab.Web?.CoreWebView2 is { } core) core.Navigate(Resolve(url));
        else _ = SelectAsync(_cur);
    }

    void NavigateHome() => Navigate(HomeUrl);

    /// <summary>Dışarıdan (ör. ücretsiz oyun kartı, yer imi) bir adres aç: yeni sekmede.</summary>
    public void NavigateTo(string url)
    {
        if (!_ready) { _pendingUrl = url; return; }
        if (_tabs.Count < MaxTabs) AddTab(url, select: true); else Navigate(url);
    }

    void GoBack()
    {
        if (_web?.CanGoBack == true) _web.GoBack();
        else _shell.FocusNav();
    }

    void UpdateUrl()
    {
        if (_web?.CoreWebView2 == null) return;
        var src = _web.Source?.ToString() ?? "";
        if (src.StartsWith("https://klyc.start")) { UrlText.Text = "Ara ya da adres yaz"; UrlText.Opacity = 0.6; }
        else
        {
            var title = _web.CoreWebView2.DocumentTitle;
            UrlText.Text = string.IsNullOrWhiteSpace(title) ? src : $"{title}";
            UrlText.Opacity = 1;
        }
        BtnBack.Opacity = _web.CanGoBack ? 1 : 0.4;
        BtnFwd.Opacity = _web.CanGoForward ? 1 : 0.4;
    }

    void OnFullscreenChanged(bool full)
    {
        _fullscreen = full;
        _shell.SetChromeVisible(!full);
        PositionWeb();
    }

    /// <summary>WebView2 bir HWND olduğu için Viewbox içinde ölçeklenemez; yer tutucunun ekran konumuna oturtulur.</summary>
    void PositionWeb()
    {
        var layer = _shell.WebLayer;
        foreach (var t in _tabs)
        {
            if (t.Web == null) continue;
            if (_fullscreen)
            {
                t.Web.Margin = new Thickness(0);
                t.Web.Width = layer.ActualWidth;
                t.Web.Height = layer.ActualHeight;
                continue;
            }
            if (!Slot.IsLoaded || Slot.ActualWidth < 10) return;
            try
            {
                var r = Slot.TransformToVisual(layer).TransformBounds(new Rect(0, 0, Slot.ActualWidth, Slot.ActualHeight));
                t.Web.Margin = new Thickness(r.Left, r.Top, 0, 0);
                t.Web.Width = r.Width;
                t.Web.Height = r.Height;
            }
            catch { }
        }
    }

    void FocusWeb()
    {
        if (_active && _web?.CoreWebView2 != null && !Osk.IsOpen && !Menu.IsOpen) _web.Focus();
    }

    void CenterCursor()
    {
        try
        {
            var p = Slot.PointToScreen(new Point(Slot.ActualWidth / 2, Slot.ActualHeight / 2));
            Native.SetCursorPos((int)p.X, (int)p.Y);
        }
        catch { }
    }

    // ---- Klavye ---------------------------------------------------------

    void OpenUrlKeyboard()
    {
        _urlMode = true;
        Osk.Open("Adres yaz ya da ara", "", showPreview: true);
        UpdateStickMode();
        PositionWebSoon();
        UpdateHints();
    }

    void OpenPageKeyboard()
    {
        _urlMode = false;
        Osk.Open("Sayfaya yaz (önce yazı kutusuna tıkla)", "", showPreview: false);
        UpdateStickMode();
        PositionWebSoon();
        UpdateHints();
    }

    void PositionWebSoon() => Dispatcher.BeginInvoke(PositionWeb, System.Windows.Threading.DispatcherPriority.Loaded);

    void OnTyped(string s)
    {
        if (_urlMode) return;
        foreach (var c in s) Native.TypeChar(c);
    }

    void OnSubmit()
    {
        if (_urlMode)
        {
            var text = Osk.Text.Trim();
            Osk.Close();
            if (text.Length > 0) Navigate(ToUrl(text));
        }
        else
        {
            Native.Tap(Native.VK_RETURN);
            Osk.Close();
        }
        PositionWebSoon();
    }

    static string ToUrl(string text)
    {
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return text;
        if (!text.Contains(' ') && text.Contains('.'))
            return "https://" + text;
        return "https://www.google.com/search?q=" + WebUtility.UrlEncode(text);
    }

    // ---- Controller -----------------------------------------------------

    public void OnTick(PadState s, double dt)
    {
        if (_active && !Osk.IsOpen && !Menu.IsOpen && !_inToolbar && _web?.Visibility == Visibility.Visible && CurrentTab is not { Tv: true })
        {
            if (Environment.GetCommandLineArgs().Contains("--mousethread"))
            {
                if (Interlocked.Exchange(ref _mouseBusy, 1) == 0)
                    _ = Task.Run(() => { try { _shell.Mouse.Update(s, dt); } finally { Interlocked.Exchange(ref _mouseBusy, 0); } });
            }
            else _shell.Mouse.Update(s, dt);
        }
    }

    public bool OnKey(PadKey key)
    {
        if (Menu.IsOpen) { Menu.HandleKey(key); UpdateHints(); return true; }
        if (Osk.IsOpen)
        {
            Osk.HandleKey(key);
            if (!Osk.IsOpen) PositionWebSoon();
            UpdateHints();
            return true;
        }

        if (_inToolbar) return OnToolbarKey(key);

        // YouTube TV: televizyon arayüzü D-pad + Enter + Esc ile kullanılır (imleç/tıklama yok)
        if (InTvMode)
        {
            switch (key)
            {
                case PadKey.A: Native.Tap(Native.VK_RETURN); return true;
                case PadKey.B: Native.Tap(Native.VK_ESCAPE); return true;
            }
        }

        switch (key)
        {
            case PadKey.B:
                if (_fullscreen) _ = _web?.ExecuteScriptAsync("document.exitFullscreen && document.exitFullscreen()");
                else GoBack();
                return true;
            case PadKey.Back: _web?.Reload(); return true;
            case PadKey.Start: EnterToolbar(); return true;
            case PadKey.Y: OpenUrlKeyboard(); return true;
            case PadKey.X: OpenPageKeyboard(); return true;
            case PadKey.Up: Native.Tap(Native.VK_UP); return true;
            case PadKey.Down: Native.Tap(Native.VK_DOWN); return true;
            case PadKey.Left: Native.Tap(Native.VK_LEFT); return true;
            case PadKey.Right: Native.Tap(Native.VK_RIGHT); return true;
        }
        return key == PadKey.A; // A tıklama olarak DesktopMode'da işleniyor
    }

    // ---- Araç çubuğu odağı (D-pad ile tüm düğmelere erişim) ---------------

    void EnterToolbar()
    {
        _inToolbar = true;
        UpdateStickMode();
        _tb = Math.Min(_tb, _toolbar.Length - 1);
        _shell.Mouse.Reset();
        RefreshToolbar();
        UpdateHints();
    }

    void LeaveToolbar()
    {
        _inToolbar = false;
        UpdateStickMode();
        _shell.Mouse.Block();
        RefreshToolbar();
        UpdateHints();
        FocusWeb();
    }

    void RefreshToolbar()
    {
        for (var i = 0; i < _toolbar.Length; i++)
        {
            var on = _inToolbar && i == _tb;
            _toolbar[i].BorderBrush = on ? Brushes.White : (Brush)FindResource("CardStroke");
            _toolbar[i].BorderThickness = new Thickness(on ? 3 : 1.5);
        }
    }

    bool OnToolbarKey(PadKey key)
    {
        switch (key)
        {
            case PadKey.Left: _tb = Math.Max(0, _tb - 1); RefreshToolbar(); return true;
            case PadKey.Right: _tb = Math.Min(_toolbar.Length - 1, _tb + 1); RefreshToolbar(); return true;
            case PadKey.Up: _inToolbar = false; RefreshToolbar(); _shell.FocusNav(); return true;
            case PadKey.Down: case PadKey.B: case PadKey.Start: LeaveToolbar(); return true;
            case PadKey.A:
                switch (_tb)
                {
                    case 0: if (_web?.CanGoBack == true) _web.GoBack(); break;
                    case 1: if (_web?.CanGoForward == true) _web.GoForward(); break;
                    case 2: _web?.Reload(); break;
                    case 3: NavigateHome(); LeaveToolbar(); break;
                    case 4: LeaveToolbar(); OpenUrlKeyboard(); break;
                    case 5: LeaveToolbar(); OpenMenu(); break;
                    case 6: LeaveToolbar(); OpenPageKeyboard(); break;
                }
                return true;
        }
        return false;
    }

    // ---- Menü: sekmeler · yer imleri · yakınlaştırma ----------------------

    static readonly double[] Zooms = { 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 };

    void OpenMenu()
    {
        if (Osk.IsOpen) Osk.Close();
        SetWebVisible(false);
        _inToolbar = false;
        RefreshToolbar();
        _shell.Mouse.Reset();
        Menu.Open("Tarayıcı", BuildMenu, select: 1);
        UpdateStickMode();
        UpdateHints();
    }

    List<MenuRow> BuildMenu()
    {
        var rows = new List<MenuRow> { new() { Text = $"SEKMELER ({_tabs.Count}/{MaxTabs})", Header = true } };
        for (var i = 0; i < _tabs.Count; i++)
        {
            var idx = i;
            var t = _tabs[i];
            var label = t.Url == HomeUrl ? "Başlangıç sayfası" : string.IsNullOrWhiteSpace(t.Title) ? t.Url : t.Title;
            rows.Add(new MenuRow
            {
                Text = (i == _cur ? "●  " : "○  ") + label,
                Right = t.Url == HomeUrl ? null : HostOf(t.Url),
                OnA = () => { Menu.Close(); _ = SelectAsync(idx); },
                OnX = () => CloseTab(idx),
            });
        }
        rows.Add(new MenuRow { Text = "＋  Yeni sekme", Accent = true, OnA = () => { Menu.Close(); AddTab(HomeUrl, select: true); } });
        rows.Add(new MenuRow { Text = "▶  YouTube TV (kolla izle)", Accent = true, Right = "D-pad · A: seç · B: geri", OnA = () => { Menu.Close(); NavigateTo(BrowserModes.YouTubeTvUrl); } });

        if (_downloads.Count > 0)
        {
            rows.Add(new MenuRow { Text = "İNDİRMELER", Header = true });
            foreach (var d in _downloads.ToList())
            {
                var item = d;
                var right = item.State switch
                {
                    "bitti" => $"✓ {DownloadHelper.FormatSize(item.Received)} · A: klasörde göster",
                    "hata" => "başarısız · X: kaldır",
                    "iptal" => "iptal edildi · X: kaldır",
                    _ => $"{DownloadHelper.Progress(item.Received, item.Total)} · X: iptal",
                };
                rows.Add(new MenuRow
                {
                    Text = item.Name, Right = right,
                    OnA = () => { if (item.State == "bitti") { Menu.Close(); _shell.EnterDesktop(); RevealInExplorer(item.Path); } },
                    OnX = () => { if (item.State == "indiriliyor") { try { item.Op?.Cancel(); } catch { } } else _downloads.Remove(item); },
                });
            }
        }

        rows.Add(new MenuRow { Text = "YER İMLERİ", Header = true });
        var cur = _tabs.ElementAtOrDefault(_cur);
        if (cur != null && cur.Url != HomeUrl)
        {
            var marked = _shell.State.Bookmarks.Any(b => b.Url == cur.Url);
            rows.Add(new MenuRow
            {
                Text = marked ? "★  Bu sayfa yer imlerinde — kaldır" : "☆  Bu sayfayı yer imlerine ekle", Accent = true,
                OnA = () =>
                {
                    var st = _shell.State;
                    if (marked) st.Bookmarks.RemoveAll(b => b.Url == cur.Url);
                    else st.Bookmarks.Add(new Bookmark(string.IsNullOrWhiteSpace(cur.Title) ? HostOf(cur.Url) : cur.Title, cur.Url));
                    st.Save();
                },
            });
        }
        foreach (var bm in _shell.State.Bookmarks.ToList())
            rows.Add(new MenuRow
            {
                Text = bm.Title, Right = HostOf(bm.Url),
                OnA = () => { Menu.Close(); NavigateTo(bm.Url); },
                OnX = () => { _shell.State.Bookmarks.Remove(bm); _shell.State.Save(); },
            });
        if (_shell.State.Bookmarks.Count == 0) rows.Add(new MenuRow { Text = "Henüz yer imi yok", Header = true });

        rows.Add(new MenuRow { Text = "AYARLAR", Header = true });
        rows.Add(new MenuRow
        {
            Text = "Yakınlaştırma", Right = $"%{(int)Math.Round(_shell.State.BrowserZoom * 100)}",
            OnLeftRight = d => SetZoom(d),
            OnA = () => SetZoom(1),
        });
        rows.Add(new MenuRow { Text = "Tarama verilerini temizle (geçmiş, çerez, önbellek)", OnA = ClearData });
        return rows;
    }

    static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : url;

    void SetZoom(int dir)
    {
        var cur = Array.FindIndex(Zooms, z => Math.Abs(z - _shell.State.BrowserZoom) < 0.01);
        if (cur < 0) cur = 2;
        var next = Zooms[(cur + dir + Zooms.Length) % Zooms.Length];
        _shell.State.BrowserZoom = next;
        _shell.State.Save();
        foreach (var t in _tabs) if (t.Web != null) { try { t.Web.ZoomFactor = next; } catch { } }
    }

    async void ClearData()
    {
        try
        {
            var core = _web?.CoreWebView2;
            if (core == null) { _shell.Toast("Önce bir sekme açık olmalı"); return; }
            await core.Profile.ClearBrowsingDataAsync();
            _shell.Toast("Tarama verileri temizlendi");
        }
        catch { _shell.Toast("Temizlenemedi"); }
    }
}

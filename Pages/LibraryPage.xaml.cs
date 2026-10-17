using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KlycKonsol.Input;
using KlycKonsol.Library;
using KlycKonsol.Models;
using KlycKonsol.Ui;

namespace KlycKonsol.Pages;

/// <summary>
/// Kütüphane: açılış sayfası. Odak iki bölgede gezer — üst çubuk (filtre çipleri + arama düğmesi) ve kapak ızgarası.
/// Her şey D-pad ile erişilebilir: ◀ ▶ ▲ ▼ gezer, A seçer, Y arar, X favori, Back = kol filtresi aç/kapat.
/// </summary>
public partial class LibraryPage : UserControl, IPage
{
    const int Cols = 7;
    const double RowHeight = 380;
    static readonly string[] ChipNames = { "Oyunlar", "Son oynananlar", "Favoriler", "Uygulamalar", "Yüklü değil", "Ücretsiz", "Koleksiyonlar" };
    const int ChipGames = 0, ChipRecent = 1, ChipFav = 2, ChipApps = 3, ChipNotInstalled = 4, ChipFree = 5, ChipCollections = 6;
    static int SearchSlot => ChipNames.Length;   // çiplerden sonraki odak noktası (arama)

    readonly IShell _shell;
    readonly ObservableCollection<Game> _items = new();
    readonly List<(Border box, TextBlock text)> _chips = new();
    int _chip, _index, _barFocus;
    bool _inBar;
    string _search = "";
    int _hiddenByFilter;
    int _colIndex;                 // Koleksiyonlar çipinde gösterilen koleksiyon
    Game? _namingFor;              // ekran klavyesi yeni koleksiyon adı için açıksa, eklenecek oyun
    bool _menuOpen;
    readonly GameDetailPanel _detail;
    int _menuIndex;
    readonly List<(string label, Action act)> _menu = new();

    public LibraryPage(IShell shell)
    {
        _shell = shell;
        InitializeComponent();
        CardList.ItemsSource = _items;

        for (var i = 0; i < ChipNames.Length; i++)
        {
            var idx = i;
            var tb = new TextBlock { Text = ChipNames[i], FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border { Padding = new Thickness(20, 12, 20, 12), CornerRadius = new CornerRadius(32), Child = tb, Cursor = System.Windows.Input.Cursors.Hand };
            box.MouseLeftButtonUp += (_, _) => { _chip = idx; Rebuild(); };
            Chips.Children.Add(box);
            _chips.Add((box, tb));
        }
        SearchPill.MouseLeftButtonUp += (_, _) => OpenSearch();

        _detail = new GameDetailPanel(_shell, () => { Rebuild(); });
        _detail.Closed += () => { DetailLayer.Visibility = Visibility.Collapsed; _shell.Mouse.Block(); UpdateHints(); Select(false); };
        DetailLayer.Children.Add(_detail);

        Osk.Typed += _ => { if (_namingFor == null) { _search = Osk.Text; Rebuild(); } };
        Osk.Backspace += () => { if (_namingFor == null) { _search = Osk.Text; Rebuild(); } };
        Osk.Submit += () => { FinishNaming(); Osk.Close(); };
        Osk.Closed += () => { _namingFor = null; _shell.Mouse.Block(); UpdateHints(); Select(); };

        // Fare desteği (kol yokken): tek tık seç, çift tık başlat, tekerlek satır kaydırır.
        CardList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_detail.IsOpen || Osk.IsOpen || _menuOpen) return;
            var el = e.OriginalSource as DependencyObject;
            while (el != null && !(el is FrameworkElement { DataContext: Game })) el = el is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(el) : LogicalTreeHelper.GetParent(el);
            if (el is not FrameworkElement { DataContext: Game g }) return;
            var i = _items.IndexOf(g);
            if (i < 0) return;
            _inBar = false; _index = i; UpdateHints(); Select();
            if (e.ClickCount >= 2) _shell.Launch(g);
        };
        PreviewMouseWheel += (_, e) =>
        {
            if (_detail.IsOpen || Osk.IsOpen || _menuOpen || _items.Count == 0) return;
            if (_inBar) { _inBar = false; UpdateHints(); }
            Move(e.Delta > 0 ? -Cols : Cols);
            e.Handled = true;
        };
    }

    Game? Current => _index >= 0 && _index < _items.Count ? _items[_index] : null;

    /// <summary>Koleksiyonlar çipinde gösterilen koleksiyon (hiç yoksa null; silinmişse sona kırpılır).</summary>
    GameCollection? CurrentCollection
    {
        get
        {
            var all = _shell.State.Collections;
            if (all.Count == 0) return null;
            _colIndex = Math.Clamp(_colIndex, 0, all.Count - 1);
            return all[_colIndex];
        }
    }

    // ---- Süzme ----------------------------------------------------------

    bool PassesPad(Game g)
    {
        if (g.Kind != EntryKind.Game || !_shell.State.ControllerOnly) return true;
        return g.Support == PadSupport.Full || (_shell.State.IncludePartial && g.Support == PadSupport.Partial);
    }

    bool _dirty;

    /// <summary>Başka sekmedeyken (arka planda gelen kol desteği / kapak güncellemeleri) yeniden kurma: sekmeye dönünce bir kez kurulur.</summary>
    public void OnLibraryChanged()
    {
        if (!IsVisible) { _dirty = true; return; }
        _dirty = false;
        Rebuild();
    }

    void Rebuild()
    {
        var keep = Current?.Id;
        var hidden = _shell.State.Hidden;
        var all = _shell.Games.Where(g => !hidden.Contains(g.Id)).ToList();

        IEnumerable<Game> q = _chip switch
        {
            ChipGames => all.Where(g => g.Kind == EntryKind.Game && g.IsInstalled && PassesPad(g)),
            ChipRecent => all.Where(g => g.Kind == EntryKind.Game && g.IsInstalled && g.LastPlayed > 0 && PassesPad(g)),
            ChipFav => all.Where(g => g.IsFavorite && g.IsInstalled && PassesPad(g)),
            ChipApps => all.Where(g => g.Kind == EntryKind.App),
            ChipNotInstalled => all.Where(g => g.Availability == GameAvailability.NotInstalled && PassesPad(g)),
            ChipCollections => Collections.Members(CurrentCollection, all, g => g.Id).Where(g => g.IsInstalled && PassesPad(g)),
            _ => all.Where(g => g.Availability == GameAvailability.FreeOffer),
        };
        if (_search.Length > 0)
            q = q.Where(g => g.Title.Contains(_search, StringComparison.CurrentCultureIgnoreCase));

        // Liste değişmediyse koleksiyona dokunma: kartların yeniden kurulması (şablon + yerleşim) 100 ms'yi bulabilir.
        var ordered = LibrarySorter.Order(q, _chip == ChipRecent ? "recent" : _shell.State.LibrarySort).ToList();
        if (_chip == ChipRecent) ordered = ordered.Take(30).ToList();
        if (!ordered.SequenceEqual(_items))
        {
            _items.Clear();
            foreach (var g in ordered) _items.Add(g);
        }

        // Liste kendi yüksekliğinden taşan satırları WPF yerleşim kırpması yüzünden çizmez: yüksekliği tam ver.
        CardList.Height = Math.Ceiling(_items.Count / (double)Cols) * RowHeight + 40;

        _hiddenByFilter = all.Count(g => g.Kind == EntryKind.Game && g.Availability != GameAvailability.FreeOffer && !PassesPad(g) && g.Support != PadSupport.Unknown);
        var pending = all.Count(g => g.Kind == EntryKind.Game && g.Support == PadSupport.Unknown && (g.Id.StartsWith("steam:") || g.Id.StartsWith("epic:")));

        _index = Math.Max(0, _items.ToList().FindIndex(g => g.Id == keep));
        SearchText.Text = _search.Length == 0 ? "Ara" : _search;
        SearchText.Foreground = (Brush)FindResource(_search.Length == 0 ? "TextDim" : "Text");

        var parts = new List<string> { $"{_items.Count} {(_chip == ChipApps ? "uygulama" : _chip == ChipFree ? "teklif" : "oyun")}" };
        if (_chip == ChipCollections && CurrentCollection is { } shown) parts.Insert(0, shown.Name);
        if (_chip != ChipApps && _chip != ChipFree && _shell.State.ControllerOnly)
        {
            if (_hiddenByFilter > 0) parts.Add($"{_hiddenByFilter} oyun gizli (kol desteği yok) · Back: tümünü göster");
            if (pending > 0) parts.Add($"kol desteği kontrol ediliyor ({pending})…");
        }
        StatusText.Text = Loc.Translate(string.Join("  ·  ", parts));

        ShowEmpty(pending);
        RefreshBar();
        Select(animate: false);
    }

    void ShowEmpty(int pending)
    {
        if (_items.Count > 0) { EmptyBox.Visibility = Visibility.Collapsed; return; }
        EmptyBox.Visibility = Visibility.Visible;
        if (_search.Length > 0) { EmptyTitle.Text = "Eşleşen sonuç yok"; EmptyHint.Text = "Aramayı değiştirmek için Y'ye bas."; }
        else if (_chip == ChipRecent) { EmptyTitle.Text = "Henüz oynanan oyun yok"; EmptyHint.Text = "Oynadığın oyunlar son oynanana göre burada sıralanır."; }
        else if (_chip == ChipFav) { EmptyTitle.Text = "Henüz favori yok"; EmptyHint.Text = "Bir oyunun üzerindeyken X'e basarak favoriye ekle."; }
        else if (_chip == ChipCollections && CurrentCollection is null) { EmptyTitle.Text = "Henüz koleksiyon yok"; EmptyHint.Text = "Bir oyunun üzerindeyken Start'a bas → Koleksiyona ekle / çıkar."; }
        else if (_chip == ChipCollections) { EmptyTitle.Text = "Bu koleksiyonda gösterilecek oyun yok"; EmptyHint.Text = "Oyunlar kurulu değil ya da kol desteği filtresi (Back) gizliyor olabilir."; }
        else if (_chip == ChipApps) { EmptyTitle.Text = "Uygulama yok"; EmptyHint.Text = "Ayarlar → Oyun / uygulama ekle."; }
        else if (_chip == ChipNotInstalled) { EmptyTitle.Text = "Yüklü olmayan oyun yok"; EmptyHint.Text = "Steam'de oynadığın ama şu an kurulu olmayan, kol destekli oyunlar burada çıkar (ilk açılışta mağazadan öğrenilir, internet gerekir)."; }
        else if (_chip == ChipFree) { EmptyTitle.Text = "Şu an ücretsiz oyun yok"; EmptyHint.Text = "Epic'in haftalık ücretsiz oyunları burada listelenir (internet gerekir)."; }
        else if (pending > 0) { EmptyTitle.Text = "Kol desteği kontrol ediliyor…"; EmptyHint.Text = "Oyunların controller desteği Steam mağazasından öğreniliyor (internet gerekir). Birkaç saniye içinde listelenir."; }
        else { EmptyTitle.Text = "Kol destekli oyun bulunamadı"; EmptyHint.Text = $"{_hiddenByFilter} oyun kol desteği olmadığı için gizlendi. Back tuşuyla filtreyi kapatabilirsin."; }
    }

    // ---- Görsel durum ---------------------------------------------------

    void RefreshBar()
    {
        for (var i = 0; i < _chips.Count; i++)
        {
            var on = i == _chip;
            var focused = _inBar && _barFocus == i;
            _chips[i].box.Background = on ? (Brush)FindResource("Lit") : Brushes.Transparent;
            _chips[i].box.BorderBrush = focused ? Brushes.White : Brushes.Transparent;
            _chips[i].box.BorderThickness = new Thickness(3);
            _chips[i].text.Foreground = on ? (Brush)FindResource("Ink") : (Brush)FindResource("Text");
            _chips[i].text.Opacity = on ? 1 : 0.8;
        }
        // Koleksiyon çipi, seçiliyken gösterilen koleksiyonun adını taşır (A ile sonrakine geçilir)
        _chips[ChipCollections].text.Text = Loc.Translate(_chip == ChipCollections && CurrentCollection is { } cc ? cc.Name : ChipNames[ChipCollections]);
        var searchFocused = _inBar && _barFocus == SearchSlot;
        SearchPill.BorderBrush = searchFocused ? Brushes.White : (Brush)FindResource("CardStroke");
        SearchPill.BorderThickness = new Thickness(searchFocused ? 3 : 1.5);
        SearchPill.Background = searchFocused ? (Brush)FindResource("CardHover") : new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
    }

    void Select(bool animate = true)
    {
        for (var i = 0; i < _items.Count; i++) _items[i].IsSelected = !_inBar && i == _index;
        RefreshBar();
        if (_items.Count == 0) { _shell.SetBackdrop(null); return; }

        var row = _index / Cols;
        ScrollAnim.Slide((TranslateTransform)CardList.RenderTransform, TranslateTransform.YProperty, -Math.Max(0, row) * RowHeight, animate);
        _shell.SetBackdrop(_inBar ? null : Current);
    }

    void UpdateHints()
    {
        if (_detail.IsOpen) _shell.SetHints(("▲▼", "Satır"), ("◀ ▶", "Seç / değiştir"), ("A", "Onayla"), ("X", "Favori"), ("B", "Kapat"));
        else if (Osk.IsOpen) _shell.SetHints(("A", "Yaz"), ("X", "Sil"), ("Y", "Büyük harf"), ("Start", "Tamam"), ("B", "Kapat"));
        else if (_menuOpen) _shell.SetHints(("▲ ▼", "Gez"), ("A", "Seç"), ("B", "Kapat"));
        else if (_inBar && _chip == ChipCollections && _shell.State.Collections.Count > 1) _shell.SetHints(("◀ ▶", "Gez"), ("A", "Sonraki koleksiyon"), ("▼", "Oyunlara in"), ("▲ / B", "Sekmeler"), ("LB RB", "Sekme"));
        else if (_inBar) _shell.SetHints(("◀ ▶", "Gez"), ("A", "Seç"), ("▼", "Oyunlara in"), ("▲ / B", "Sekmeler"), ("LB RB", "Sekme"));
        else _shell.SetHints(("A", "Başlat"), ("X", "Favori"), ("Y", "Ara"), ("Start", "Menü"), ("B", "Sekmeler"), ("LB RB", "Sekme"));
    }

    public void OnEnter() { if (_dirty) { _dirty = false; Rebuild(); } UpdateHints(); Select(false); }
    public void OnLeave() { if (Osk.IsOpen) Osk.Close(); if (_detail.IsOpen) _detail.Close(); }

    void OpenSearch()
    {
        Osk.Open("Kütüphanede ara", _search, showPreview: true);
        UpdateHints();
    }

    // ---- Tuşlar ---------------------------------------------------------

    public bool OnKey(PadKey key)
    {
        if (Osk.IsOpen) { Osk.HandleKey(key); UpdateHints(); return true; }
        if (_detail.IsOpen) { _detail.HandleKey(key); return true; }
        if (_menuOpen) { MenuKey(key); return true; }

        if (key == PadKey.Y) { OpenSearch(); return true; }
        if (key == PadKey.Back)
        {
            _shell.State.ControllerOnly = !_shell.State.ControllerOnly;
            _shell.State.Save();
            _shell.Toast(_shell.State.ControllerOnly ? "Sadece kol destekli oyunlar gösteriliyor" : "Tüm oyunlar gösteriliyor");
            Rebuild();
            return true;
        }

        return _inBar ? OnBarKey(key) : OnGridKey(key);
    }

    bool OnBarKey(PadKey key)
    {
        switch (key)
        {
            case PadKey.Left:
                _barFocus = Math.Max(0, _barFocus - 1);
                if (_barFocus < ChipNames.Length) { _chip = _barFocus; Rebuild(); } else RefreshBar();
                return true;
            case PadKey.Right:
                _barFocus = Math.Min(SearchSlot, _barFocus + 1);
                if (_barFocus < ChipNames.Length) { _chip = _barFocus; Rebuild(); } else RefreshBar();
                return true;
            case PadKey.Up:
            case PadKey.B: _inBar = false; RefreshBar(); _shell.FocusNav(); return true;
            case PadKey.Down:
                if (_items.Count > 0) { _inBar = false; UpdateHints(); Select(); }
                return true;
            case PadKey.A:
                if (_barFocus == SearchSlot) OpenSearch();
                else if (_barFocus == ChipCollections && _chip == ChipCollections && _shell.State.Collections.Count > 1)
                {
                    _colIndex = (_colIndex + 1) % _shell.State.Collections.Count;   // A: sonraki koleksiyon
                    Rebuild();
                }
                else if (_items.Count > 0) { _inBar = false; UpdateHints(); Select(); }
                return true;
        }
        return false;
    }

    bool OnGridKey(PadKey key)
    {
        switch (key)
        {
            case PadKey.Left: Move(-1); return true;
            case PadKey.Right: Move(1); return true;
            case PadKey.Down: Move(Cols); return true;
            case PadKey.Up:
                if (_index < Cols || _items.Count == 0) EnterBar();
                else Move(-Cols);
                return true;
            case PadKey.A: if (Current is { } g) _shell.Launch(g); return true;
            case PadKey.X: if (Current is { } f) { _shell.ToggleFavorite(f); Rebuild(); } return true;
            case PadKey.B: _inBar = false; RefreshBar(); _shell.FocusNav(); return true;
            case PadKey.Start: OpenMenu(); return true;
        }
        return false;
    }

    void EnterBar()
    {
        _inBar = true;
        _barFocus = _chip;
        UpdateHints();
        Select();
    }

    void Move(int delta)
    {
        if (_items.Count == 0) return;
        var i = _index + delta;
        if (delta == Cols && i >= _items.Count) i = _items.Count - 1;   // son satırda aşağı: son öğeye in
        if (i < 0 || i >= _items.Count || i == _index) return;
        if (delta == -1 && _index % Cols == 0) return;
        if (delta == 1 && _index % Cols == Cols - 1) return;
        _index = i;
        Select();
    }

    // ---- Bağlam menüsü --------------------------------------------------

    void OpenMenu()
    {
        if (Current is not { } g) return;
        _menu.Clear();
        _menu.Add((g.Availability switch { GameAvailability.NotInstalled => "Yükle (Steam)", GameAvailability.FreeOffer => "Mağazada aç", _ => "Başlat" }, () => { CloseMenu(); _shell.Launch(g); }));
        _menu.Add(("Ayrıntılar", () => { CloseMenu(); OpenDetail(g); }));
        _menu.Add((g.IsFavorite ? "Favorilerden çıkar" : "Favorilere ekle", () => { _shell.ToggleFavorite(g); CloseMenu(); Rebuild(); }));
        if (g.Kind == EntryKind.Game && g.Availability != GameAvailability.FreeOffer)
        {
            var has = g.Support is PadSupport.Full or PadSupport.Partial;
            _menu.Add((has ? "Kol desteği YOK olarak işaretle" : "Kol destekli olarak işaretle", () => SetSupport(g, has ? PadSupport.None : PadSupport.Full)));
        }
        if (g.Kind == EntryKind.Game && g.IsInstalled) _menu.Add(("Koleksiyona ekle / çıkar", () => OpenCollectionMenu(g)));
        _menu.Add(("Kütüphaneden gizle", () => { _shell.State.Hidden.Add(g.Id); _shell.State.Save(); CloseMenu(); _shell.Toast($"Gizlendi: {g.Title} (Ayarlar'dan geri getirilir)"); Rebuild(); }));

        _menuOpen = true;
        _menuIndex = 0;
        MenuTitle.Text = g.Title;
        MenuLayer.Visibility = Visibility.Visible;
        RenderMenu();
        UpdateHints();
    }

    /// <summary>Bağlam menüsünün alt listesi: her koleksiyon için ekle/çıkar (✓ = içinde) ve "Yeni koleksiyon" (ad ekran klavyesiyle).</summary>
    void OpenCollectionMenu(Game g)
    {
        _menu.Clear();
        foreach (var c in _shell.State.Collections.ToList())
        {
            var name = c.Name;
            _menu.Add(((Collections.Contains(_shell.State.Collections, name, g.Id) ? "✓ " : "+ ") + name, () => ToggleCollection(g, name)));
        }
        if (_shell.State.Collections.Count < Collections.MaxCollections)
            _menu.Add(("Yeni koleksiyon…", () => { CloseMenu(); _namingFor = g; Osk.Open("Koleksiyon adı", "", showPreview: true); UpdateHints(); }));
        _menu.Add(("Geri", () => OpenMenu()));
        _menuIndex = 0;
        MenuTitle.Text = g.Title;
        RenderMenu();
    }

    /// <summary>Ekran klavyesinde Tamam: yazılan ada göre yeni koleksiyonu oluşturup oyunu içine koyar.</summary>
    void FinishNaming()
    {
        if (_namingFor is not { } g) return;
        _namingFor = null;
        var name = Collections.CleanName(Osk.Text);
        if (name is null) { _shell.Toast("Koleksiyon adı boş olamaz"); return; }
        if (Collections.Contains(_shell.State.Collections, name, g.Id)) { _shell.Toast($"{g.Title} zaten {name} koleksiyonunda"); return; }
        var r = Collections.Toggle(_shell.State.Collections, name, g.Id);
        if (r is null) { _shell.Toast("Koleksiyon eklenemedi (en çok 8 koleksiyon)"); return; }
        _shell.State.Save();
        _colIndex = _shell.State.Collections.FindIndex(c => c.Name == r.Value.Name);
        _shell.Toast($"{g.Title} → {r.Value.Name}");
        Rebuild();
    }

    void ToggleCollection(Game g, string name)
    {
        var r = Collections.Toggle(_shell.State.Collections, name, g.Id);
        CloseMenu();
        if (r is null) { _shell.Toast("Koleksiyon eklenemedi (en çok 8 koleksiyon, ad boş olamaz)"); return; }
        _shell.State.Save();
        _shell.Toast(r.Value.Added ? $"{g.Title} → {r.Value.Name}" : $"{g.Title} ← {r.Value.Name} koleksiyonundan çıkarıldı");
        Rebuild();
    }

    void OpenDetail(Game g)
    {
        DetailLayer.Visibility = Visibility.Visible;
        _detail.Open(g);
        UpdateHints();
    }

    void SetSupport(Game g, PadSupport s)
    {
        _shell.State.SupportOverride[g.Id] = s == PadSupport.None ? "none" : "full";
        _shell.State.Save();
        g.Support = s;
        CloseMenu();
        _shell.Toast(s == PadSupport.None ? $"{g.Title}: kol desteği yok olarak işaretlendi" : $"{g.Title}: kol destekli olarak işaretlendi");
        Rebuild();
    }

    void RenderMenu()
    {
        MenuItems.Children.Clear();
        for (var i = 0; i < _menu.Count; i++)
        {
            var on = i == _menuIndex;
            MenuItems.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 16, 22, 16), Margin = new Thickness(0, 3, 0, 3),
                Background = on ? (Brush)FindResource("Lit") : Brushes.Transparent,
                Child = new TextBlock { Text = _menu[i].label, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = on ? (Brush)FindResource("Ink") : (Brush)FindResource("Text") },
            });
        }
    }

    void CloseMenu()
    {
        _menuOpen = false;
        MenuLayer.Visibility = Visibility.Collapsed;
        _shell.Mouse.Block();
        UpdateHints();
    }

    void MenuKey(PadKey key)
    {
        switch (key)
        {
            case PadKey.Up: _menuIndex = Math.Max(0, _menuIndex - 1); RenderMenu(); break;
            case PadKey.Down: _menuIndex = Math.Min(_menu.Count - 1, _menuIndex + 1); RenderMenu(); break;
            case PadKey.A: _menu[_menuIndex].act(); break;
            case PadKey.B: case PadKey.Start: CloseMenu(); break;
        }
    }
}

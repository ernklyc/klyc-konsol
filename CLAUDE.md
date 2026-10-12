# KLYC Konsol — yapay zekâ / geliştirici çalışma kılavuzu

Bu dosyayı (ve `docs/HEDEF.md`, `docs/DURUM.md`, `docs/YOL-HARITASI.md`, `docs/KARARLAR.md`) işe başlamadan **oku**. Yeni bir sohbette süreklilik buradan sağlanır.
Kullanıcıya adıyla ve Türkçe, samimi ama net hitap edilir (kullanıcı çoğu zaman kısa, yazım hatalı, sesli yazar gibi yazar — niyeti anla, sorgulama).

## Proje özeti

**KLYC Konsol**: Windows için controller ile (fare/klavye olmadan) kullanılan PS5 / Big Picture tarzı tam ekran arayüz. C# · .NET 8 · WPF · XInput · WebView2.
Oyun kütüphanesi (Steam, Epic, Xbox, EA, Riot, GOG, Ubisoft, Battle.net), uygulama içi tarayıcı, Wi‑Fi / Bluetooth / ses / parlaklık, oyun sırasında **sıfıra yakın yük**
(Konsol kapanır, ~1,5 MB'lık `KlycGuard.exe` bekçisi kalır). Sahibi: kullanıcı (GitHub: `ernklyc`). Kardeş proje: **KLYC-Pulse** (performans/ısı/bakım; ayrı repo, ayrı sohbet).

- Depo: <https://github.com/ernklyc/klyc-konsol> (public, MIT). Varsayılan dal `main`. Sürümler GitHub Releases'te.
- Güncel sürüm: bkz. `docs/DURUM.md`. Açık işler: `docs/YOL-HARITASI.md`. Neden böyle yapıldı: `docs/KARARLAR.md`. Pulse entegrasyonu: `docs/PULSE-ENTEGRASYON.md`.

## Çalışma kuralları (kullanıcının tercihleri — bozma)

1. **Dürüst ol.** Neyi *gerçekten* doğruladığını (gerçek donanım / ekran görüntüsü / test) ile neyi doğrulamadığını (yalnızca kod yazıldı, sahte oyunla denendi) açıkça ayır. Kullanıcı bunu özellikle istedi.
2. **Doğrulamadan "bitti" deme.** Arayüz işinde ekran görüntüsü al ve bak; davranış işinde otomatik denemeyle çalıştır. Hata bulursan gizleme, düzelt.
3. Uzun/riskli işe başlamadan önce kısa plan ver; kullanıcı "başla" dediyse sorma, yap. Tasarım kararı gerçekten kullanıcıya aitse (görünürlük, kapsam, geri alınamaz şey) sor.
4. Dışarıya açık/geri alınamaz işlemler (GitHub'a push, release, repo görünürlüğü, silme) **yalnızca kullanıcı istediğinde**. Push/release istenmişse yap ve linki ver.
5. Kullanıcı verisine dokunma (bkz. aşağıdaki tuzaklar). Kullanıcının çalışan Konsol örneğini test için kapatmak gerekirse **bunu söyle** ve iş bitince yeniden başlat.
6. Mesaj üslubu: kısa, madde madde, abartısız; neyi yaptığını, neyi doğruladığını, neyi yapmadığını ve sıradaki adımı yaz.
7. Commit mesajlarının sonuna (ortamın verdiği) `Co-Authored-By` satırı eklenir. Depo tek temiz commit geçmişiyle yayınlandı; eski ayrıntılı geçmiş yalnızca kullanıcının yerel `local-history` dalında.

## Komutlar

```powershell
dotnet build -c Release                                  # bin\Release\net8.0-windows10.0.19041.0\KlycKonsol.exe
dotnet test tests/KlycKonsol.Tests                       # 64+ birim testi (Debug derler!)
powershell -ExecutionPolicy Bypass -File .\Yayinla.ps1   # dist\KLYC-Konsol-<sürüm>-win-x64.zip
powershell -ExecutionPolicy Bypass -File .\Guard\build.ps1   # KlycGuard.exe (MSVC gerekir; derlenmiş exe depoda)
gh release create v<sürüm> dist\KLYC-Konsol-<sürüm>-win-x64.zip --repo ernklyc/klyc-konsol --title "..." --notes "..."
```

Sürüm çıkarma sırası: `KlycKonsol.csproj` `<Version>` → `CHANGELOG.md` → build + test → `Yayinla.ps1` → commit/push (`main`) → `gh release create` → CI'ın (`build` iş akışı) yeşil olduğunu doğrula (`gh run list`).
GitHub girişi düşerse: `gh auth login --web -h github.com -p https -s repo,workflow` çalıştırıp kullanıcıya **tek seferlik kodu** ver (kullanıcı tarayıcıda girer; kodlar ~15 dk geçerli).

## Mimari haritası

```
KlycKonsol.csproj      WPF uygulaması (net8.0-windows10.0.19041.0 — Bluetooth WinRT için)
MainWindow.xaml(.cs)   Kabuk (IShell): sayfalar, üst çubuk, ipuçları, toast, kaplamalar, WebLayer; Launch / HideLauncher / ShowLauncher / bekçiye devir
App.xaml(.cs)          Tek örnek mutex'i, çökme günlüğü, bekçiden açılış (--from-guard)
Input/                 XInputPad (4 yuva), DesktopMode (kol=fare), PowerMode, GameSession (güç planı+bildirim), GuardLauncher, WifiService (netsh),
                       BluetoothService (WinRT), Brightness (WMI), SystemAudio, AudioDevices (çıkış değiştirme), DisplayModes (çözünürlük/Hz), WinMmPads + HidPadMapper (PS/Switch),
                       PulseLink (özet/komut/durum), PulseCommandTracker, PadBatteryWatch, StartupPolicy, UiSound, Native
Library/               GameScanner + Stores/* (mağaza tarayıcıları), ControllerSupportService (Steam appdetails), ArtService, GameMetaService, GameWatcher,
                       SteamOwnedService (kurulu olmayan), EpicFreeService, UpdateService + SelfUpdate (Velopack), Store.cs (AppState, AppPaths, UserLibrary), PlayTime, Vdf,
                       Loc (çeviri) + Localization/en.tsv, LibraryWatch (yeni oyun izleme), LibrarySorter, BrowserModes (YouTube TV), DownloadHelper, AppLog, SupportBundle
Models/Game.cs         Game (INPC), PadSupport, GameAvailability
Pages/                 LibraryPage, BrowserPage (sekmeler/yer imleri), SettingsPage, StartPage (tarayıcı başlangıç HTML'i)
Ui/                    NavBar (Pulse çipi), OnScreenKeyboard, ListMenu, ControllerPanel, WifiPanel, BluetoothPanel, DevicePanel (ekran+ses), AboutPanel, GameDetailPanel, TrayIcon, BackdropView, PerfMeter, Localizer
Guard/                 KlycGuard.c (saf Win32 bekçi) + build.ps1 + derlenmiş KlycGuard.exe
tests/KlycKonsol.Tests xUnit (gerçek manifest/yaml/xml/json örnekleriyle; TestIsolation veri klasörünü yönlendirir)
tests/e2e              Gerçek pencereyle UI Automation testleri (ui-smoke CI'da; browser-e2e, display-e2e elle)
```

Veri: `%AppData%\KLYC-Konsol` (`KLYC_DATA_DIR` ile değişir): `state.json` (AppState), `library.json` (elle eklenenler), `status.json` (Pulse için durum), `art\`, `meta\`, `epicfree.json`, `guard.json`, günlükler.

## Bilinen tuzaklar (geçmişte vakit/veri kaybettirdi — her seferinde uygula)

**Veri güvenliği**
- `AppState.Save()` gerçek `%AppData%\KLYC-Konsol\state.json`'a yazar. Testler/otomasyon **mutlaka** `KLYC_DATA_DIR` ile geçici klasöre yönlenmeli (birim testlerde `TestIsolation` yapar). Bir kez bu yüzden kullanıcının state'i ezildi.
- Otomasyonda "A" tuşu kütüphanedeki **ilk kartı** başlatır. Sahte oyunu `LastPlayed` ile en öne al, `SupportOverride: full` ver; aksi halde **gerçek bir Steam oyunu başlayabilir**. Sahte oyun: `PING.EXE`'yi `FakeGame.exe` olarak kopyala, `-t 127.0.0.1` ile çalıştır, `library.json` girdisi `Kind: game`.
- Wi‑Fi/Bluetooth/ağ/parlaklık/güç planı gibi sistem ayarlarını test için değiştirirsen **eski haline getir** ve kullanıcıya söyle. Kullanıcı bilgisayarında Wi‑Fi kartı bozuk (Ethernet kullanıyor): gerçek Wi‑Fi bağlanma denenemez.

**Derleme / test**
- `dotnet test` yalnızca **Debug** derler. Release exe'yi denemeden önce `dotnet build -c Release` çalıştır (eski exe ile test etmek defalarca yanılttı).
- Konsol tek örnektir (mutex `KLYC-Konsol-SingleInstance`). Test başlatmadan önce çalışan örnekleri kapat, yoksa komutlar kullanıcının örneğine gider. Çalışan exe derlemeyi kilitler.
- Bekçi (`KlycGuard.exe`) derleme çıktısına `Guard\KlycGuard.exe`'den kopyalanır; kaynağı değiştirirsen `Guard\build.ps1` ile yeniden derle ve exe'yi commit'le (SHA-256'yı `docs/GELISTIRICI.md`'ye yaz).

**Otomasyon bayrakları** (`docs/GELISTIRICI.md`): `--debugcmd` + `%AppData%\KLYC-Konsol\cmd.txt` (tuş adları, `tab:N`, `wait:ms`, `pad:LB,Guide`, `stick:…`), `--virtualonly` (gerçek kolu yok say), `--noguard` / `--forceguard`, `--noextras`, `--bench`.
Bekçi testi: Konsol'u `--debugcmd --virtualonly --forceguard` ile aç, "A" ile (seed'li) sahte oyunu başlat → bekçi devralır; Ctrl+Alt+K ile Konsol geri açılır (`SendKeys "^%k"`).
Ekran görüntüsü: DPI-aware yakala (ekran %125); önce Konsol'u öne getir. Konsol tam ekran/Topmost olduğunda görev çubuğu örtülür.

**Otomasyon (yeni, 1.0)**
- Ayarlar sayfasında satır sayısı değişir (Dil, Ekran ve ses, Sıralama, PULSE bölümü eklendi): eski "24×Down = Hakkında" bilgisi GEÇERSİZ. Otomasyonda "A" basmadan önce UI Automation ile doğru satırın seçili olduğunu doğrula (ör. değeri ◀ ▶ ile değişen bir satırda `→` görünmesi) — yanlış satırda A Yeniden Başlat/Kapat'a ya da Pulse'a gerçek komuta basabilir.
- `--debugcmd` komut dosyası art arda yazılırsa bir önceki komut dizisi bitmeden yenisi okunur ve tuşlar karışır; 26 tuşluk bir dizinin ardından en az 8–9 sn bekle.
- Çözünürlük testi (`tests/e2e/display-e2e.ps1`) ekranı GERÇEKTEN değiştirir; betik hata olsa bile `finally` içinde eski kipi geri yükler. Elle çalıştırırken kullanıcıya haber ver.
- Tarayıcı testleri `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=…` ile sayfaya CDP üzerinden bakar; indirmeler için `KLYC_DOWNLOADS_DIR` ver (gerçek İndirilenler'e dosya düşmesin).
- Bu ortamda `python` yok (Microsoft Store takma adı bash'te sonsuza dek bekler): metin düzenlemek için `Edit`/`Write` kullan; `sed` ile `\u`, `\\` ve `\"` içeren C# satırları bozulur.
- `Localization/en.tsv` dosyasına eklerken satır sonu `\n` yazılır, sekme (TAB) ayraçtır; yeni metin için `LocTests` hangi anahtarın eksik olduğunu söyler.

**Performans ölçümü**
- WPF'te `CompositionTarget.Rendering`'e abone olmak boşta bile sürekli kare çizdirir → `PerfMeter` fps/"kasma" sayıları **yanıltıcı**. Gerçek profil için `dotnet-trace collect --profile dotnet-sampled-thread-time` + speedscope çıktısını analiz et; UI iş parçacığı çoğunlukla boştur.
- Gerçek takılma nedenleri: liste yeniden kurma (şablon+yerleşim 100 ms), arayüz iş parçacığında WMI/COM/ses yükleme. Ölçüm gürültülüdür (başka uygulamalar GPU'yu paylaşır); çözünürlük/pencere boyutu sonucu değiştirmedi → dolgu hızı sorunu değil.

**WPF / WebView2**
- WebView2 bir HWND'dir: `Viewbox` içinde ölçeklenmez, `MainWindow.WebLayer`'da yer tutucuya hizalanır; üstüne WPF çizilemez (menü açılırken web gizlenir).
- `Hidden` sayfalar yerleşimde kalır; uzun listeler `Canvas` içinde kaydırılır (WPF yerleşim kırpması). `ItemsControl` koleksiyonunu gereksiz `Clear/Add` ile yeniden kurma.
- Kaynak anahtarı çakışması (`Card` gibi) sayfa kaynaklarını bozar → özel adlar kullan.
- Tarayıcıda sol çubuk hem imleç hem ok tuşu olmamalı (`XInputPad.StickNavigation`).

**Araç / ortam (Windows + Git Bash + PowerShell)**
- Bash heredoc içinde uzun Python/C# metni ve ters eğik çizgiler **bozuluyor** (`\b`, `\n`, `\K`…) → dosyaya yazmak için `Write`/`Edit` araçlarını kullan; Python gerekiyorsa betiği dosya olarak yaz. `\b` kazara 0x08 karakteri üretir (csproj/markdown bozulur).
- `.ps1` dosyaları **UTF-8 BOM'lu** olmalı (Windows PowerShell 5.1 Türkçe karakterleri yoksa bozar).
- `powershell.exe`'yi bash'ten çağırınca `${env:ProgramFiles(x86)}` gibi parantezli ortam değişkenleri kaybolur → PowerShell aracından çalıştır.
- Bazı `Remove-Item` kalıpları güvenlik korumasına takılır → `[IO.File]::Delete` / `[IO.Directory]::Delete` kullan.
- Cursor/CRLF uyarıları zararsız; `git -c core.autocrlf=false commit` kullanıldı.

## Kod yazarken

- Mevcut stile uy: Türkçe XML-doc yorumları ("neden"i anlatan), kısa metotlar, `IShell` üzerinden sayfa–kabuk iletişimi, arayüz iş parçacığında ağır iş yok (`Task.Run`).
- Oyun sırasında Konsol'un **hiçbir şey yapmaması** ana tasarım ilkesidir: zamanlayıcı, yoklama, önbellek bırakılmalı; yeni özellik bunu bozmamalı.
- Her yeni saf mantık için birim test (gerçek örnek veriyle); ağ/sistem çağrıları ince sarmalayıcıda kalsın.
- Kullanıcıya görünen her metin Türkçe yazılır **ve** `Localization/en.tsv`'ye İngilizcesi eklenir (`LocTests` eksikleri yakalar; çalışma anında oluşan metin için sözlükte `{0}` kalıbı yazılır). Yeni arayüz kol ile (D-pad) erişilebilir olmalı: odak, geri (B), ipucu çubuğu (`SetHints`).

# Tasarım kararları ve nedenleri

Format: **karar** — neden (— alternatifler / bedel). Yeni önemli karar verince buraya ekle; eski bir kararı değiştirirsen eskisini silme, "yerine geçti" diye işaretle.

## Teknoloji

- **WPF (.NET 8) + XInput** — Node/Electron yok; XInput'u arka planda okuyup Guide ile oyundan Konsol'a dönebilmek gerekiyordu; WPF + P/Invoke hafif ve bağımlılıksız. WebView2 yalnızca tarayıcı için.
- **Guide düğmesi `XInputGetStateEx` (ordinal #100)** — belgesiz ama Guide'ı verir. Windows 11 Game Bar Guide'ı yakalar → ilk açılışta "Game Bar'ı kapatalım mı?" sorulur (registry `GamepadNexusChordEnabled=0`), Ayarlar'da anahtar var.
- **Hedef `net8.0-windows10.0.19041.0`** — Bluetooth WinRT API'leri (eşleştirme) için; sonuç: Windows 10 2004+, çıktı klasörü değişti, zip ~1 MB → ~7 MB. Alternatif: Bluetooth'u Windows ayarlarına yönlendirmek (elendi).
- **Bildirim alanı simgesi ham `Shell_NotifyIcon`** — WinForms bağımlılığı ve çakışan tür adları (WPF/WinForms) yüzünden; simge exe'ye gömülü ikondan `ExtractIconEx` ile alınır.
- **Sesler kodla üretilir** (sinüs tonları → WAV, `SoundPlayer`) — dosya/ek bağımlılık yok; ilk çalmada takılmasın diye arka planda ısıtılır.
- **Wi‑Fi `netsh wlan`** (yönetici izni gerekmez, dilden bağımsız ayrıştırma); **parlaklık WMI** (`System.Management`); **medya tuşları** sentetik tuş (GSMTC/WinRT'ye gerek kalmadı).

## Görsel / performans

- **Gradyanlar bir kez bitmap'e pişirilir, `BlurEffect` yok, kartlarda `BitmapCache`, sayfalar `Hidden`** — tam ekran blur + üst üste şeffaf katmanlar iGPU'da çok yavaştı (47→60 fps iyileşmesi). Ekran iGPU'ya (Intel UHD 630) bağlı; NVIDIA tercihi denendi, kötüleştirdi, geri alındı.
- **Uzun listeler `Canvas` içinde kaydırılır** — WPF yerleşim kırpması taşan satırları çizmiyordu (kullanıcının "bazı yerlere erişemiyorum" şikâyetinin gerçek sebebi).
- **Kütüphane listesi değişmediyse koleksiyona dokunulmaz; arka plan güncellemeleri görünmeyen sayfada ertelenir (`_dirty`)** — yeniden kurma 100+ ms takılma yapıyordu.
- **UI Automation ağacı kapalı (`OnCreateAutomationPeer → null`)** — bir otomasyon istemcisi dinlerken sekme geçişinde 25 ms; bedeli: ekran okuyucu desteği yok (kol arayüzü; istenirse #17'de yeniden açılır).
- **Kapak sığdırma:** 2:3'e yakın değilse bulanık dolgu + olduğu gibi (kesmeden). Kullanıcı kesik kapak istemiyor.

## Oyun modu (evrim)

1. İlk sürüm: arayüz gizlenir, süreç Idle öncelik + EcoQoS + bellek iadesi, kol yoklaması ~11 Hz (≈35–42 MB, %0,5).
2. **0.7.0: Konsol tamamen kapanır, `KlycGuard.exe` bekçisi kalır** — hedef "gerçekten hafif" (≈1,5 MB, %0,1–0,3, WebView2 yok). **Bekçi saf C** (.NET çalışma zamanı bile ~20 MB+ tutardı); MSVC ile derlenir, derlenmiş exe depoda (`dotnet build` her makinede çalışsın). Bedeli: Guide'dan sonra Konsol ~3 sn'de açılır; antivirüs şüphesi.
- Bekçi **tek seferlik**: Konsol'u açınca kendini kapatır (`KlycKonsol.Guard.Stop` olayı); oyun bilgisi argümanla, devralma için `guard.json`; oyun bitince Konsol `--from-guard --game-exited …` ile açılır, otomatik dönüş kapalıysa `--silent` (arayüz açmadan süre kaydı + ayar geri yükleme).
- Oyun izleme: kurulum klasöründeki süreçler (yol önekiyle); başlatıcı süreçler kapanıp asıl oyun gecikmeli gelebilir → ilk 2 dk'da 20 sn, sonra 6 sn bekleme; 25 sn sonra olay tabanlı bekleme (yoklama yok).
- Güç planı oyun sürerken (Konsol yeniden açılsa bile) **geri alınmaz**; yalnızca oyun bitince/çıkışta/çökme sonrası açılışta.

## Kütüphane / içerik

- **Yalnızca kol destekli oyunlar** (varsayılan): Steam `appdetails` `controller_support` full/partial ya da kategori 28/18; Steam dışı oyunlar Steam'de ada göre eşleştirilir; sonuç önbellekte; **kullanıcı elle düzeltebilir** (Start menüsü). Back tuşu filtreyi geçici kapatır.
- **Mağazalar arası tekilleştirme** (sıra: steam > epic > xbox > ea > gog > ubisoft > riot > battlenet > kısayol); kanonik başlık/takma adlarla.
- **"Yüklü değil" yalnızca Steam hesabında oynanmış (yerel `localconfig.vdf`) oyunlar** — tüm kitaplığı API ile çekmek anahtar/oturum gerektirirdi; adlar/kol desteği tek tek `appdetails` ile (1,3 sn arayla), önbellekli.
- **Epic ücretsiz oyunlar** herkese açık `freeGamesPromotions` ucundan (kimlik gerekmez), 6 saat önbellek.
- **Oynama süresi:** Steam için Steam'in kendi kaydı; diğerleri için Konsol'un kendi izlemesi (≥30 sn).

## Tarayıcı

- **Gerçek WebView2**; sekmeler tembel oluşturulur (yalnızca görüntülenen sekme süreç tutar), en çok 6; oturum `state.json`'da; oyun modunda hepsi kapanır. Menü/OSK açıkken web gizlenir (HWND hava boşluğu).
- Sol çubuk = imleç (ok tuşu üretmez; `StickNavigation`), aksi halde arayüz zamanlayıcılarını durduran ok tuşu yağmuru oluyordu.

## Dağıtım / depo

- **Public + MIT** (Pulse ile aynı); **tek temiz commit geçmişiyle yayınlandı** (eski geçmişte kullanıcının gerçek Steam hesap bilgileri vardı; testler artık sahte veri kullanıyor). Eski geçmiş yalnızca yerelde `local-history` dalında.
- **Çerçeveye bağlı yayın** (.NET 8 Desktop Runtime gerekir) + `Kur.ps1` (kullanıcı profiline, yönetici izni yok) — Velopack/MSI ileride (#9–11). Güncelleme denetimi yalnızca **sorar**, indirmez/kurmaz (Pulse ile aynı yaklaşım).
- **Hakkında/BMC:** destek bağlantısı Pulse'tan alındı (`buymeacoffee.com/13tpsxlcea`); hiçbir özellik buna bağlı değil. Logo Pulse'ın diliyle (koyu lacivert yuvarlak kare, beyaz simge) çizildi.
- **Testler gerçek kullanıcı verisine yazmaz** (`KLYC_DATA_DIR`) — bir kez state.json ezildi, ders çıkarıldı.

## Pulse ile ilişki

- **Dosya tabanlı haberleşme** (JSON + `FileSystemWatcher`): Pulse yönetici, Konsol standart kullanıcı; named pipe/HTTP elendi. Ayrıntı ve gerekçe: `PULSE-ENTEGRASYON.md`.
- Güç planında tek yetkili Pulse (varsa) — çakışma ve "eski plana dönme" yarışını bitirmek için.

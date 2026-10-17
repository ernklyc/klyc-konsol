# Proje durumu

**Güncel sürüm: 1.0.1** (GitHub Releases'te, 257 birim testi + gerçek pencereyle arayüz testleri). Son güncelleme: 2026-10-12.
Bu belge "ne var, ne kadar doğrulandı" sorusunun tek kaynağıdır. Her değişiklikten sonra güncelle.

Doğrulama düzeyleri: **✔ gerçek** = kullanıcının bilgisayarında gerçek veriyle/donanımla görüldü · **◐ sahte** = sahte oyun/sanal tuşla otomatik denendi ·
**✖ denenmedi** = kod yazıldı, doğrulanmadı · **birim** = birim testi var.

## Sürüm geçmişi (kısa)

| Sürüm | Öne çıkan |
|---|---|
| 0.1–0.4 (yerel, depo öncesi) | WPF + XInput iskelet, klyc-box tasarımı, Steam/Epic tarama, tarayıcı (WebView2), masaüstü modu (kol=fare), oyun modu (düşük güç), çoklu kol, kalibrasyon, kol destekli oyun filtresi |
| 0.5.0 | Yeni mağazalar (Xbox, EA, Riot, GOG, Ubisoft, Battle.net), oynama süresi, oyun detayı, oyun oturumu (güç planı/bildirim), "Yüklü değil", Epic "Ücretsiz", Wi‑Fi paneli, parlaklık, medya tuşları, sesler/titreşim, tarayıcı sekmeleri/yer imleri, tepsi simgesi, paketleme, GitHub'a ilk yayın |
| 0.6.0 | Bluetooth paneli, README/belge yeniden düzeni, issue/PR şablonları |
| 0.7.0 | **KLYC Guard** (oyun sırasında Konsol kapanır), akıcılık düzeltmeleri, test izolasyonu |
| 0.8.0 | **KLYC Pulse ile birlikte çalışma** (durum şema 2, güç planı sahipliği, Pulse raporu), uyanma/ekran değişimi toparlama |
| 0.9.0 | **Açılışta kol yoksa gizli bekle** (`--startup`, gerçek pencerede 3 senaryo denendi; gerçek Windows oturum açılışında denenmedi) |
| 0.9.1 | Eski Pulse özeti oyun açılışını geciktirmesin |
| 1.0.0 | **Kurulum dosyası + uygulama içi güncelleme, Sorun bildir, İngilizce arayüz, PS/Switch kolları, yeni oyun izleme, son oynananlar/sıralama, ekran ve ses paneli, YouTube TV + indirme yöneticisi, Pulse ile iki yönlü çalışma (çip + mod/Hızlandır), otomatik arayüz testi** |
| (belge) | `docs/PULSE-ENTEGRASYON.md` tasarımı yayınlandı; 0.8.0'da çekirdeği uygulandı (kapsamı belgenin başındaki durum bölümünde), `CLAUDE.md` + proje belgeleri |

## Özellik durumu

| Alan | Durum | Not |
|---|---|---|
| Kütüphane görünümü (kapaklar, çipler, arama, favori, detay) | ✔ gerçek | Ekran görüntüsüyle; 12 kol destekli oyun (Steam/Epic/Xbox/EA) |
| Xbox, Riot, EA tarayıcıları | ✔ gerçek + birim | Gerçek kurulumlar bu bilgisayarda var |
| Steam/Epic tarama, oynama süresi (`localconfig.vdf`) | ✔ gerçek + birim | Steam dosyasıyla karşılaştırıldı |
| GOG, Ubisoft Connect, Battle.net tarayıcıları | ✖ denenmedi | Kayıt defteri okuyor; bu bilgisayarda kurulu değil |
| Kol desteği (Steam appdetails) + elle işaretleme | ✔ gerçek + birim | |
| "Yüklü değil" (Steam hesabı) | ✔ gerçek + birim | Kapaklar ve A → `steam://install` (indirme **başlatma** denenmedi) |
| Epic "Ücretsiz" | ✔ gerçek + birim | Canlı uç nokta; A → mağaza sayfası Konsol tarayıcısında açıldı |
| Oyun başlatma (Steam/Epic/Xbox) gerçek oyunla | ✖ denenmedi | Yalnızca sahte oyunla; kullanıcı gerçek oyunla denemeli |
| Oyun oturumu: güç planı, bildirim susturma, geri yükleme | ◐ sahte | Dengeli → Nihai → Dengeli doğrulandı |
| **Pulse bağlantısı** (durum şema 2, "Pulse var mı", güç planına dokunmama, geri yükleme koruması, Pulse raporu satırı, Hakkında durumu) | ◐ birim | 49 birim test + **gerçek Konsol penceresi ve gerçek Pulse birlikte, sahte oyunla uçtan uca** (durum dizisi launching→playing, Pulse varken/yokken/Pulse otomatik modu kapalıyken güç planı, oyun sırasında başkası planı değiştirirse ezilmez, Pulse'ın klasör ipucuyla oyunu bulması, oyun sırasında Pulse özet yazmaz, oyun bitince Konsol geri gelir; ekran görüntüsüyle detay satırı ve Hakkında durumu). **Gerçek (Steam/Epic) oyunla ve gerçek kolla denenmedi**
| Oyun kapanınca otomatik dönüş + süre kaydı | ◐ sahte | |
| **KLYC Guard**: devir, Ctrl+Alt+K ile geri açma, yeniden gizleme, oyun bitişi, sessiz bitirme, normal açılışta devralma | ◐ sahte | Bellek ~1,5 MB, CPU %0,1–0,3 ölçüldü |
| Guard'ın **gerçek kol Guide'ı** ile geri açması | ✖ denenmedi | XInput `GetStateEx` (ordinal 100) Konsol'daki ile aynı; kol elimizde yoktu |
| Tarayıcı: gezinme, sekmeler, yer imi, oturum geri yükleme, zoom, menü | ✔ gerçek | WebView2; oyun modunda süreçler kapanıyor |
| Wi‑Fi paneli: tarama/listeleme, şifre klavyesi | ✔ gerçek | Gerçek ağlar listelendi |
| Wi‑Fi'a **bağlanma / profil ekleme** | ✖ denenmedi | Kullanıcının Wi‑Fi kartı bozuk; birim: profil XML/ayrıştırıcılar |
| Bluetooth: aç/kapat, tarama, eşleşmiş cihaz listesi | ✔ gerçek | Radyo açılıp kapatıldı (eski haline dönüldü) |
| Bluetooth **eşleştirme / eşleme kaldırma** | ✖ denenmedi | Yakında eşleşme modunda cihaz yoktu |
| Parlaklık okuma | ✔ gerçek | Ayarlama (`WmiSetBrightness`) doğrulanmadı |
| Medya tuşları, ses kontrolü | ✖ / ◐ | Medya tuşu gönderimi çalan bir oynatıcıyla denenmedi |
| Arayüz sesleri, titreşim | ✖ denenmedi | Duyulamadı/hissedilemedi; üretim ve çağrı yolu birim/çalışma testli |
| Tepsi simgesi, sağ tık menüsü, sol tık, Ctrl+Alt+K | ✔ gerçek | Simge ilk açılışta gizli çekmecedeydi; "görev çubuğunda göster" kaydı sonraki açılışta etkili (doğrulanmadı) |
| Klavye/fare desteği, Alt+F4 onayı, gizle/çık düğmeleri | ✔ gerçek / ◐ | |
| Hakkında paneli, logo, BMC bağlantısı | ✔ gerçek | |
| Güncelleme denetimi (GitHub Releases) | ◐ | 404 ve "güncel" yolları denendi; "yeni sürüm var" yolu birim testli, gerçek yayınla denenmedi |
| Kur/Kaldir betikleri | ◐ | Sahte `LOCALAPPDATA` ile kurup kaldırıldı; gerçek kurulum denenmedi |
| Windows 10 | ✖ denenmedi | Yalnızca Windows 11'de çalıştırıldı; hedef 10 (2004+) |
| **Kurulum dosyası + uygulama içi güncelleme** (Velopack) | ◐ sahte kaynak | Gerçek kurulum → sahte güncelleme kaynağından yeni sürüme güncelleme → kaldırma bu bilgisayarda denendi; gerçek GitHub sürümünden "Güncelle" ilk yayından sonra denenecek |
| **Sorun bildir** (tanı paketi) | ◐ birim + gerçek | Kişisel veri temizliği birim testli; paket masaüstüne yazıldı |
| **İngilizce arayüz** | ✔ gerçek + birim | Gerçek pencerede UI Automation taraması: İngilizcede Türkçe harfli metin 0; ekran görüntüleriyle gözden geçirildi. Yalnızca ASCII harflerle yazılmış kaçak Türkçe sözcükler taramaya takılmaz: ekran görüntüsü taraması temiz, ama her ekran/durum görülmedi. Kaynak koddaki her görünür metin için çeviri testi var |
| PlayStation / Switch kolları (winmm + HID) | birim ✖ gerçek kol | Eşleme birim testli; **gerçek DualShock/DualSense/Switch Pro ile denenmedi**; bekçi (oyun sırasında) bu kolların PS/Home tuşuyla Konsol'u açamaz (yalnızca Xbox Guide) |
| Yeni oyunu kendiliğinden algıla (`LibraryWatch`) | birim ✖ gerçek kurulum | İmza ve izleyici (olay + bekletme) birim testli; gerçek Steam/Epic kurulumu denenmedi |
| "Son oynananlar" sekmesi, sıralama | ✔ gerçek / birim | Çip ekran görüntüsüyle görüldü (Türkçe + İngilizce); sıralama mantığı birim testli, Ayarlar'dan değiştirme gerçek pencerede kullanılmadı |
| Kol pili uyarısı | birim ✖ gerçek kol | Mantık birim testli; gerçek kablosuz kolda denenmedi |
| **Ekran ve ses**: ses çıkışı listesi, aynı aygıtı seçme | ✔ gerçek | 3 gerçek aygıt listelendi (STA iş parçacığında da); varsayılanı yine aynı aygıta ayarlama doğrulandı. **Başka aygıta geçiş denenmedi** (kullanıcının sesi değişmesin diye) |
| **Ekran ve ses**: çözünürlük / yenileme hızı + 15 sn onay | ✔ gerçek | 1920×1080@144 → 1680×1050@144 gerçekten uygulandı, onay verilmeyince ~14 sn'de eski kipe döndü, Konsol her iki kipte ekrana sığdı. Birden çok monitör, HDR, farklı yenileme hızı seçimi denenmedi |
| Tarayıcı: **YouTube TV kipi** | ✔ gerçek (sayfa) ✖ kolla | Gerçek WebView2'de TV kimliğiyle `youtube.com/tv` tam arayüz geldi, normal sekmelerde kimlik yok. **D-pad/A/B ile gerçek kolla gezinme denenmedi** |
| Tarayıcı: **indirme yöneticisi** | ✔ gerçek | Yerel sunucudan 3 MB: pencere açılmadı, güvenli ad, ikinci indirmede "(2)", menüde liste. İptal ve "klasörde göster" gerçek pencerede denenmedi |
| **Pulse ile iki yönlü çalışma** (üst çubuk çipi, canlı ısı, mod seç / Hızlandır) | ✔ gerçek (yetkisiz Pulse) | Gerçek Konsol penceresi + gerçek Pulse, yalıtılmış klasörlerle: izin kapalıyken komut reddedildi ve neden bildirildi; izin açıkken Konsol arayüzünden "Oyun" modu seçilip uygulandı, çip güncellendi, Günlük'e geri dönüldü; `exec` gibi komut reddedildi, hiçbir program açılmadı. **Yetkili (UAC) Pulse ile ve gerçek Hızlandır ile denenmedi** |
| Otomatik arayüz testi (CI) | ✔ yerel 23/23 + ✔ GitHub CI | `tests/e2e/ui-smoke.ps1` hem bu bilgisayarda hem GitHub'ın `windows-latest` makinesinde (`ui-smoke` işi) geçti; tarayıcı/ekran testleri elle |
| İlk açılışta "Windows ile başlat" kendiliğinden (1.0.1) | birim ✖ gerçek açılış | Mantık ve kayıt komutu birim testli; **gerçek Windows açılışında Konsol'un gelmesi denenmedi** (kayıt yazma bu araçta sanal olduğu için gerçek Run anahtarı Konsol'un kendi açılışında yazılır) |
| Uykudan uyanma / ekran kapanıp açılma | ✖ denenmedi | Kod hazır (0.8.0); bilgisayarı uyutmak otomasyonu keser, denenmedi |
| Konsol soğuk açılışı (Guide sonrası) | ✔ gerçek | ~3 sn |
| Akıcılık | ◐ | UI iş parçacığı %90+ boş; sekme geçişinde en uzun takılma ~70–85 ms (öncesi ~190 ms) |

## Ölçümler (bu bilgisayar: dizüstü, Intel UHD 630 + GTX 1650 Ti, ekran iGPU'ya bağlı, 1920×1080 @ 60 Hz, %125 DPI, Windows 11)

- Oyun sırasında **bekçi**: ~1,5 MB çalışma kümesi, %0,1–0,3 tek çekirdek (Idle öncelik). Eski "arka planda bekleyen Konsol": ~35–42 MB, ~%0,5; tarayıcı açıkken ek ~300 MB WebView2.
- Konsol (görünür) boşta bellek ve render notları `KARARLAR.md` / `CLAUDE.md` içinde.
- Boş WPF penceresi bu makinede 59–60 fps; `PerfMeter` rakamları yanıltıcı olduğundan fps hedefi için güvenilir tek ölçüt profil (bkz. `CLAUDE.md`).

## Kullanıcının bilgisayarındaki durum (bilgi)

- Wi‑Fi kartı bozuk (Ethernet). Bluetooth bağdaştırıcısı var (MediaTek). GameSir G7 SE + eski Xbox 360 kolları kullanıyor; test sırasında kol bağlı olmayabilir.
- Pulse, Sunshine (yayın) aynı makinede; Konsol Sunshine ile birlikte kullanılacak (uyumluluk denenmedi).
- Depo yerelde kullanıcının kullanıcı klasöründe `KLYC-Konsol` adıyla (git, `main`; eski ayrıntılı geçmiş yerel `local-history` dalında); Pulse yanında `Pulse` klasöründe.

## Bilinen sorunlar

1. Çok kartlı sayfalarda (50+ oyun, ilk dakikalarda arka planda kapaklar gelirken) ~80 ms'lik yerleşim takılması olabilir (kalıcı kütüphanede yok).
2. Bekçi imzasız küçük bir exe: bazı antivirüsler uyarabilir; SmartScreen "bilinmeyen yayıncı" gösterebilir (zip indirme).
3. Wi‑Fi şifre girişinde yazılan şifre ekran klavyesinde açık görünür.
4. Konsol açıkken görev çubuğu örtülür (tam ekran, Topmost) — tasarım gereği; erişim için Guide / Ctrl+Alt+K.
5. `status.json` oyun sırasında bayat kalır (Konsol kapalı); `pid` ölüdür. (Pulse entegrasyon belgesinde ele alındı.)
6. CI'da iki nullability derleme uyarısı (`GameMetaService.cs`, `StoreUtil.cs`) — zararsız, temizlenecek.
7. `PerfMeter` kare sayıları yanıltıcı (bkz. `CLAUDE.md`).

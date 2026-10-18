# Değişiklikler / Changelog

Biçim [Keep a Changelog](https://keepachangelog.com/tr/) esaslıdır; sürümleme [SemVer](https://semver.org/lang/tr/).

## [1.3.1] — 2026-10-18

### Düzeltilenler
- **Oyun hiç başlamazsa Konsol artık boşlukta bırakmıyor:** bekçi oyun sürecini 150 sn içinde görmezse (ör. Steam'den başlatılan EA oyunu: EA uygulaması açılıp kapanıp oyun hiç açılmadı) Konsol "oyun başlamadı ya da hemen kapandı; mağaza uygulamasından bir kez başlatmayı dene" bildirimiyle geri açılıyor. Eskiden bekçi 3 dk sonra sessizce izlemeyi bırakıyor, Konsol kapalı kalıyor, kullanıcı yalnızca Guide ile dönebiliyordu. Gerçek EA SPORTS FC 25 denemesinde görüldü: oyun Konsol'dan değil doğrudan Steam'den başlatılınca da açılmıyordu (EA uygulaması sorunu), bu yüzden bekçi tarafı düzeltildi.

## [1.3.0] — 2026-10-18

### Eklenenler
- **Oyun özeti:** oyundan çıkınca (2 dakikadan uzun oturumlarda) tam ekran "Oyun özeti" paneli: bu oturumun süresi, toplam süre ve Pulse varsa **Pulse raporu** (ort. FPS, ısılar, bulgular; önem durumuna göre renkli). Rapor birkaç sn sonra hazır olur, panel kendiliğinden güncellenir. A / B ile kapanır. Eskiden yalnızca 2 sn'lik bir bildirim vardı.
- **Güç ve çıkış menüsü:** üst çubuktaki güç düğmesi (ya da kolda sekme çubuğundayken **Y**) tek yerden: **Windows masaüstüne geç (klavye ve fare)**, **Konsol'u kapat**, Uyku, Yeniden başlat, Bilgisayarı kapat. Önceden yalnızca "Konsol kapatılsın mı?" soruluyordu.
- **Masaüstü modunda kolla tam kontrol:** **L3** pencere değiştir (Alt+Tab), **R3** Esc, **Back+X** Görev Yöneticisi, **Back+B** pencereyi kapat (Back basılıyken A/B tıklama üretmez); X zaten Enter, Y ekran klavyesi. İpucu çubuğu iki satırda hepsini gösterir.
- **Pulse'u kolla başlat:** Pulse kapalıyken bir Pulse satırına A basınca Pulse'un kendi "Windows ile başlat" görevi çalıştırılır (UAC çıkmaz; kolla UAC onaylanamaz). Görev yoksa nedeni söyler.
- **Sistem denetimi satırı** (Ayarlar → PULSE): Pulse'un salt okunur bulguları (sayfa dosyası, ekran kartı belleği, ısı, arka plan yükü…) tam ekran panelde; oyun özetinde Pulse bulguları 4 satıra çıktı.
- **Oyunda kapatılacak programlar** (Ayarlar → PULSE): Pulse'un son oyun raporlarından önerilen arka plan programlarından ◀ ▶ ile seç, A ile aç/kapat; seçilen program oyun açılınca Pulse tarafından durdurulur, oyun bitince geri açılır (Pulse ≥ 1.14.0).
- **Oyun sırasında ekran görüntüsü:** kolda **Back + X** (Start basılı değilken; Start+Back Konsol'a dönüş kısayolu): bekçi Windows'un kendi Win+PrtScn kısayolunu gönderir, görüntü Resimler\Ekran Görüntüleri'ne kaydedilir, kısa bip duyulur. Kaynak: `Guard/KlycGuard.c`; bekçi test bayrağı `--test-shot` ile gerçek Windows'ta doğrulandı (PNG oluştu); gerçek kolla Back+X denenmedi.
- **Düzeltme (gerçek oyun denemesinden):** bekçiyle başlayan oyunlardan dönüşte oyun özeti paneli açılmıyordu (yalnızca bildirim vardı); Pulse raporu Steam oyunlarında hiç eşleşmiyordu (Steam "son oynama"yı oyun bitince yazar) → rapor artık oturum başlangıcına göre eşleşir.

### Not
- Pulse 1.14.0 (oyunda işlemci frenlemesi FPS öncelikli, daha ucuz sensör okuması, sistem denetimi) ile birlikte çıkar; Konsol'un kendisi oyun sırasında hâlâ kapalıdır (yalnızca ~1,5 MB'lık bekçi çalışır).

## [1.1.0] — 2026-10-18

### Eklenenler
- **Konsol modu (Windows masaüstü yerine):** oturum açılınca Windows masaüstü ve görev çubuğu yerine doğrudan Konsol gelir. Küçük bir C gözetmeni (`KlycShell.exe`, 135 KB) kabuk olarak kaydedilir; Konsol'u başlatır, kapanırsa yeniden başlatır ve **her sorunda Windows masaüstüne döner** (Safe Mode, SHIFT basılı oturum açma, Konsol yok, `shell.off` dosyası, çökme döngüsü). Kapatınca önceki kabuk değeri geri yazılır. Varsayılan KAPALI, açmak onay ister. Gözetmen mantığı SAHTE Konsol/masaüstüyle gerçek süreçlerde 13 denetimle sınandı; gerçek Windows oturum açılışında denenmedi.
- **Windows masaüstüne geç**, **şifresiz otomatik oturum açma** (Windows'un `netplwiz` penceresini açar; şifreyi Konsol girmez) ve **Yönetici olarak başlat (UAC'siz)** (zamanlanmış görev; varsayılan KAPALI) satırları.
- **Pulse'u kolla tam yönet:** Ayarlar → PULSE'ta mod, Hızlandır, **Temizlik**, **oyun açılınca otomatik mod**, **ısı hedefi** (Kapalı/75/80/85/90 °C) ve **fan desteği** (Pulse ≥ 1.12.0; izin açıksa). Gerçek Konsol + gerçek Pulse ile uçtan uca denendi (otomatik mod açılıp kapandı, ayar dosyasına yazıldı).
- Konsol'dan çıkarken (Konsol modundaysa) önce Windows masaüstü açılır.
- **Koleksiyonlar:** oyunun üzerindeyken Start → "Koleksiyona ekle / çıkar"; kendi adınla (ekran klavyesi) en çok 8 koleksiyon. Kütüphane'de yeni **Koleksiyonlar** çipi (A: sonraki koleksiyon). Boşalan koleksiyon kendiliğinden kalkar.
- **Oyun başına Pulse modu:** Oyun ayrıntılarında "Bu oyun için Pulse modu" satırı (◀▶ seç, A uygula): o oyun için Pulse modunu seçer ya da o oyunda otomatik geçişi kapatır. Pulse ≥ 1.12.0 ve izin açıkken görünür; Pulse oyunu ilk oynayınca profil oluşturur (Konsol profil uyduramaz, yoksa satır bunu söyler).

### Değişenler
- Ayarlar'da satır sayısı arttı (SİSTEM: +4, PULSE: +4).
- **Boşta işlemci kullanımı düştü:** kol yoklaması uyarlanır (kol yokken 60 ms, girdi varken 12 ms, 2 sn boşta 30 ms; eskiden hep 12 ms). Ölçüm (Konsol ekranda, boşta, 40 sn x3): tek çekirdeğin ortalama ~%2,8 → ~%1,7. Denenip bırakılanlar: ReadyToRun (açılış 2,06 → 2,01 sn, fark yok), arka plan katmanını önbelleğe almak (fark yok). Bellek ~195 MB ağırlıkla WPF çalışma zamanı.
- 290 birim test (Konsol modu mantığı sahte kayıt defteriyle; gerçek Windows kaydına dokunulmaz).

## [1.0.1] — 2026-10-17

### Değişenler
- **Bilgisayar açılınca Konsol kendiliğinden açılır (PlayStation gibi):** ilk normal açılışta "Windows ile başlat" bir kez otomatik açılır ve "Açılışta kol yoksa gizli bekle" kapatılır; yani güç düğmesine basınca (oturum açıldığında) doğrudan arayüz gelir. İstemezsen Ayarlar'dan kapatırsın; kapattıktan sonra bir daha kendiliğinden açılmaz. Test/otomasyon açılışları gerçek Windows kaydına dokunmaz. 9 yeni birim test (toplam 257).
- Tam "açılır açılmaz" için ayrıca Windows'ta **otomatik oturum açma** gerekir (şifre ekranı çıkmasın): Windows ayarı olduğu için Konsol açmaz; `README` → "PlayStation gibi kullanmak" bölümünde adımlar var.

## [1.0.0] — 2026-10-12

Konsol artık "kurulum dosyası + kendi kendine güncelleme + iki dil + Pulse ile iki yönlü" bir ürün.

### Eklenenler
- **Kurulum dosyası ve uygulama içi güncelleme** (Velopack): çift tıkla kurulur (yönetici izni gerekmez), Hakkında'dan "Güncelle" ile yeni sürüm indirilip kurulur. `Kur.ps1` zip yolu da duruyor.
- **Sorun bildir:** Hakkında → tek tuşla kişisel veri içermeyen bir tanı paketi (günlükler, sürüm, ayar bayrakları; kullanıcı adı/bilgisayar adı/yol temizlenir) masaüstüne yazılır; kendiliğinden hiçbir yere gönderilmez. `konsol.log` izleme günlüğü (açılış, oyun, güç planı, Pulse, ekran) eklendi.
- **İngilizce arayüz:** Ayarlar → "Dil / Language" (Otomatik · Türkçe · English; Otomatik = Windows Türkçeyse Türkçe, değilse İngilizce). ~470 çeviri, çalışma anında oluşan metinler ("3 oyun gizli", "1 sa 30 dk") dahil; ekran klavyesi İngilizcede QWERTY. Yeni bir metin eklenip çevrilmezse birim testi kırılır (`LocTests`).
- **PlayStation (DualShock 4 / DualSense) ve Switch Pro kolları** köprü olmadan: winmm + HID eşleme, Xbox kolla aynı anda kullanılabilir.
- **Kütüphane:** yeni kurulan/silinen oyunlar kendiliğinden algılanır (Steam/Epic/kısayol klasörü izlenir; oyun sırasında hiçbir şey çalışmaz, dönünce bir kez bakılır) ve "Yeni oyun bulundu" bildirimi gelir · **"Son oynananlar"** sekmesi · **sıralama** (Akıllı · Son oynanan · A-Z · En çok oynanan; Ayarlar'dan).
- **Ekran ve ses (Ayarlar):** ses çıkış aygıtını değiştir (kulaklık ↔ hoparlör ↔ TV) · çözünürlük ve yenileme hızı seç. Ekran değişikliği güvenli: uygulanınca 15 sn içinde "Bu ayarı koru" denmezse (ya da panel kapanırsa) eski kipe kendiliğinden dönülür.
- **Kol pili uyarısı:** kablosuz kolun pili azalınca (ve bitmek üzereyken) bir kez uyarı.
- **Tarayıcı:** **YouTube TV kipi** (D-pad + A/B ile televizyon arayüzü; TV kimliği yalnızca o sekmede ve yalnızca YouTube/Google adreslerinde) · **indirme yöneticisi** (pencere yok; İndirilenler klasörüne güvenli ad, "(2)" çakışma çözümü; menüden ilerleme, iptal, klasörde göster).
- **Pulse ile iki yönlü çalışma ("Birlikte"):** üst çubukta Pulse modu ve (Pulse isterse) işlemci/ekran kartı ısısı; Ayarlar → PULSE: **mod seç / Hızlandır'ı çalıştır** (Pulse'ta "Konsol'dan mod değiştirmeye izin ver" açıksa; varsayılan KAPALI). Komut kanalı dar: yalnızca bilinen bir modu uygula ya da Hızlandır; Pulse her komutu (boyut, sahip, bağlantı noktası yok, 20 sn yaş, tekrar yok, hız sınırı, izin listesi) denetler ve sonucu Konsol'a bildirir.
- **Otomatik arayüz testi** (CI): `tests/e2e/ui-smoke.ps1` gerçek pencereyi UI Automation ile gezer (Türkçe + İngilizce tur). Elle çalıştırılan `browser-e2e.ps1` ve `display-e2e.ps1` da depoda.

### Düzeltilenler / iyileştirmeler
- Pulse özeti ve kol/ekran/ses işleri arayüz iş parçacığında bekletmiyor; ses aygıtı listesi Windows ses hizmeti meşgulken yeniden dener.
- Arayüz çevirisi yalnızca İngilizce seçiliyken ve Konsol görünürken çalışır (oyun sırasında sıfır yük ilkesi korunur).
- Birim testi 121 → 249.

## [0.9.1] — 2026-10-12

### Düzeltilenler
- Pulse'ı kaldırmış ya da kapatmadan sonlandırmış kullanıcıda, geride kalan eski özet dosyası her oyun açılışını 3 sn geciktirebilirdi. Özet 2 dakikadan eskiyse Pulse yok sayılır, beklenmez. Test eklendi (121 test).

## [0.9.0] — 2026-10-12

### Eklenenler
- **Açılışta kol yoksa gizli bekle:** "Windows ile başlat" artık `--startup` ile başlar. Bilgisayar açılınca kol takılıysa doğrudan Konsol arayüzü gelir (klavye/fare gerekmez); 8 sn içinde kol görünmezse Konsol ekranı örtmeden arka planda bekler (bekçi devralır), kolu takıp Guide'a ya da Ctrl+Alt+K'ya basınca açılır. Ayarlar'dan kapatılabilir (kapalıysa her açılışta ekranı kaplar). Elle başlatınca hep açılır.
- `Kur.ps1 -Otomatik` Run anahtarına `--startup` yazar. 7 yeni birim test (toplam 120).

## [0.8.0] — 2026-10-12

### Eklenenler
- **KLYC Pulse ile birlikte çalışma** (isteğe bağlı; Pulse yoksa Konsol bugünkü gibi çalışır):
  - Oyun başlatırken **Pulse çalışıyorsa ve "oyun açılınca modu kendiliğinden değiştir" ayarı açıksa güç planına dokunulmaz**, plan yalnızca Pulse'ta yönetilir (çakışma biter). Ayar kapalıysa Konsol bugünkü gibi kendisi yönetir (oyun hızlı plana geçmeden kalmasın). Konsol Pulse'ın özetini kontrol eder; özet bayatsa en çok 3 sn bekler, hiç yoksa gecikme olmaz.
  - `status.json` **şema 2** ve atomik yazım: yeni `launching` durumu (oyun başlamadan), oyun bilgisi (kimlik, ad, mağaza, klasör), güç planını kimin yönettiği. Eski alanlar (`gameId`, `since`) kalır.
  - Oyun detayında **"Pulse raporu"** satırı (süre, ortalama FPS, azami ısı, ilk iki bulgu; renk: yeşil/sarı/kırmızı) ve Hakkında'da Pulse durumu.
  - **Geri yükleme koruması:** oyun bitince etkin güç planı Konsol'un seçtiği plan değilse (Pulse ya da sen değiştirdiysen) eski plana dönülmez.
- Oyun kapanınca Pulse raporu hazır olunca tek satır bildirim ("Pulse raporu: ort. 118 FPS · ...").
- Ekran çözünürlüğü/ikinci monitör değişince Konsol yeniden sığar; uykudan uyanınca öne gelir (gerçek donanımda denenmedi).
- `powercfg`/kayıt defteri işleri arayüz iş parçacığında çalışmıyor (oyun başlatırken kapak ekranı takılmaz).
- Arayüz dışı hatalar `crash.log`'a yazılır; iki derleme uyarısı giderildi.
- 49 yeni birim test (`PulseLinkTests`, toplam 113).

## [0.7.0] — 2026-10-12

### Eklenenler
- **KLYC Guard (bekçi):** oyun başlayınca Konsol tamamen kapanır, yerine ~1,5 MB'lık küçük bir süreç kalır (Guide / Start+Back / Ctrl+Alt+K / simge ile geri açar, oyun kapanınca Konsol'u açar, bildirim alanı simgesini ve kısayolu devralır). Oyun sırasında bellek ~40 MB → ~1,5 MB, işlemci ~%0,5 → ~%0,1; tarayıcının WebView2 süreçleri de açık kalmaz. Ayarlar'dan kapatılabilir; bekçi bulunamazsa eski "arka planda düşük güç" moduna düşülür.
- Oyun açıkken Konsol başka yoldan (ör. kısayol) yeniden açılırsa oturumu devralır; güç planı oyun sürerken geri alınmaz.

### İyileştirmeler
- **Akıcılık:** Kütüphane listesi değişmediyse yeniden kurulmuyor (arka planda gelen kapak/kol desteği güncellemeleri 100+ ms'lik takılma yapıyordu); parlaklık WMI sorgusu ve arayüz sesleri arayüz iş parçacığından çıkarıldı; UI Automation ağacı kapatıldı. Sekme geçişlerindeki en uzun takılma ~190 ms → ~70 ms.
- Testler artık gerçek `%AppData%\KLYC-Konsol` klasörüne yazmıyor (`KLYC_DATA_DIR` ile yönlendirilir).

## [0.6.0] — 2026-10-12

### Eklenenler
- **Bluetooth paneli:** aç/kapat, eşleşmiş ve yakındaki cihazları (klasik + LE) listeleme, uygulama içinde eşleştirme (onay / PIN eşleşti mi / PIN gösterme), eşlemeyi kaldırma. Windows'un kendi eşleştirme API'leri kullanılır.
- README ve belgeler yeniden düzenlendi; geliştirici notları `docs/GELISTIRICI.md`'ye taşındı. Issue / PR şablonları ve güvenlik politikası eklendi.

### Değişenler
- Hedef platform `net8.0-windows10.0.19041.0` oldu (Windows 10 2004+). Derleme çıktısı `bin/Release/net8.0-windows10.0.19041.0/` altına gider.

## [0.5.0] — 2026-10-12

İlk herkese açık sürüm.

### Eklenenler
- **Kütüphane:** Steam, Epic, Xbox/Game Pass, EA App, Riot, GOG, Ubisoft Connect, Battle.net ve kısayol klasörü taraması; yalnızca kol destekli oyunlar; oynama süresi; oyun detay sayfası ve oyun başına ayarlar; "Yüklü değil" (Steam) ve "Ücretsiz" (Epic) sekmeleri.
- **Oyun modu:** oyun başlayınca Konsol arka plana iner (Idle öncelik, EcoQoS, tarayıcı kapanır), oyun kapanınca öne gelir; oyun sırasında güç planı (yalnızca prizdeyken) ve isteğe bağlı bildirim susturma, çökmeye karşı geri yükleme.
- **Tarayıcı:** WebView2 ile uygulama içi tarayıcı; 6 sekme, yer imleri, oturum geri yükleme, yakınlaştırma.
- **Ayarlar:** Wi‑Fi paneli, parlaklık, medya tuşları, ses, arayüz sesleri ve titreşim, kol (XInput, 4 yuva, Xbox 360 dahil) paneli ve kalibrasyonu.
- **Kol olmadan kullanım:** Ctrl+Alt+K ile aç/gizle, klavye ve fare desteği, bildirim alanı simgesi, Alt+F4 onayı, sağ üstte gizle/çık düğmeleri.
- **Hakkında:** logo, destek (Buy Me a Coffee), bağlantılar, günlük güncelleme denetimi (hiçbir şey indirmez).
- Paketleme: `Yayinla.ps1`, `Kur.ps1`, `Kaldir.ps1`.

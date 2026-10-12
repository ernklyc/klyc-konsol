# Hedef ve ilkeler

## Vizyon

Bilgisayarı **kanepeden, yalnızca controller ile**, bir konsol gibi kullanmak: oyunu seç, oyna, bitince geri dön; tarayıcı, Wi‑Fi, Bluetooth, ses gibi gündelik işler de kol ile.
Tasarım dili kullanıcının `klyc-box` projesinden (zemin `#0B0D11`, vurgu `#6178AD`, cam kartlar, kapsül sekme çubuğu) alındı.

## Kullanıcının asıl istekleri (sohbet boyunca söylediklerinin özeti)

1. **PS5 / Steam Big Picture gibi** tam ekran arayüz; fare/klavyesiz tam kullanım; "emülatör gibi PC'yi kolla kullanabilmek".
2. **Hiçbir yere sıkışmayan arayüz:** her sekmeye, her düğmeye D-pad ile erişilsin; kapaklar kesik olmasın; yazılar yarım kalmasın.
3. **Yalnızca controller destekli oyunlar** görünsün (kullanıcı elle düzeltebilsin).
4. **Gerçek uygulama içi tarayıcı** (sekme, yer imi), controller ile imleç/yazma.
5. **Oyunlarda neredeyse hiç yük:** oyun açılınca kapanmalı/uyumalı, oyun kapanınca geri gelmeli. Orta tuş (Guide) aç/kapa yapmalı.
6. Eski **Xbox 360 kolları** dahil çoklu kol desteği; çubuk kayması kalibrasyonu; pil göstergesi.
7. **Tüm mağazalar:** Steam, Epic, Xbox/Game Pass, EA, Riot, GOG, Ubisoft, Battle.net (PlayStation yok). "Yüklü değil" ve Epic "ücretsiz" oyunlar.
8. Oyun detayı, oynama süresi, oyun başına ayarlar; sistem paneli (Wi‑Fi, Bluetooth, parlaklık, medya); ses ve titreşim geri bildirimi.
9. **Kol olmadan da kilitlenmemek:** klavye, fare, kısayol, tepsi simgesi, çıkış yolları.
10. **Profesyonel yayın:** GitHub deposu, README, sürümler, kurulum/kaldırma, güncelleme denetimi, logo, "Hakkında" + Buy Me a Coffee.
11. **KLYC-Pulse ile entegrasyon** (kullanıcının diğer projesi): güç planı çakışmasının bitmesi, oyun sinyali, kol ile mod değiştirme, rapor — bkz. `PULSE-ENTEGRASYON.md`.
12. "Kusursuz, gerçekten çalışan, full optimize" — ve **neyin doğrulandığı konusunda dürüstlük**.

## Tasarım ilkeleri

- **Oyun sırasında sıfıra yakın yük.** Konsol oyun başlayınca kapanır; bekçi (`KlycGuard.exe`) yalnızca geri açar ve oyunun bitişini izler.
- **Controller-first.** Her şey D-pad + A/B/X/Y/Start/LB/RB ile; ipucu çubuğu her ekranda geçerli tuşları söyler.
- **Hiçbir şey zorunlu değil.** Pulse, bekçi, ağ, Bluetooth, Wi‑Fi yoksa uygulama bozulmadan çalışır; yoksa özellik gizlenir ya da açıklama gösterir.
- **Kullanıcıya sürpriz yok.** Sistem ayarlarını (güç planı, bildirim) değiştirirse oyun bitince/çökmede **geri yükler**; ağ yalnızca belgelenen yerlere çıkar; telemetri yok.
- **Düşük bağımlılık, yönetici izni yok.** Standart kullanıcı yetkisiyle çalışır; kurulum kullanıcı profiline.
- **Dürüst belgeler.** README'de yalnızca doğrulanmış/belirgin sınırlar; iç notlar `docs/` altında.

## Hedef olmayanlar

PlayStation/Switch kollarını yerleşik desteklemek (XInput köprüsü gerekir), bulut/hesap, çok oyunculu/sosyal özellikler, kendi mağazası, Linux/Mac.

## Başarı ölçütleri

- Oyun sırasında Konsol/bekçi toplamı ≲ 2 MB ve ≲ %0,3 çekirdek (ölçüldü: ~1,5 MB, %0,1–0,3).
- Kütüphane ve ayarlarda her öğeye kol ile ulaşılır; sekme geçişinde 100 ms üstü takılma yok (şu an ~70 ms'ye kadar düştü; bkz. `YOL-HARITASI.md`).
- Oyun açma → oynama → kapanma → geri dönüş döngüsü güç planını/bildirimleri bozmadan çalışır (sahte oyunla doğrulandı; gerçek oyunla kullanıcı denemeli).
- Yeni kullanıcı: zip → `Kur.ps1` → ilk açılışta çalışır.

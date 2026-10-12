# Geliştirici notları

Proje belgeleri: [CLAUDE.md](../CLAUDE.md) (çalışma kuralları ve tuzaklar) · [HEDEF](HEDEF.md) · [DURUM](DURUM.md) · [YOL-HARITASI](YOL-HARITASI.md) · [KARARLAR](KARARLAR.md) · [PULSE-ENTEGRASYON](PULSE-ENTEGRASYON.md)

## Yapı

```
KlycKonsol.csproj        WPF uygulaması (net8.0-windows10.0.19041.0)
Guard/                   KlycGuard.c (oyun sırasında Konsol'un yerine geçen küçük C programı) + build.ps1; derlenmiş KlycGuard.exe depoda
Input/                   XInput + PlayStation/Switch kolları (winmm/HID), masaüstü modu, güç modu, oyun oturumu, Wi-Fi / Bluetooth, ses çıkışı (AudioDevices), ekran kipleri (DisplayModes), Pulse bağlantısı (PulseLink, komut izleme)
Library/                 Mağaza tarayıcıları (Stores/), kol desteği, kapaklar, oynama süresi, güncelleme (UpdateService + Velopack SelfUpdate), durum (Store.cs), dil (Loc), yeni oyun izleme (LibraryWatch), sıralama, indirme/tarayıcı yardımcıları
Models/                  Game ve ilgili türler
Pages/                   Kütüphane, Tarayıcı, Ayarlar
Ui/                      Ortak denetimler: sekme çubuğu (Pulse çipi), ekran klavyesi, paneller (Ekran ve ses dahil), liste menüsü, tepsi simgesi, Localizer (İngilizce çeviri gezgini)
Localization/en.tsv     Türkçe → İngilizce sözlük (gömülü; LocTests kaynaktaki her görünür metnin çevrildiğini denetler)
tests/KlycKonsol.Tests   xUnit birim testleri (gerçek manifest / yaml / xml örnekleriyle)
tests/e2e                Gerçek pencereyle UI Automation testleri (ui-smoke CI'da; browser/display elle) — bkz. tests/e2e/README.md
```

Tasarım ilkeleri: oyun sırasında sıfıra yakın yük (görünmezken zamanlayıcı ve önbellek bırakılır), tek örnek (Mutex), HWND olan WebView2'nin
Viewbox dışındaki katmanda yer tutucuya hizalanması, uzun listelerin `Canvas` içinde kaydırılması (WPF yerleşim kırpması).

## KLYC Guard

`Guard/KlycGuard.c` saf Win32 (≈140 KB, statik CRT, yönetici izni/ağ/dosya yazma yok). Konsol oyun başlatınca ya da arka plana inince onu
`--konsol <exe> --dir <oyun klasörü> --game <kimlik> --started <unix> --autoreturn 0|1` ile başlatıp kendini kapatır; bekçi Guide / Start+Back
(XInputGetStateEx), Ctrl+Alt+K, bildirim alanı simgesi ve oyun süreçlerini (klasör eşleşmesi, `GameWatcher` ile aynı mantık) izler ve Konsol'u
`--from-guard [--game-exited --game .. --played N [--silent]]` ile açar. Konsol açılırken `KlycKonsol.Guard.Stop` olayıyla bekçiyi durdurur;
oturum bilgisi `guard.json`'da tutulur (bekçi çalışırken Konsol başka yoldan açılırsa devralınır).

Yeniden derleme (MSVC gerekir): `powershell -File Guard/build.ps1`; depodaki `KlycGuard.exe` SHA-256: `9cef4774e4db5e1bd4fdcbfea3cbd845f778fbf65e84e2536fc394727bb45f41`. `--test-guide <dosya>`: dosya oluşunca Guide basılmış sayılır (yalnızca test).

## Komut satırı bayrakları (yalnızca geliştirme)

| Bayrak | Ne yapar |
|---|---|
| `--startup` | Windows açılışından başlatıldığını bildirir (Run anahtarı yazar): kol 8 sn içinde görünmezse Konsol gizli beklemeye geçer (`StartupPolicy`) |
| `--bench` | Kare sürelerini `%AppData%\KLYC-Konsol\perf.log`'a yazar, sonra kapanır |
| `--debugcmd` | `%AppData%\KLYC-Konsol\cmd.txt` içindeki tuşları (`Right Down A Start Guide tab:1 wait:500 pad:RB stick:lx,ly,rx,ry,ms`) gerçek girdi gibi işler |
| `--virtualonly` | Gerçek kolları yok say (otomatik testte kullanıcının kolu karışmasın) |
| `--noguard` / `--forceguard` | Bekçiye devri kapatır / hata ayıklama bayraklarıyla bile açar |
| `--noextras` | Arka plan veri işlerini (kurulu olmayan Steam oyunları, Epic) kapatır |
| `--dumplibrary` | Kütüphaneyi `library_dump.txt`'e yazar |
| `--debugcmd` komutları | `tab:N`, tuş adları (`Right Down A Start …`), `wait:ms`, `pad:LB,Guide`, `stick:…`, **`url:<adres>`** (tarayıcıda yeni sekme). Komutlar sırayla işlenir; **bir sonrakini göndermeden öncekinin bitmesini bekle** (üst üste binerse tuşlar karışır) |

Test için ortam değişkenleri: `KLYC_DATA_DIR` (veri klasörü), `KLYC_PULSE_DIR` (Pulse özeti), `KLYC_DOWNLOADS_DIR` (tarayıcı indirmeleri gerçek İndirilenler'e düşmesin), `KLYC_HW_TESTS=1` (birim testlerinde gerçek ekran/ses aygıtını **değiştirmeden** sınayan testleri açar). `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` ile tarayıcı sayfalarına dışarıdan bakılabilir (`tests/e2e/browser-e2e.ps1`).
| `--nobackdrop`, `--nopad`, `--awaytest` | Ölçüm / hata ayıklama |

`%AppData%\KLYC-Konsol\status.json` Konsol'un durumunu verir (`launcher` / `playing` / `background` / `desktop`, oyun adı); başka araçlar okuyabilir.
Testler ve otomasyon için `KLYC_DATA_DIR` ortam değişkeni veri klasörünü değiştirir (gerçek kullanıcı verisine dokunmamak için). Hata günlüğü `crash.log`, tepsi simgesi günlüğü `tray.log`.

## Test

```powershell
dotnet test tests/KlycKonsol.Tests
```

Kapsam: mağaza tarayıcıları (Xbox, Riot, EA …), VDF / oynama süresi, güç planı ayrıştırma, Steam `appdetails`, Epic ücretsiz oyunlar, kurulu olmayan
Steam oyunları, Wi‑Fi (`netsh` çıktıları, profil XML'i), Bluetooth sıralama / durum metinleri, güncelleme denetimi, ses üretimi.

Kaynaktaki her **kullanıcıya görünen Türkçe metin** `Localization/en.tsv`'de olmalı: `LocTests.EveryVisibleSourceLiteral_HasATranslation` eksikleri listeler (satır: `Türkçe<TAB>İngilizce`, `{0}` yer tutucu, satır sonu `\n`). Çalışma anında oluşan metinler (`$"{n} oyun"`) sözlükte `{0} oyun` olarak aranır.
Gerçek pencereyle arayüz testleri: `tests/e2e/` (bkz. oradaki README); CI'da `ui-smoke` işi çalışır.

## Sürüm çıkarma

1. `KlycKonsol.csproj` içindeki `<Version>` ve `CHANGELOG.md` güncellenir; `dotnet test` + `tests/e2e/ui-smoke.ps1` yeşil olmalı.
2. `Yayinla.ps1` → `dist\KLYC-Konsol-Setup-v<sürüm>.exe`, `dist\KLYC-Konsol-<sürüm>-win-x64.zip` ve `dist\velopack\*` (güncelleme paketleri).
3. `gh release create v<sürüm> --title ... --notes ...` ile Setup.exe, zip **ve** `dist\velopack` içindeki `*.nupkg`, `releases.win.json`, `RELEASES`, `assets.win.json` yüklenir (uygulama içi güncelleme bunları okur); güncelleme denetimi bu sürümü görür.

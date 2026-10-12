# Arayüz (uçtan uca) testleri

Gerçek `KlycKonsol.exe`'yi **yalıtılmış** veri klasörüyle (`KLYC_DATA_DIR`), sahte oyunla ve sanal kolla (`--virtualonly`) açıp Windows UI Automation ile ekranı okur.
Kullanıcının gerçek verisine, kolu ya da ayarlarına dokunmaz. Önce `dotnet build -c Release` çalıştırın.

| Betik | Ne yapar | CI'da |
|---|---|---|
| `ui-smoke.ps1` | Kütüphane, sekmeler, Ayarlar, Ekran ve ses paneli, Pulse satırları; Türkçe + İngilizce tur (İngilizcede Türkçe metin kalmaz); çökme günlüğü yok | **evet** (`ui-smoke` işi) |
| `browser-e2e.ps1` | Tarayıcı: YouTube TV kipi (TV kimliği + tam arayüz), normal sekmede TV kimliği YOK, indirme (adlandırma, "(2)" çakışması, menüde liste). İnternet ister | hayır (elle) |
| `display-e2e.ps1` | Ekran çözünürlüğünü GERÇEKTEN değiştirir; 15 sn onaylanmazsa kendiliğinden eski kipe dönmeyi ve Konsol'un yeni boyuta uymasını doğrular. Hata olsa bile eski kipi geri yükler | hayır (elle; ekran yanıp söner) |

Elle çalıştırma: `powershell -ExecutionPolicy Bypass -File tests\e2e\ui-smoke.ps1 [-Exe <KlycKonsol.exe>] [-ShotDir <klasör>]`

Notlar: Konsol tek örnekli çalışır (mutex): kendi Konsol'unuz açıkken önce kapatın. `--debugcmd` komutları (`tab:N`, `Down`, `A`, `url:…`) bir sonrakinden önce bitmeli; betikler bu yüzden bekler.

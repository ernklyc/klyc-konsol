# KLYC Konsol ↔ KLYC Pulse entegrasyon tasarımı

Bu belge iki kardeş projenin nasıl birlikte çalışacağını anlatır: **KLYC Konsol** (controller ile PC'yi konsol gibi kullanma arayüzü) ve
**KLYC Pulse** (performans, ısı, pil ve bakım yöneticisi). Belge, Pulse üzerinde çalışan biri Konsol deposunu görmeden de uygulayabilsin diye
Konsol'un ilgili yönlerini de içerir. Konsol deposu: <https://github.com/ernklyc/klyc-konsol> (MIT).

> **Durum (2026-10-12): kısmen uygulandı, kapsam bilerek daraltıldı.** Uygulananlar: Kanal A (Konsol → Pulse, şema 2), Kanal B'nin çekirdeği (Pulse → Konsol özeti: "Pulse çalışıyor" işareti,
> mod, son oyun raporu), güç planı sahipliği (§6), Pulse tarafında erken oyun ipucu ve klasörden oyun algılama, Konsol'da oyun detayında "Pulse raporu" ve Hakkında'da Pulse durumu.
> **Yapılmayanlar (bilerek):** Kanal C (komut kutusu, kol ile mod değiştirme, Hızlandır): Pulse yönetici yetkisiyle kullanıcının yazabildiği klasörden komut alırdı; kazanç güvenlik yüzeyine değmedi.
> Üst çubuk ısı çipi, sensör özeti (`sensors`), `thermalGuard` alanı, Araçlar kartı ve pid ile yoklama azaltma (Faz 4) da yapılmadı.
> **Belgedeki farklar (kodla uyumlu olan bu bölümdür):** (1) `launching` yazılmadan önce Konsol `status.json`'u tazeler, Pulse'ın özeti ≥ taze değilse en çok 3 sn bekler (`PulseLink.PulseIsPresentAsync`); özet hiç yoksa beklemez.
> (2) Pulse özetini Konsol açıkken ~8 sn'de bir (20 sn tazelik kuralı için) ve Konsol durumu değişince hemen yazar; oyun sırasında yazmaz (oyun bitince rapor hariç). (3) `status.json` şema 2'de `game` nesnedir; şema 1 okuyucuları için `gameId` ve `since` üst düzeyde kalır.
> (4) Geri yükleme koruması: Konsol'un oyun başında seçtiği plan `AppState.AppliedPowerPlan` içinde saklanır; oyun sonunda etkin plan bu değilse eski plana dönülmez (§6.4).
> (5) **Güç planı sahipliği kuralı sıkılaştı:** Konsol planı Pulse'a yalnızca Pulse çalışıyorsa **ve** özetteki `autoGameMode` açıksa bırakır. Pulse'ın otomatik oyun modu varsayılan olarak kapalıdır; kapalıyken Pulse oyunda plana dokunmaz, Konsol da çekilirse oyun hiç hızlı plana geçmezdi. Kapalıyken Konsol planı kendisi yönetir (`powerPlanManagedBy: "konsol"`) ve Pulse'ın mod koruyucusu oyun bitene kadar bekler.
> (6) Oyun bitince Pulse raporu hazır olduğunda Konsol tek satır bildirim gösterir (yalnızca oyun BİTTİKTEN sonra, 20 sn'ye kadar bekler).
> Pulse tarafı: ernklyc/klyc-pulse (`Pulse.Core/Companion/KonsolLink.cs`, `Pulse.App/KonsolLinkService.cs`, `docs/MIMARI.md` "KLYC Konsol bağlantısı").
> **Güncelleme (1.0.0 / Pulse 1.11.0): Kanal C ve üst çubuk çipi artık VAR.** Aşağıdaki "Yapılmayanlar" paragrafı 0.8.0 anını anlatır; güncel durum:
> (a) **Konsol → Pulse komutu** (`%AppData%\KLYC-Konsol\pulse-command.json`; yalnızca `mode` + bilinen mod anahtarı ya da `boost`) — Pulse'ta **varsayılan KAPALI** ("Konsol'dan mod değiştirmeye izin ver"); her komut boyut, dosya sahibi, bağlantı noktası, 20 sn yaş, kimlik tekrarı, hız sınırı ve izin listesi denetiminden geçer; sonuç özetteki `lastCommand` ile döner. Sözleşme ve ayrıntı: Pulse `docs/MIMARI.md`.
> (b) **Üst çubuk çipi:** özetteki `modeTitle` + `live` (işlemci/ekran kartı ısısı). Konsol, durum dosyasına `wantsLive: true` yazarak ısıyı ister; Pulse yalnızca Konsol ekrandayken ve istendiğinde sensör okur.
> (c) Özete eklenen alanlar geriye uyumludur (`modeTitle`, `modes[]`, `live`, `lastCommand`); eski sürümler yok sayar. Hâlâ yapılmayanlar: `sensors` genel özeti, `thermalGuard` alanı, pid ile yoklama azaltma (Faz 4).

## Bu belgeyi uygulayacak kişi / yapay zekâ için (önce bunu oku)

1. **Önce Pulse tarafını yap** (§8): Faz 1 → Faz 2 → Faz 3 → Faz 4 sırasıyla, her fazı ayrı commit ve testle. Konsol tarafı (§9) ayrı bir işte, Konsol deposunda yapılır; Pulse'ta Konsol'a ait hiçbir dosya değiştirilmez.
2. **Pulse'a özgü sınırlar:** Pulse yönetici olarak çalışır (UAC); testler gerçek kullanıcı verisine dokunmamalı (`KLYC_PULSE_SETTINGS`, `KLYC_PULSE_TEST`, bu belgedeki yeni `KLYC_KONSOL_DIR` / `KLYC_PULSE_DIR`); hiçbir zaman uygulamayı doğrudan açıp
   gerçek ayarları değiştirme — mantık `Pulse.Core` içinde, birim/CLI testiyle doğrulanır.
3. **Konsol'un gerçek davranışını doğrulamak** için Konsol'u kurman gerekmez: §4.1'deki `status.json` ve §4.3'teki komut dosyalarını elle oluşturup Pulse'ın tepkisini test et. Sahte dosya örnekleri belgede var.
4. **Açık sorular (§11) için varsayılan cevaplar** (kullanıcı değiştirmediği sürece geçerli): 1) Pulse varsa Pulse güç planını yönetir; yoksa Konsol bugünkü gibi. 2) Konsol komutları Pulse'ta **varsayılan kapalı**. 3) Sürümler bağımsız, şema
   numarasıyla uyum. 4) Guard'a dokunulmaz. 5) Farklı yönetici hesabında entegrasyon devre dışı + durum satırında uyarı. 6) "Konsol'u Pulse ile başlat" varsayılan kapalı.
5. **Dil:** Pulse arayüzü `Loc.T(...)` ile çevrilir; yeni metinler Türkçe yazılıp `Loc` kalıbıyla eklenir. Kod yorumları ve belgeler Türkçe.
6. Bitirdiğinde: `CHANGELOG.md`, `README` (Kardeş proje bölümü), `docs/MIMARI.md`, testler ve (yeni sürüm gerekiyorsa) sürüm numarası güncellenir; Pulse'ın kendi sürüm tarifi (varsa) izlenir.

---

## 1. Amaç ve kapsam

**Neden birlikte?** İkisi de aynı olayın etrafında çalışıyor: *bir oyun başlıyor ve biri bitiyor.* Konsol oyunu başlatan taraf (hangi oyun, hangi mağaza,
kurulum klasörü, ne zaman başladığını kesin biliyor), Pulse ise oyunun bilgisayara nasıl davrandığını ölçen ve ayarlayan taraf (mod, ısı, FPS, rapor).
Bugün birbirlerinden habersizler ve **aynı şeyi iki kez, çakışarak yapıyorlar** (bkz. §3).

**Hedefler**

1. **Tek yetkili:** Güç planı / performans ayarlarını yalnızca Pulse yönetir; Konsol Pulse varken bunlara dokunmaz (çakışma biter).
2. **Kesin oyun sinyali:** Konsol'dan başlatılan oyunlarda Pulse oyunu *süreç görünmeden önce* ve doğru kimlikle (ad, mağaza, klasör) bilir; yoklama azalır.
3. **Controller ile Pulse:** Konsol'dan (kol ile) mod değiştirmek (Oyun / Günlük / Sessiz / Boşta), ısı ve modu üst çubukta görmek, "Hızlandır"ı başlatmak.
4. **Oyun raporu Konsol'da:** Pulse'ın "neden takıldı?" raporu oyun kapanınca Konsol'da (detay sayfasında) görünür.
5. **Birbirini bulma:** Pulse kuruluysa Konsol bilir ve gösterir; Konsol kuruluysa Pulse "Araçlar" sayfasında yardımcı olarak gösterir.

**Hedef olmayanlar**

- Pulse'ın yönetici izni gereksinimini değiştirmek ya da Konsol'u yönetici yapmak.
- Ağ üzerinden haberleşme, hesap, bulut.
- Konsol'un Pulse olmadan çalışmasını bozmak. **Her bağlantı isteğe bağlıdır; biri yoksa öteki bugünkü gibi çalışır.**
- Oyun sırasında ek işlemci/bellek yükü. Konsol oyun sırasında zaten kapalıdır (§2.3); entegrasyon bunu bozmamalı.

---

## 2. İki projenin ilgili yönleri (bugünkü durum)

### 2.1 KLYC Pulse (bu belgeyi uygulayacak taraf için özet)

- WPF, .NET 8, **`requireAdministrator`** manifestli (yönetici olarak çalışır). Ayarlar: `%LocalAppData%\Pulse\settings.json` (`SettingsStore`,
  `KLYC_PULSE_SETTINGS` ile değiştirilebilir). Son oyun raporu: `%LocalAppData%\Pulse\last_game_report.json` (`GameReportStore`).
- Modlar: `ModeDefinition` içinde anahtarlar `"oyun"`, `"gunluk"`, `"sessiz"`, `"bosta"`; tek giriş noktası `ModeController.ApplyAsync(key, overrides)`
  (uygula → geri oku → doğrula; aynı anda tek işlem). `ModeController.Applied` olayı var.
- **Oyun algılama:** `AutoModeService` 5 sn'de bir `GameDetector.FindRunningGameInfo` çağırır → `Process.GetProcesses()` ile tüm süreçlere bakar,
  görüntü yolu `\steamapps\common\`, `\Epic Games\`, `\XboxGames\`, `\Games\` … gibi işaretçilerden birini içeren ve penceresi/belleği olan süreci oyun sayar.
  `GameStarted` / `GameStopped` olayları ve `TuneGame` (GPU tercihi, süreç önceliği) buradan tetiklenir. `AutoGameMode` ayarı açıksa profil
  (`GameProfile`: `ExeName`, `ExePath`, `ModeKey`, `RefreshHz`, `Brightness`, `CpuMaxMhz`, `Enabled` …) uygulanır; oyun bitince önceki moda dönülür.
- `ModeKeeperService` ("mod koruyucu"): başka bir program güç ayarını bozarsa dakikada bir düzeltir.
- `GameReportService`: oyun açıkken `SensorService`'e abone olur (ısı, hız, GPU, bellek, FPS), kapanınca `GameSessionReport` üretir
  (`Game`, `Minutes`, `AvgFps`, `CpuTempMax`, `Bottleneck`, `Severity`, `Findings`, …).
- `SensorService.Latest` (`SensorSnapshot`: `CpuPercent`, `CpuMhz`, `CpuTempC`, `Gpu`, `RamPercent`, fan hızları). `ThermalGuardService` tehlikeli ısıda Sessiz moda geçer.
- `CompanionService`: ThrottleStop / Afterburner / G-Helper'ı Pulse ile birlikte açma ve moda göre profil uygulama kalıbı — Konsol için aynı kalıp kullanılabilir.
- `UpdateService`: GitHub'dan en son sürümü sorar (indirmez); `NoticeChip` küçük bildirimler gösterir.

### 2.2 KLYC Konsol

- WPF, .NET 8, **standart kullanıcı** (yönetici izni istemez). Veri klasörü `%AppData%\KLYC-Konsol` (`KLYC_DATA_DIR` ile değiştirilebilir; testlerde şart).
- Oyunları Steam, Epic, Xbox, EA, Riot, GOG, Ubisoft, Battle.net ve kısayollardan tarar. Her oyun için bilir: **kimlik** (`steam:1145360`, `epic:…`, `xbox:…`),
  **ad**, **mağaza**, **kurulum klasörü**, **başlatma hedefi**.
- Konsol bir oyunu başlatınca `GameSession.Begin` ile (yalnızca prizdeyken) Windows güç planını **Yüksek/Nihai performansa** geçirir ve isteğe bağlı
  bildirimleri susturur; oyun bitince geri alır (`GameSession.End`; eski plan `state.json`'da saklanır, çökmeye karşı).
- `%AppData%\KLYC-Konsol\status.json` (bugünkü şema, **Pulse için zaten hazır**):

  ```json
  { "state": "launcher|playing|background|desktop", "game": "Hades", "gameId": "steam:1145360", "since": 1791796212, "pid": 14660, "version": "0.7.0" }
  ```

  `game` / `gameId` yalnızca `playing`'de dolu. Yazım: `File.WriteAllText` (atomik değil).
- Kol yoksa/çıkarılırsa klavye ve fare çalışır; **Ctrl+Alt+K** Konsol'u açıp gizler.

### 2.3 Oyun sırasında Konsol KAPALIdır (önemli)

Konsol 0.7.0'dan beri bir oyun başlayınca **tamamen kapanır**; yerine `KlycGuard.exe` (≈1,5 MB, saf Win32) çalışır. Guard yalnızca: Guide / Start+Back /
Ctrl+Alt+K / tepsi simgesiyle Konsol'u geri açar; oyunun kurulum klasöründeki süreçleri izleyip oyun kapanınca Konsol'u açar. **Guard hiçbir dosya yazmaz ve
Pulse ile konuşmaz.** Sonuç: oyun sırasında Konsol tarafında canlı bir süreç yoktur → Pulse'a Konsol'dan *canlı veri beklenmemeli*; tüm sinyaller **oyun
başında ve sonunda** gelir. Bu, tasarımı sadeleştirir (olay tabanlı, yoklama yok).

---

## 3. Bugünkü çakışmalar ve riskler

| # | Sorun | Etki |
|---|---|---|
| 1 | **Güç planı iki kez yönetiliyor.** Konsol oyun başında planı Yüksek/Nihai yapıyor; Pulse modunu `powercfg` değerleriyle (turbo, en yüksek işlemci durumu, hız/güç dengesi) uyguluyor ve `ModeKeeperService` bozulan ayarı geri düzeltiyor. | Pulse Konsol'un plan değişikliğini "bozulma" sayıp geri alabilir ya da oyun sonunda iki taraf farklı "eski" plana dönebilir; kullanıcı Dengeli yerine yanlış planda kalabilir. |
| 2 | **Oyun iki kez algılanıyor.** Pulse her 5 sn'de tüm süreçleri geziyor (oyun sürerken de); Konsol zaten hangi oyunu başlattığını biliyor. | Gereksiz yoklama; Konsol dışı klasörlerde (`\Games\` kuralına uymayan) oyunu Pulse kaçırabilir, Konsol bilir. |
| 3 | **Süreç görününce geç kalma.** Pulse oyunu ancak süreç oluştuktan, penceresi açıldıktan sonra (≤5 sn) fark ediyor; mod geçişi oyunun ilk saniyelerine yetişmiyor. | Konsol başlatma anını önceden bildirebilir. |
| 4 | **Yetki farkı.** Pulse yönetici, Konsol standart kullanıcı. Yüksek bütünlük düzeyindeki bir süreç düşük düzeydekine yazabilir ama tersi için ACL gerekir. | Kanal seçimi (§4) buna göre yapıldı. |
| 5 | **Pulse çalışmıyor / Konsol kapalı** durumları. | Tüm okuyucular bayat veriyi (eski zaman damgası) yok saymalı. |

---

## 4. Mimari: dosya tabanlı iki yönlü kanal

**Seçim:** kullanıcı profilindeki küçük JSON dosyaları + `FileSystemWatcher`. Sebepleri: yönetici/standart kullanıcı farkı sorun olmaz (dosya sahibi kullanıcı, iki
süreç de o kullanıcının profilinde okur/yazar), ek bağımlılık/port/servis yok, **sıfır yoklama** (olay gelince uyanılır), çökme/yeniden başlatmada kalıcı, elle
incelenebilir. Değerlendirilip elenenler: *named pipe* (yönetici sunucuya standart istemcinin bağlanması için güvenlik tanımı gerekir, bağlantı kopması/yeniden
bağlanma karmaşası), *localhost HTTP* (port çakışması, yerel saldırı yüzeyi), *pencere mesajı* (veri taşımaz).

Üç kanal:

| Kanal | Yazan → Okuyan | Dosya | Ne zaman yazılır |
|---|---|---|---|
| **A. Konsol durumu** | Konsol → Pulse | `%AppData%\KLYC-Konsol\status.json` (genişletilmiş, **şema 2**) | Durum değişince (açılış, oyun başlıyor, oyun bitti, gizle/göster) |
| **B. Pulse akışı (feed)** | Pulse → Konsol | `%LocalAppData%\Pulse\konsol-feed.json` | Mod değişince, oyun raporu hazır olunca; **yalnızca Konsol açıkken** 5 sn'de bir sensör özeti |
| **C. Komut kutusu** | Konsol → Pulse | `%AppData%\KLYC-Konsol\pulse-cmd\<kimlik>.json` (+ `<kimlik>.result.json` yanıt) | Kullanıcı Konsol'dan bir Pulse eylemi seçince |

Genel kurallar: **UTF-8**, **atomik yazım** (aynı klasörde `.tmp` dosyasına yaz → `File.Move(overwrite)`; okuyucu yarım dosya görmez), her dosyada `"schema"`
ve `"updatedAt"` (UTC unix saniye), **bilinmeyen alanlar yok sayılır**, eksik alan = `null`, okuyucu hata verirse sessizce "veri yok" der.

### 4.1 Kanal A — `status.json` şema 2 (Konsol → Pulse)

Mevcut alanlar korunur (geriye uyumlu); yenileri eklenir:

```json
{
  "schema": 2,
  "updatedAt": 1791796212,
  "version": "0.8.0",
  "pid": 14660,
  "state": "launcher | launching | playing | background | desktop",
  "game": {
    "id": "steam:1145360",
    "title": "Hades",
    "store": "steam | epic | xbox | ea | riot | gog | ubisoft | battlenet | shortcut | custom",
    "installDir": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Hades",
    "target": "steam://rungameid/1145360",
    "startedAt": 1791796200
  },
  "powerPlanManagedBy": "konsol | pulse | none",
  "guard": true,
  "controllerConnected": true
}
```

- `state: "launching"` **yeni**: kullanıcı A'ya bastı, Konsol oyunu başlatmak üzere (süreç henüz yok). Pulse bunu görünce oyun profilini hemen uygulamaya başlayabilir (§5.A).
  Konsol `launching`'i `playing`'den ~2 sn önce yazar.
- `game` nesnesi `launching` / `playing` iken dolu, diğerlerinde `null`. Eski `game` (metin) ve `gameId` alanları **şema 1 okuyucuları için kalır** (iki sürüm boyunca) — ya da
  Pulse yalnızca `schema >= 2`'yi okur ve eskiyi görmezden gelir.
- `powerPlanManagedBy` (§6): Konsol, Pulse'ın çalıştığını anlarsa `"pulse"` yazar ve güç planına dokunmaz.
- `pid`: durumu en son yazan Konsol sürecinin kimliği. **Oyun sırasında Konsol kapalı olduğu için bu pid ölüdür**; Pulse "pid canlı mı" kontrolüyle durumu
  *doğrulamaya çalışmamalı*. `state: "playing"` + eski `updatedAt`, "oyun sürüyor olabilir ama bilgi eski" demektir; oyun bitince Konsol açılıp günceller.
- *(İsteğe bağlı, küçük Guard değişikliği)* Guard oyun bittiğinde `status.json`'a `state: "background"` + `game: null` + `lastGame: { id, playedSeconds }` yazabilir; böylece
  Konsol hiç açılmasa da Pulse bitişi dosyadan öğrenir. Pulse'ın kendi algılaması zaten bitişi bildiği için zorunlu değildir.

**Pulse tarafı okuma:** `FileSystemWatcher` (`%AppData%\KLYC-Konsol`, filtre `status.json`, `Changed`/`Created`), olay gelince 150 ms bekleyip (yazım bitsin) oku. Dosya yoksa/okunamazsa
"Konsol yok". `updatedAt` 24 saatten eskiyse "Konsol uzun süredir çalışmadı". Pulse'ın kendi `GameDetector` mantığı **yerinde kalır** (Konsol dışı oyunlar için); Konsol sinyali
onu *tamamlar*.

### 4.2 Kanal B — `konsol-feed.json` (Pulse → Konsol)

```json
{
  "schema": 1,
  "updatedAt": 1791796300,
  "pulseVersion": "1.10.0",
  "running": true,
  "mode": { "key": "oyun", "name": "Oyun", "applying": false },
  "power": { "onAc": true, "batteryPercent": null },
  "sensors": { "cpuTempC": 68, "gpuTempC": 61, "cpuPercent": 23, "cpuFanRpm": 3100, "gpuFanRpm": 2800, "throttleHint": null },
  "thermalGuard": { "active": false, "note": null },
  "autoGameMode": true,
  "acceptsCommands": true,
  "lastReport": {
    "game": "Hades", "startedAt": 1791790000, "minutes": 62.4, "avgFps": 118, "cpuTempMax": 84, "gpuTempMax": 71,
    "bottleneck": "gpu", "severity": 1, "findings": ["Ekran kartı oyunu sınırlıyor", "Arka planda Chrome %9 işlemci kullandı"]
  }
}
```

- `mode.applying`: `ModeController.IsBusy`. Konsol bunu "uygulanıyor…" olarak gösterir.
- `sensors` **yalnızca Konsol `launcher` durumundayken** (Kanal A'dan bilinir) ve en çok 5 sn'de bir güncellenir; Pulse zaten `SensorService` aboneliğiyle ölçüyor,
  ek maliyet bir `File.Replace`. Konsol açık değilken (oyun dahil) **bu dosya güncellenmez** ve ek abonelik açılmaz → oyun sırasında sıfır ek yük. `sensors` alanını Pulse
  Konsol `launcher` olmadığında `null` yazar ya da hiç dokunmaz.
- `lastReport`: `GameReportService.Ready` olayında (`GameSessionReport`'tan türetilir; en fazla 3 bulgu). Konsol oyun kapanınca açıldığında bunu okur.
- `acceptsCommands`: kullanıcı Pulse ayarında "Konsol komutlarına izin ver"i açtıysa `true` (§4.3).
- Pulse kapanırken `running: false` yazar (yoksa `updatedAt` bayatlar: 20 sn'den eski = çalışmıyor sayılır).
- Dosya yerleri: Pulse yönetici olarak çalışsa da dosya **kullanıcının** `%LocalAppData%`'ındadır (UAC aynı kullanıcı hesabında yükseltir; profil aynı). Standart kullanıcı Konsol
  okuyabilir. (Başka bir yönetici hesabıyla yükseltilirse profil farklıdır — o durumda entegrasyon sessizce çalışmaz; bkz. Açık soru 5.)

### 4.3 Kanal C — komut kutusu (Konsol → Pulse)

Konsol bir eylem seçince `%AppData%\KLYC-Konsol\pulse-cmd\<guid>.json` yazar:

```json
{ "schema": 1, "id": "b6f0…", "createdAt": 1791796400, "cmd": "apply-mode", "args": { "mode": "sessiz" } }
```

Pulse (yönetici) bu klasörü `FileSystemWatcher` ile izler, **izin verilen komut listesine** göre işler ve aynı klasöre `<guid>.result.json` yazar:

```json
{ "id": "b6f0…", "ok": true, "message": "Sessiz mod uygulandı", "finishedAt": 1791796403 }
```

Komut listesi (**allow-list**, başka hiçbir şey çalıştırılmaz):

| `cmd` | `args` | Pulse'ın yaptığı |
|---|---|---|
| `apply-mode` | `mode`: `oyun` \| `gunluk` \| `sessiz` \| `bosta` | `ModeController.ApplyAsync(mode)` |
| `boost` | — | "Hızlandır" (geçici dosya temizliği + bellek rahatlatma); sonucu `message`'a özet olarak yazar |
| `open-window` | `page`: `home` \| `games` \| `monitor` | Pulse penceresini öne getirir (kullanıcı masaüstü modundayken) |

**Güvenlik** (Pulse yönetici olduğu için kritik): (1) yalnızca allow-list; argümanlar doğrulanır (mod anahtarı sabit küme); (2) dosya sahibi **oturumdaki kullanıcı** olmalı
(`File.GetAccessControl().GetOwner` ya da klasör ACL'i yalnızca o kullanıcıya yazma verir); (3) `createdAt` 30 sn'den eskiyse reddedilir (tekrar oynatma); (4) hız sınırı (dakikada ≤ 10);
(5) komut dosyası işlendikten sonra silinir; sonuç dosyaları 60 sn sonra temizlenir; (6) **varsayılan kapalı olabilir**: Pulse Ayarlar → "KLYC Konsol ile çalış" altında
"Konsol komut gönderebilsin" anahtarı (Açık soru 2) — kapalıyken `acceptsCommands:false`, Konsol eylemleri gizler/devre dışı gösterir.

Konsol tarafı yanıtı `FileSystemWatcher` ile bekler (en çok 10 sn), sonra toast gösterir; yanıt gelmezse "Pulse yanıt vermedi".

---

## 5. Akışlar

### A. Konsol'dan oyun başlatma (en önemli akış)

```
Kullanıcı A → Konsol: status.json {state:"launching", game:{id,title,store,installDir}, powerPlanManagedBy:"pulse"}
              Konsol: (Pulse varsa) güç planına dokunmaz; bildirim susturma ayarı varsa uygular
              Konsol: oyunu başlatır (Process.Start / steam://…), ~2 sn sonra state:"playing", sonra Guard'a devredip kapanır
Pulse (FileSystemWatcher): "launching" görür →
    1. GameProfile'ı bul: ExePath == game.installDir ile başlayan ya da DisplayName == game.title eşleşmesi; yoksa oluştur (EnsureProfile benzeri; exe adı bilinmiyorsa
       InstallDir kaydı)
    2. AutoGameMode açıksa profil modunu uygula (oyun penceresi açılmadan önce) — _restoreTo mantığı değişmez
    3. Süreç görününce mevcut GameDetector yolu: GameStarted → GameReportService.Start; TuneGame (GPU tercihi, öncelik)
Pulse: oyun kapanınca GameStopped → önceki moda dön, rapor üret → konsol-feed.json.lastReport
Guard: oyun kapanınca Konsol'u açar → Konsol status.json {state:"launcher"} yazar → feed'i okur → "Hades kapandı · Rapor: ortalama 118 FPS, ekran kartı sınırlıyor" toast + detay satırı
```

**Pulse'ın dikkat edeceği noktalar:** `launching` sinyali bir *ipucudur*; süreç hiç gelmezse (başlatma başarısız, 180 sn) uygulanan mod geri alınır (`_autoApplied` mantığıyla). `GameStarted`
olayı iki kez tetiklenmemeli (aynı oyun için `launching` + süreç algılama tek oturum sayılır).

### B. Kol ile mod değiştirme

Konsol Ayarlar → **Pulse** satırı (yalnızca `konsol-feed.json` taze ve `acceptsCommands:true` ise etkin) → liste: `Oyun · Günlük · Sessiz · Boşta` (mevcut mod işaretli),
`Hızlandır`, `Pulse'ı aç`. A → Kanal C komutu → sonuç toast. Üst çubukta küçük bir çip: `🔥 Oyun · 68°C` (feed taze ve Konsol açıkken).

### C. Isı koruması

`ThermalGuardService` Sessiz moda geçince feed'de `thermalGuard.active=true` + `note`; Konsol açıksa toast ("Isı yüksek: Sessiz moda geçildi"). Oyun sırasında Konsol kapalıdır; kullanıcıya bildirim
Pulse'ın kendi `NoticeChip`/oyun üstü göstergesinden gider (değişmez).

### D. Oyun raporu Konsol'da

Oyun detay sayfasında (Start → Ayrıntılar) yeni satır: **"Pulse raporu"** — son oturum (süre, ortalama FPS, azami ısı, darboğaz, ilk 2 bulgu); `severity` rengi (0 yeşil, 1 sarı, 2 kırmızı).
`lastReport.game` ile Konsol oyununun eşleşmesi: `Game.Title` ≈ `report.Game` (Pulse exe adından türetir; Konsol karşılaştırmayı küçük harf + alfasayısal temizlikle yapar, tam eşleşme yoksa
`installDir` yolunda `report.Game` geçiyor mu bakar). Eşleşmezse satır gösterilmez.

---

## 6. Güç planı sahipliği protokolü (çakışma #1'in çözümü)

**Kural:** Pulse çalışıyorsa güç planını **yalnızca Pulse** yönetir.

1. Konsol açılırken ve her oyun başlatmadan önce `konsol-feed.json`'a bakar: `running == true` ve `updatedAt` ≤ 20 sn (Konsol açıkken Pulse 5 sn'de bir güncelliyor) ⇒ **Pulse var**.
2. Pulse varsa Konsol `GameSession.Begin(usePowerPlan: false)` çağırır (bildirim susturma bağımsız kalır, o Konsol'a ait), `status.json.powerPlanManagedBy = "pulse"` yazar.
   Pulse yoksa bugünkü davranış (`"konsol"`) sürer. Kullanıcı ayarı (`PowerPlanOnGame`) kapalıysa `"none"`.
3. Pulse, oyun başlarken `"powerPlanManagedBy":"pulse"` görürse modu normal uygular (zaten sahibi odur). `"konsol"` görürse (Pulse feed'i yeni açılmışsa yarış durumu) Konsol'un planına **mod
   koruyucu ile savaşmaz**: oyun bitene kadar `ModeKeeperService`'in plan değişikliklerini yok saymasını sağlayan basit bir bayrak (`KonsolOwnsPowerPlan`) ve oyun bitince
   kullanıcının *önceki* planına dönmeyi Konsol'a bırakır.
4. Çökme güvenliği: Konsol `state.json`'da eski planı saklar (`RestorePowerPlan`); Pulse bu sırada aynı planı değiştirmişse Konsol geri yüklerken Pulse'ın seçimini ezmemek için,
   geri yükleme öncesi mevcut plan Konsol'un *kendi uyguladığı* plan değilse (yani Pulse/kullanıcı değiştirmişse) **geri yükleme yapılmaz**. (Konsol tarafı değişikliği.)

---

## 7. Yük ve yoklama bütçesi

- **Oyun sırasında:** Konsol kapalı, Guard sıfıra yakın (~%0,1 çekirdek, 1,5 MB), Pulse oyun sırasında *hiçbir ek şey yapmaz* (feed'e yazmaz, abonelik açmaz). Entegrasyonun oyun sırasındaki ek maliyeti **sıfır**.
- **Pulse yoklaması azaltma (isteğe bağlı kazanç):** Konsol'dan başlatılmış oyun (`state:"playing"`, `game.installDir` dolu) için Pulse süreci bir kez bulduktan sonra 5 sn'lik tam
  süreç taramasını bırakıp **pid'i `WaitForSingleObject` ile bekleyebilir** (olay tabanlı, tarama yok). Pid kaybolunca `GameStopped`. Konsol dışı oyunlar için tarama aynen sürer.
- **Konsol açıkken:** Pulse 5 sn'de bir ~1 KB dosya yazar (mevcut `SensorService` aboneliğinden; yeni abonelik gerekmiyorsa abone sayısı değişmez); Konsol bir `FileSystemWatcher` kullanır, yoklama yok.
- Hiçbir tarafta `Process.GetProcesses()` eklenmez.

---

## 8. Pulse tarafında yapılacaklar (öneri)

Yeni (öneri adlarıyla):

1. **`Pulse.Core/Companion/KonsolLink.cs`** — saf mantık (test edilebilir): `status.json` şema 2 okuyucu (`KonsolStatus`), `konsol-feed.json` yazıcı (`PulseFeed`, atomik), komut doğrulayıcı
   (allow-list, yaş, hız sınırı), yol sabitleri (`KLYC_KONSOL_DIR` / `KLYC_PULSE_DIR` ortam değişkenleriyle değiştirilebilir — testler gerçek veriye dokunmasın).
2. **`Pulse.App/KonsolLinkService.cs`** — `FileSystemWatcher` ×2 (status.json, pulse-cmd), `ModeController.Applied` / `GameReportService.Ready` / `ThermalGuard` aboneliği,
   Konsol `launcher` iken 5 sn'de bir feed güncelleme, komut işleme (`ModeController.ApplyAsync`, boost). `AppServices`'e kayıt, `App.xaml.cs`'te başlat/durdur.
3. **`AutoModeService`** — dışarıdan `NotifyGameLaunching(KonsolGame)` girişi: profil eşleme/oluşturma + (AutoGameMode açıksa) mod uygulama; `_autoApplied`/`_restoreTo` mantığı tek yerde kalsın;
   "launching" sonrası 180 sn'de süreç gelmezse geri al; iki tetikleyiciyi tek oturumda birleştir. Konsol pid'i ile `WaitForSingleObject` seçeneği.
4. **`ModeKeeperService`** — `KonsolOwnsPowerPlan` bayrağı (§6.3).
5. **Ayarlar sayfası** — yeni bölüm "KLYC Konsol ile çalış": `LinkWithKonsol` (varsayılan açık), `KonsolCanSendCommands` (varsayılan **kapalı**; Açık soru 2), durum satırı
   ("Konsol bulundu · v0.8.0 · son görülme …" / "bulunamadı"). `SettingsStore`'a iki bool.
6. **Araçlar sayfası** — "KLYC Konsol" kartı: kurulu mu (`%LocalAppData%\Programs\KLYC-Konsol\KlycKonsol.exe` ya da Başlat menüsü kısayolu / `status.json` varlığı), "Aç", "Pulse ile birlikte başlat"
   (`CompanionService` kalıbı), GitHub bağlantısı (kurulu değilse indirme sayfası).
7. **Testler** — `Pulse.Cli`: `konsol-link-test` (şema ayrıştırma, bayat/eksik/bozuk dosya, komut doğrulama, atomik yazım, hız sınırı), `SelfTest`'e uçtan uca "sahte Konsol dosyası → mod uygulandı" adımı;
   CI'daki test listesine ekle.
8. **Belgeler** — README "Kardeş proje" bölümü + CHANGELOG; `docs/MIMARI.md`'ye `Companion/KonsolLink` satırı.

## 9. Konsol tarafında yapılacaklar (bu repoda, ayrı iş)

1. `PulseLink` servisi: `konsol-feed.json` izleme (`FileSystemWatcher`), bayatlık kuralı (20 sn), komut yazıcı + yanıt bekleyici.
2. `status.json` şema 2 (`launching`, `game{}`, `powerPlanManagedBy`, atomik yazım).
3. `GameSession`: Pulse varken güç planına dokunma; geri yüklemede "plan değiştirilmiş mi" denetimi (§6.4).
4. Arayüz: Ayarlar → Pulse satırı/paneli (`ListMenu`), üst çubuk çipi, oyun detayında "Pulse raporu" satırı, Hakkında'da Pulse kurulu/sürüm durumu.
5. Guard: (isteğe bağlı) oyun bitince `status.json`'a `lastGame` yazma.
6. Testler: `KLYC_DATA_DIR` + `KLYC_PULSE_DIR` ile yalıtılmış; sahte feed/komut dosyalarıyla.

---

## 10. Aşamalar ve kabul ölçütleri

| Faz | Kapsam | Kabul |
|---|---|---|
| **1** | Kanal A (Konsol→Pulse) + güç planı sahipliği (§6) | Konsol'dan oyun başlatınca Pulse oyun modunu oyun penceresi açılmadan uygular; oyun bitince önceki moda döner; Konsol güç planına **dokunmaz** (Pulse açıkken); Pulse kapalıyken Konsol eski davranışta |
| **2** | Kanal B (Pulse→Konsol): mod + sıcaklık çipi, oyun raporu satırı | Mod değişince ≤1 sn'de Konsol çipi güncellenir; oyun kapanınca Konsol raporu gösterir; oyun sırasında Pulse feed yazmaz |
| **3** | Kanal C: kol ile mod değiştirme, Hızlandır | Konsol'dan mod seçilince ≤3 sn'de uygulanır ve sonuç toast olur; allow-list dışı/eski/yabancı sahipli komut reddedilir (test) |
| **4** | Araçlar kartı, yoklama azaltma (pid bekleme), belgeler | Konsol dışı oyunlarda davranış değişmez; Konsol oyunlarında tam süreç taraması yapılmaz |

Her faz bağımsız yayınlanabilir; Faz 1 tek başına en büyük değeri (çakışmanın bitmesi) verir.

**Ortak test ilkesi:** testler gerçek kullanıcı verisine dokunmaz (Konsol: `KLYC_DATA_DIR`, Pulse: `KLYC_PULSE_SETTINGS` / `KLYC_PULSE_TEST`, yeni `KLYC_KONSOL_DIR`/`KLYC_PULSE_DIR`);
otomasyon "A" tuşuyla oyun başlatıyorsa sahte oyunun en öne alındığından emin olunur (gerçek oyun başlamasın).

---

## 11. Açık sorular (kullanıcı kararı)

1. **Güç planı sahipliği** §6'daki gibi "Pulse varsa Pulse" olsun mu, yoksa Konsol'un Yüksek/Nihai plan geçişi tamamen kaldırılıp yalnızca Pulse'a mı bırakılsın (Pulse yoksa da)?
   *Öneri:* Pulse varsa Pulse; yoksa Konsol'un bugünkü davranışı.
2. **Konsol komutları** (kanal C) Pulse'ta varsayılan **kapalı** (kullanıcı açar) mı olsun? Pulse yönetici olduğu için öneri: kapalı + ilk Konsol komutunda Pulse'ta tek seferlik onay chip'i.
3. **Entegrasyon sürümü:** Pulse ve Konsol sürümleri bağımsız mı ilerlesin (şema numarasıyla uyum) yoksa ortak bir "entegrasyon sürümü" mü yazılsın? *Öneri:* bağımsız, şema numarası.
4. **Guard'ın `status.json`'a oyun bitişini yazması** (küçük C değişikliği) yapılsın mı, yoksa Pulse kendi algısına mı güvensin? *Öneri:* Pulse kendi algısı yeterli; Guard'a dokunma.
5. **Farklı yönetici hesabı:** Pulse, standart kullanıcı yerine *başka bir yönetici hesabıyla* yükseltilirse `%LocalAppData%` farklı olur; bu durumda entegrasyon devre dışı kalsın mı
   (öneri: evet, durum satırında "farklı hesap" uyarısı), yoksa ortak bir klasör (`%ProgramData%\KLYC`) mı kullanılsın (öneri: hayır, izin sorunları getirir)?
6. **Pulse'ın Konsol kartında "Konsol'u Pulse ile başlat"** varsayılan kapalı mı olsun? (Öneri: kapalı.)

---

## 12. Ek: Konsol dosya ve bayrakları (Pulse geliştiricisi için kısa başvuru)

| Konu | Değer |
|---|---|
| Konsol veri klasörü | `%AppData%\KLYC-Konsol` (`KLYC_DATA_DIR` ile değişir) |
| Durum dosyası | `status.json` (bugün şema 1: `state`, `game`, `gameId`, `since`, `pid`, `version`) |
| Konsol çalışıyor mu | `Local\KLYC-Konsol-SingleInstance` adlı mutex (ya da `status.json` + canlı `pid`) |
| Guard çalışıyor mu | `KlycKonsol.Guard` adlı mutex; durdurma olayı `KlycKonsol.Guard.Stop` |
| Hızlı aç/gizle | Ctrl+Alt+K (global kısayol; Konsol ya da Guard dinler) |
| Kurulum yeri (Kur.ps1) | `%LocalAppData%\Programs\KLYC-Konsol\KlycKonsol.exe`, Başlat menüsü kısayolu "KLYC Konsol" |
| Konsol'u açma | `KlycKonsol.exe` (tek örnek; ikinci çalıştırma sessizce çıkar) |
| Arayüz parçaları (Konsol) | `Ui/ListMenu.cs` (liste menüsü), `Pages/SettingsPage.xaml.cs` (satırlar), `Ui/NavBar.xaml` (üst çubuk), `Ui/GameDetailPanel.cs` (oyun detayı) |
| Oyun modeli | `Models/Game.cs` (`Id`, `Title`, `Source`, `InstallDir`, `Target`) |
| Güç oturumu | `Input/GameSession.cs` (`Begin` / `End`, eski plan `state.json`'da) |

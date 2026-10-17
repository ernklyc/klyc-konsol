# Yol haritası ve açık işler

Efor: **S** küçük (bir oturum) · **M** orta · **L** büyük. Durum: ⏳ sırada · 🔵 tasarım hazır, uygulama bekliyor · ✅ bitti · ◐ kısmen · 💬 karar bekliyor · ⛔ bilerek yapılmayacak.
Öncelik sırası kullanıcıyla konuşuldu; kullanıcı "7 kalsın (uzaktan oynatma)" dedi. 1.0.0 ile "hepsini yap" turu tamamlandı (aşağıda ✅/◐ olanlar); kalanlar küçük.

## A. Sözleştiklerimiz

| # | İş | Durum | Not |
|---|---|---|---|
| 1 | Oyun sırasında sıfır yük (bekçi) | ✅ 0.7.0 | Gerçek kolla Guide denemesi hâlâ gerekli (`DURUM.md`) |
| 2 | Akıcılık (60 fps hedefi) | ◐ | UI-thread takılmaları düzeltildi; kalan: çok kartlı sayfa yerleşim maliyeti |
| 3 | **KLYC Pulse entegrasyonu** | ✅ 1.0.0 | Oyun ipucu, güç planı sahipliği, rapor, **üst çubuk çipi + mod seç / Hızlandır** (Pulse ≥ 1.11.0'da izin açıksa). Gerçek (yetkili) Pulse ile ve gerçek Hızlandır ile denemesi kullanıcıda |
| 4 | Gerçek kullanım doğrulaması | ⏳ kullanıcı | Gerçek Steam/Epic/Xbox oyunu aç-kapat; Bluetooth eşleştir; gerçek kolla bekçi Guide'ı; **gerçek PlayStation/Switch kolu**; gerçek açılışta "Windows ile başlat"; uyku/uyanma; Konsol'dan gerçek Pulse modu. Çıkan hatalar bu belgeye eklenir |

## B. Sağlamlaştırma

| # | İş | Efor | Durum |
|---|---|---|---|
| 5 | Derleme uyarıları (CI'daki 2 nullability), kullanılmayan kod | S | ⏳ |
| 6 | Otomatik arayüz testi (CI'da; yalıtılmış veri + sahte oyun + UI Automation) | M | ✅ 1.0.0 (`tests/e2e/ui-smoke.ps1`; tarayıcı/ekran testleri elle) |
| 7 | Çökme dayanıklılığı: Konsol çökerse otomatik yeniden başlatan koruma (oyun sırasında bekçi zaten ayakta) | S–M | ⏳ |
| 8 | Uyku/uyanma, ekran çözünürlüğü değişimi, kol toparlanması | M | ◐ ekran değişimi gerçek pencerede doğrulandı; uyku/uyanma kodlu, denenmedi |

## C. Dağıtım

| # | İş | Efor | Durum |
|---|---|---|---|
| 9 | Uygulama içi güncelleme (indir + kur) | M | ✅ 1.0.0 (Velopack; sahte kaynakla gerçek denendi, gerçek sürümle ilk yayında) |
| 10 | `winget` paketi; kod imzalama (ücretli, kullanıcı "kalsın" dedi) | M–L | ⏳ / ⛔ imza |
| 11 | Çift tıkla kurulan kurulum dosyası | M | ✅ 1.0.0 |

## D. Yeni özellikler

| # | İş | Durum |
|---|---|---|
| 12 | PlayStation / Switch kolları köprü olmadan (HID) | ✅ 1.0.0 (gerçek kolla denenmedi; tuş atama yok; bekçi PS/Home tuşunu görmez) |
| 13 | Kütüphane: son oynananlar, sıralama, yeni kurulumu algılama, koleksiyonlar | ✅ 1.0.0 / koleksiyonlar 1.1.0 (başarım/ekran görüntüsü yok) |
| 14 | Tarayıcı: indirme yönetimi, YouTube için TV modu | ✅ 1.0.0 (reklam engelleme, şifre yöneticisi yok) |
| 15 | Sistem: ses çıkışı değiştirme, çözünürlük/yenileme hızı, pil uyarısı | ✅ 1.0.0 (zamanlı kapatma yok) |
| 16 | **Konsol modu**: bilgisayar açılınca Windows kabuğu yerine doğrudan Konsol | ✅ 1.1.0 (isteğe bağlı, varsayılan KAPALI; kullanıcı açıkça istedi). Çok katmanlı geri dönüş ve geri alma yolları var (README). **Gerçek oturum açılışında denenmedi** |
| 17 | Çoklu dil (İngilizce) | ✅ 1.0.0 (açık tema, büyük yazı/erişilebilirlik yok) |
| 18 | Uzaktan oynatma (Sunshine/Moonlight) uyumu | ⏳ (kullanıcı "7 kalsın" dedi → ertelendi) |

### Konsol modu (#16): neden önce ertelendi, nasıl yapıldı

Windows kabuğunu Konsol ile değiştirmek, hata olursa bilgisayarı kullanılmaz bırakabilir; bu yüzden 1.0.0'da bilerek yapılmadı. Kullanıcı açıkça istedi ("PC'yi PlayStation gibi kullanmak") ve 1.1.0'da şu güvenceyle yapıldı: kabuk olarak doğrudan Konsol değil, **çıkmayan küçük bir gözetmen** (`KlycShell.exe`, C, ~135 KB) kayıtlıdır; o Konsol'u başlatır ve Safe Mode / SHIFT / `shell.off` / Konsol yok / çökme döngüsü durumlarında Windows masaüstünü (explorer.exe) açar. Yalnızca HKCU (tek kullanıcı), varsayılan KAPALI, tek tuşla geri alınır; son çare Görev Yöneticisi → explorer.exe. Kalan risk: gerçek oturum açılışında denenmedi; Winlogon'un tırnaklı yol + bağımsız değişkenli kabuk değerini kabul ettiği belgelenmiş davranış ama burada doğrulanmadı.

## E. Tanıtım

| # | İş | Efor |
|---|---|---|
| 19 | İngilizce README, demo GIF'i, basit tanıtım sayfası | S–M |

## Karar bekleyen konular 💬

1. `docs/PULSE-ENTEGRASYON.md` depoda kalsın mı (kullanıcı "GitHub'da olmasına gerek yok" dedi ama link için yayınlandı) — kullanıcı isterse kaldırılır.
2. Bekçiye PS/Home tuşu desteği (C kodu, gerçek kol olmadan doğrulanamaz): kol elimize geçince.

## Sonraki oturum için önerilen başlangıç

1. `git pull`, `docs/DURUM.md` ve `CLAUDE.md`'yi oku; kullanıcıdan gerçek kullanım geri bildirimini al (özellikle #4'teki "denenmedi" satırları).
2. Küçük işler: #5 (uyarılar), #7 (çökme koruması), bekçi PS tuşu, kütüphane koleksiyonları.

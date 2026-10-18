using System.IO;
using System.Text.Json;
using KlycKonsol.Library;
using KlycKonsol.Models;

namespace KlycKonsol.Input;

/// <summary>Pulse'ın son oyun raporu özeti (konsol-feed.json içinde).</summary>
public sealed record PulseReport(string Game, long StartedAt, double Minutes, double? AvgFps, double? CpuTempMax, int? GpuTempMax, string Bottleneck, int Severity, List<string> Findings);

/// <summary>Pulse'ın Konsol'a bıraktığı özet. Eksik alan = null; bilinmeyen alanlar yok sayılır.</summary>
/// <summary>Pulse'ın anlık ısı/yük değerleri (Konsol `wantsLive` isterse ve Pulse açıkken doldurulur).</summary>
public sealed record PulseLive(double? CpuTemp, double? GpuTemp, double? CpuLoad, double? GpuLoad, long At);

/// <summary>Pulse'ın bildirdiği seçilebilir mod (anahtar + Pulse dilindeki ad).</summary>
public sealed record PulseModeInfo(string Key, string Title);

/// <summary>Konsol'un gönderdiği son komutun Pulse tarafındaki sonucu.</summary>
public sealed record PulseCommandResult(string Id, string Action, bool Ok, string Message, long At);

/// <summary>Pulse'ın bildiği oyun profili: Mode = mod anahtarı, Enabled=false ise o oyunda otomatik geçiş kapalı.</summary>
public sealed record PulseGameProfile(string Name, string Mode, bool Enabled);

/// <summary>Pulse sistem denetimi bulgusu. Severity: 0 bilgi, 1 dikkat.</summary>
public sealed record PulseAuditItem(string Id, int Severity, string Title, string Detail);

public sealed record PulseFeed(int Schema, long UpdatedAt, string? PulseVersion, bool Running, string? ModeKey, bool ModeApplying, bool AcceptsCommands, PulseReport? LastReport, bool AutoGameMode = false,
    string? ModeTitle = null, List<PulseModeInfo>? Modes = null, PulseLive? Live = null, PulseCommandResult? LastCommand = null, int? HeatTarget = null, List<PulseGameProfile>? GameProfiles = null, List<PulseAuditItem>? Audit = null);

/// <summary>
/// KLYC Pulse (kardeş proje) ile dosya tabanlı haberleşme: Pulse'ın `konsol-feed.json` özetini okur, Konsol kendi durumunu `status.json` (şema 2) olarak yazar.
/// Her şey isteğe bağlıdır: dosya yoksa, bozuksa ya da bayatsa "Pulse yok" sayılır ve Konsol bugünkü gibi çalışır.
/// Saf mantık burada, testler `KLYC_PULSE_DIR` / `KLYC_DATA_DIR` ile gerçek veriye dokunmadan çalışır. Tasarım: docs/PULSE-ENTEGRASYON.md.
/// </summary>
public static class PulseLink
{
    /// <summary>Pulse'ın canlı sayılması için özetin en çok bu kadar eski olması gerekir (Pulse Konsol açıkken ~8 sn'de bir yeniler).</summary>
    public const int FreshSeconds = 20;

    /// <summary>Taze değil ama "az önce yaşıyordu" sayılan süre: bu kadar yeni bir özet varsa Pulse yeni açılıyor olabilir, kısa süre beklenir.</summary>
    public const int RecentSeconds = 120;

    public static string PulseDir =>
        Environment.GetEnvironmentVariable("KLYC_PULSE_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse");

    public static string FeedFile => Path.Combine(PulseDir, "konsol-feed.json");
    public static string StatusFile => Path.Combine(AppPaths.Dir, "status.json");

    // ---- Pulse özetini okuma ----------------------------------------------

    public static PulseFeed? ReadFeed(string? path = null)
    {
        path ??= FeedFile;
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
            return ParseFeed(sr.ReadToEnd());
        }
        catch { return null; }
    }

    public static PulseFeed? ParseFeed(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            var mode = r.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.Object ? m : (JsonElement?)null;
            PulseReport? report = null;
            if (r.TryGetProperty("lastReport", out var lr) && lr.ValueKind == JsonValueKind.Object && Str(lr, "game") is { Length: > 0 } game)
                report = new PulseReport(game, Long(lr, "startedAt") ?? 0, Dbl(lr, "minutes") ?? 0, Dbl(lr, "avgFps"), Dbl(lr, "cpuTempMax"), (int?)Dbl(lr, "gpuTempMax"),
                    Str(lr, "bottleneck") ?? "unknown", (int?)Dbl(lr, "severity") ?? 0, Findings(lr));
            List<PulseModeInfo>? modes = null;
            if (r.TryGetProperty("modes", out var ml) && ml.ValueKind == JsonValueKind.Array)
                modes = ml.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && Str(x, "key") is { Length: > 0 }).Select(x => new PulseModeInfo(Str(x, "key")!, Str(x, "title") ?? Str(x, "key")!)).ToList();
            PulseLive? live = null;
            if (r.TryGetProperty("live", out var lv) && lv.ValueKind == JsonValueKind.Object)
                live = new PulseLive(Dbl(lv, "cpuTemp"), Dbl(lv, "gpuTemp"), Dbl(lv, "cpuLoad"), Dbl(lv, "gpuLoad"), Long(lv, "at") ?? 0);
            PulseCommandResult? cmd = null;
            if (r.TryGetProperty("lastCommand", out var lc) && lc.ValueKind == JsonValueKind.Object && Str(lc, "id") is { Length: > 0 } cid)
                cmd = new PulseCommandResult(cid, Str(lc, "action") ?? "", Bool(lc, "ok"), Str(lc, "message") ?? "", Long(lc, "at") ?? 0);
            List<PulseGameProfile>? profiles = null;
            if (r.TryGetProperty("gameProfiles", out var gp) && gp.ValueKind == JsonValueKind.Array)
                profiles = gp.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && Str(x, "name") is { Length: > 0 })
                    .Select(x => new PulseGameProfile(Str(x, "name")!, Str(x, "mode") ?? "", !x.TryGetProperty("enabled", out _) || Bool(x, "enabled"))).ToList();
            List<PulseAuditItem>? audit = null;
            if (r.TryGetProperty("audit", out var au) && au.ValueKind == JsonValueKind.Array)
                audit = au.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && Str(x, "title") is { Length: > 0 })
                    .Select(x => new PulseAuditItem(Str(x, "id") ?? "", (int?)Dbl(x, "severity") ?? 0, Str(x, "title")!, Str(x, "detail") ?? "")).ToList();
            return new PulseFeed((int?)Long(r, "schema") ?? 1, Long(r, "updatedAt") ?? 0, Str(r, "pulseVersion"), Bool(r, "running"),
                mode is { } me ? Str(me, "key") : null, mode is { } me2 && Bool(me2, "applying"), Bool(r, "acceptsCommands"), report, Bool(r, "autoGameMode"),
                Str(r, "modeTitle"), modes, live, cmd, (int?)Dbl(r, "heatTarget"), profiles, audit);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Pulse çalışıyor mu? `running` doğru ve özet 20 sn'den eski değil (gelecekteki zaman da güvenilmez).</summary>
    public static bool IsAlive(PulseFeed? f, long? now = null)
    {
        if (f is not { Running: true }) return false;
        var age = (now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()) - f.UpdatedAt;
        return age is >= -300 and <= FreshSeconds;
    }

    /// <summary>
    /// Güç planını Pulse mu yönetecek? Yalnızca Pulse çalışıyorsa VE "oyun açılınca modu kendiliğinden değiştir" ayarı açıksa.
    /// Ayar kapalıysa Pulse oyunda plana dokunmaz; Konsol da çekilirse oyun hiç hızlı plana geçmez (performans kaybı), o yüzden Konsol kendi yönetir.
    /// </summary>
    public static bool PulseManagesPlan(PulseFeed? f, long? now = null) => IsAlive(f, now) && f!.AutoGameMode;

    /// <summary>
    /// Oyun başlatırken "Pulse var mı?" kararı: özet tazeyse hemen evet; özet var ama bayatsa (Pulse çalışıyor olabilir, yeni açıldı)
    /// en çok <paramref name="maxWait"/> bekleyip yeniden bakar. Özet hiç yoksa (Pulse kurulu değil / hiç çalışmadı) beklemeden hayır.
    /// Yalnızca oyun BAŞLARKEN çağrılır; oyun sırasında Konsol'un hiçbir şey yapmaması ilkesi bozulmaz.
    /// </summary>
    public static async Task<bool> PulseIsPresentAsync(TimeSpan maxWait, Func<PulseFeed?>? read = null, Func<long>? now = null, Func<TimeSpan, Task>? delay = null)
    {
        read ??= () => ReadFeed();
        now ??= () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        delay ??= Task.Delay;
        var feed = read();
        if (IsAlive(feed, now())) return true;
        if (feed is not { Running: true }) return false;           // Pulse kapandığını kendisi yazmış ya da hiç yazmamış
        // Özet "çalışıyor" diyor ama çok eski: Pulse kaldırılmış/öldürülmüş (kapanırken running=false yazamadı). Her oyun açılışında boşuna bekletme.
        if (now() - feed.UpdatedAt > RecentSeconds) return false;
        var waited = TimeSpan.Zero; var step = TimeSpan.FromMilliseconds(250);
        while (waited < maxWait)
        {
            await delay(step); waited += step;
            if (IsAlive(read(), now())) return true;
        }
        return false;
    }

    // ---- Oyun raporu eşleştirme --------------------------------------------

    public static string Normalize(string? s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>
    /// Pulse raporu bu oyuna ait mi? Pulse oyunu exe adından türetir ("Hades"); karşılaştırma küçük harf ve harf/rakam dışı atılarak yapılır.
    /// Tam eşleşme yoksa kurulum klasörü yolunda rapor adı geçiyor mu bakılır. Eşleşmezse null (satır gösterilmez).
    /// </summary>
    public static PulseReport? ReportFor(PulseFeed? feed, string title, string? installDir)
    {
        var r = feed?.LastReport;
        if (r is null) return null;
        var rep = Normalize(r.Game);
        if (rep.Length == 0) return null;
        if (Normalize(title) == rep) return r;
        if (!string.IsNullOrEmpty(installDir) && Normalize(installDir).Contains(rep, StringComparison.Ordinal)) return r;
        return null;
    }

    /// <summary>
    /// Rapor şimdi biten oturuma mı ait? Pulse kaydı oyun sürecini görünce başlatır (Steam/Epic'te Konsol'un başlatmasından ~30 sn sonra),
    /// bu yüzden başlangıç Konsol'un hesapladığı oturum başlangıcından en çok 3 dk önce/sonra ve gelecekte olmayan bir zaman olmalı.
    /// Eski kural `StartedAt >= LastPlayed - 60` Steam oyunlarında tutmuyordu: Steam LastPlayed'i oyun BİTİNCE yazar.
    /// </summary>
    public static bool ReportMatchesSession(PulseReport r, long sessionStart, long now) =>
        r.StartedAt >= sessionStart - 180 && r.StartedAt <= now + 60;

    /// <summary>
    /// Pulse'ın bu oyun için tuttuğu profil (oyun ilk oynandığında Pulse oluşturur; adı çoğunlukla exe adıdır, "Hades2"). Eşleşme: ad tam eşleşir ya da
    /// kurulum klasörü yolunda profil adı geçer. Yoksa null — Konsol yeni profil yaratamaz.
    /// </summary>
    public static PulseGameProfile? ProfileFor(PulseFeed? feed, string title, string? installDir)
    {
        if (feed?.GameProfiles is not { Count: > 0 } list) return null;
        var t = Normalize(title);
        var dir = Normalize(installDir);
        return list.FirstOrDefault(p => t.Length > 0 && Normalize(p.Name) == t)
            ?? list.FirstOrDefault(p => dir.Length > 0 && Normalize(p.Name) is { Length: >= 4 } n && dir.Contains(n, StringComparison.Ordinal));
    }

    /// <summary>Pulse'un "Windows ile başlat" ayarının oluşturduğu zamanlanmış görev (kendi adı: KLYC-Pulse).</summary>
    public const string PulseTaskName = "KLYC-Pulse";

    /// <summary>
    /// Pulse çalışmıyorsa, Pulse'un kendi "Windows ile başlat" görevini çalıştırarak açar. Pulse yönetici ister; doğrudan açmak UAC penceresi çıkarırdı ve
    /// kolla onaylanamaz (güvenli masaüstü). Zamanlanmış görev UAC'siz çalışır. Görev yoksa (Pulse'ta "Windows ile başlat" hiç açılmamış) nedenini söyler.
    /// <paramref name="run"/>: schtasks argümanlarını çalıştırıp çıkış kodunu döner (testlerde sahte).
    /// </summary>
    public static (bool Ok, string Message) StartPulse(Func<string[], int>? run = null)
    {
        run ??= RunSchtasks;
        try
        {
            if (run(new[] { "/Query", "/TN", PulseTaskName }) != 0)
                return (false, "Pulse'u buradan açmak için Pulse → Ayarlar → \"Windows ile başlat\" bir kez açık olmalı (kolla UAC onaylanamaz)");
            return run(new[] { "/Run", "/TN", PulseTaskName }) == 0 ? (true, "Pulse başlatılıyor…") : (false, "Pulse başlatılamadı");
        }
        catch { return (false, "Pulse başlatılamadı"); }
    }

    static int RunSchtasks(string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        return p.WaitForExit(8000) ? p.ExitCode : -1;
    }

    /// <summary>Denetim bulgularını okunur metne çevirir (önce dikkat edilecekler). Bulgu yoksa olumlu cümle.</summary>
    public static string AuditText(PulseFeed? feed)
    {
        if (feed?.Audit is not { } list) return Loc.T("Pulse sistem denetimi sonucu henüz gelmedi. Biraz sonra yeniden aç.");
        if (list.Count == 0) return Loc.T("Dikkat edilecek bir şey bulunmadı: sayfa dosyası, ekran kartı belleği, ısı ve arka plan yükü uygun görünüyor.");
        return string.Join("\n\n", list.OrderByDescending(a => a.Severity).Select(a => (a.Severity > 0 ? "• " : "· ") + a.Title + "\n" + a.Detail));
    }

    public static string ModeName(string? key) => key switch
    {
        "oyun" => "Oyun", "gunluk" => "Günlük", "sessiz" => "Sessiz", "bosta" => "Boşta", _ => key ?? "",
    };

    /// <summary>Hakkında sayfasındaki Pulse durumu: çalışıyor (sürüm, mod) / kurulu ama kapalı / bulunamadı.</summary>
    public static string StatusText(PulseFeed? f, long? now = null)
    {
        if (f is null) return "bulunamadı · ernklyc/klyc-pulse";
        if (!IsAlive(f, now)) return "kapalı";
        var mode = ModeName(f.ModeKey);
        return $"çalışıyor · v{f.PulseVersion ?? "?"}" + (mode.Length > 0 ? $" · {mode} modu" : "");
    }

    /// <summary>
    /// Üst çubuktaki Pulse bilgisi: "Oyun modu · İşlemci 61° · Ekran kartı 55°". Pulse çalışmıyorsa null (chip gizlenir).
    /// Isı değerleri yalnızca özetin canlı bölümü tazeyse (≤ 20 sn) gösterilir.
    /// </summary>
    public static string? ChipText(PulseFeed? f, long? now = null)
    {
        if (!IsAlive(f, now)) return null;
        var n = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var title = f!.ModeTitle ?? ModeName(f.ModeKey);
        var text = f.ModeApplying ? "Mod uygulanıyor…" : title.Length > 0 ? $"{title} modu" : "Pulse";
        if (f.Live is { } l && n - l.At is >= -300 and <= FreshSeconds)
        {
            if (l.CpuTemp is { } c) text += $" · İşlemci {c:0}°";
            if (l.GpuTemp is { } g) text += $" · Ekran kartı {g:0}°";
        }
        return text;
    }

    /// <summary>Isı yüksek mi (chip uyarı rengi): işlemci ≥ 90° ya da ekran kartı ≥ 85°.</summary>
    public static bool IsHot(PulseFeed? f) => f?.Live is { } l && (l.CpuTemp >= 90 || l.GpuTemp >= 85);

    /// <summary>Oyun kapanınca Konsol'da gösterilecek kısa bildirim metni.</summary>
    public static string ShortSummary(PulseReport r)
    {
        var parts = new List<string>();
        if (r.AvgFps is { } fps) parts.Add($"ort. {fps:0} FPS");
        parts.Add(r.Findings.Count > 0 ? r.Findings[0] : r.Severity == 0 ? "sorun görülmedi" : "dikkat edilecek şeyler var");
        return string.Join(" · ", parts);
    }

    /// <summary>Oyun detayında gösterilecek tek satır özet.</summary>
    public static string Summarize(PulseReport r, int maxFindings = 2)
    {
        var parts = new List<string> { $"{r.Minutes:0} {Loc.T("dk")}" };
        if (r.AvgFps is { } fps) parts.Add($"ort. {fps:0} FPS");
        if (r.CpuTempMax is { } c) parts.Add($"işlemci en çok {c:0} °C");
        if (r.GpuTempMax is { } g) parts.Add($"ekran kartı {g} °C");
        var line = "Pulse raporu · " + string.Join(" · ", parts);
        return r.Findings.Count > 0 ? line + "\n" + string.Join("\n", r.Findings.Take(maxFindings).Select(f => "• " + f)) : line;
    }

    // ---- Konsol durumunu yazma (şema 2) -------------------------------------

    /// <summary>
    /// `status.json` içeriği. Şema 2: `game` bir nesne (kimlik, ad, mağaza, klasör, hedef, başlangıç). Şema 1 okuyucuları için `gameId` ve `since` üst düzeyde kalır.
    /// <paramref name="state"/>: launcher | launching | playing | background | desktop. <paramref name="powerPlanManagedBy"/>: konsol | pulse | none.
    /// </summary>
    public static string BuildStatus(string state, Game? game, string powerPlanManagedBy, bool guard, bool controllerConnected, int pid, string version, long now, long? since = null, string? installDir = null, bool wantsLive = false)
    {
        object? g = game is null ? null : new
        {
            id = game.Id, title = game.Title, store = StoreName(game.Source), installDir = installDir ?? game.InstallDir, target = game.Target,
            startedAt = since ?? now,
        };
        // wantsLive: Konsol üst çubukta Pulse ısı bilgisini gösteriyor; Pulse yalnızca bu bayrak varken (ve Konsol açıkken) sensör okur
        return JsonSerializer.Serialize(new
        {
            schema = 2, updatedAt = now, version, pid, state, game = g, gameId = game?.Id, since = since ?? now,
            powerPlanManagedBy, guard, controllerConnected, wantsLive,
        }, new JsonSerializerOptions { WriteIndented = false });
    }

    // ---- Pulse'a komut gönderme (yalnızca dar bir izin listesi: mod uygula, Hızlandır) -----------------

    public static string CommandFile => Path.Combine(AppPaths.Dir, "pulse-command.json");

    /// <summary>Komut JSON'u (Pulse KonsolCommands.Parse'ın beklediği biçim): şema, kimlik (8-64 harf/rakam/-), eylem, mod, zaman.</summary>
    public static string BuildCommand(string id, string action, string? mode, long now, string? value = null) =>
        JsonSerializer.Serialize(new { schema = 1, id, action, mode, value, at = now }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

    /// <summary>Komutu yazar ve kimliğini döner (başarısızsa null). Yalnızca Pulse bunu kabul ettiğini bildirmişse çağrılır.</summary>
    public static string? SendCommand(string action, string? mode, long? now = null, string? path = null, string? value = null)
    {
        var id = "k" + Guid.NewGuid().ToString("N")[..15];
        var json = BuildCommand(id, action, mode, now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(), value);
        return WriteAtomic(path ?? CommandFile, json) ? id : null;
    }

    /// <summary>Mağaza adını belgedeki küçük harfli anahtara çevirir (steam, epic, xbox, ea, riot, gog, ubisoft, battlenet, shortcut, custom).</summary>
    public static string StoreName(string? source)
    {
        var s = Normalize(source);
        foreach (var k in new[] { "steam", "epic", "xbox", "ea", "riot", "gog", "ubisoft", "battlenet" })
            if (s.StartsWith(k, StringComparison.Ordinal)) return k;
        return s.Contains("kisayol") || s.Contains("shortcut") ? "shortcut" : "custom";
    }

    /// <summary>Aynı klasörde geçici dosyaya yazıp üzerine taşır: okuyucu (Pulse) yarım dosya görmez. Başarısızlık sessizdir.</summary>
    public static bool WriteAtomic(string path, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    // ---- JSON yardımcıları ------------------------------------------------

    static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static long? Long(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : null;
    static double? Dbl(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : null;
    static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    static List<string> Findings(JsonElement e) =>
        e.TryGetProperty("findings", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new();
}

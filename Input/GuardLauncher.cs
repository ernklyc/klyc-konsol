using System.Diagnostics;
using System.Text.Json;
using KlycKonsol.Library;

namespace KlycKonsol.Input;

/// <summary>Bekçiye (KlycGuard.exe) verilen / ondan alınan oyun oturumu bilgisi.</summary>
public sealed record GuardInfo(string GameId, string Dir, long Started, bool AutoReturn, long FirstSeen);

/// <summary>Bekçi açılış argümanlarından anlaşılan durum.</summary>
public enum GuardStart { None, Resume, GameExited }

public sealed record GuardArgs(GuardStart Kind, string GameId, long PlayedSeconds, long FirstSeen, bool Silent);

/// <summary>
/// Oyun sırasında Konsol tamamen kapanır, yerine çok küçük bir bekçi çalışır (Guide / kısayol / simge ile Konsol'u geri açar,
/// oyun kapanınca "oyun bitti" bilgisiyle açar). Bekçi yoksa (ör. geliştirme derlemesi) eski "arka planda düşük güç" moduna düşülür.
/// </summary>
public static class GuardLauncher
{
    const string MutexName = "KlycKonsol.Guard", StopName = "KlycKonsol.Guard.Stop";

    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "KlycGuard.exe");
    public static bool Available => File.Exists(ExePath);
    static string InfoFile => Path.Combine(AppPaths.Dir, "guard.json");

    public static bool IsRunning()
    {
        try { using var m = Mutex.OpenExisting(MutexName); return true; } catch { return false; }
    }

    /// <summary>Çalışan bekçiye "görevi bıraktım" der (olayı hemen kapatır; nesne bekçi çıkana dek yaşar).</summary>
    public static void StopRunning()
    {
        try { using var e = EventWaitHandle.OpenExisting(StopName); e.Set(); } catch { }
    }

    // ---- Saf ayrıştırıcı (test edilebilir) ------------------------------

    public static GuardArgs ParseArgs(IReadOnlyList<string> args)
    {
        if (!args.Contains("--from-guard")) return new GuardArgs(GuardStart.None, "", 0, 0, false);
        string Val(string key) { var i = args.ToList().IndexOf(key); return i >= 0 && i + 1 < args.Count ? args[i + 1] : ""; }
        long.TryParse(Val("--played"), out var played);
        long.TryParse(Val("--firstseen"), out var first);
        var exited = args.Contains("--game-exited");
        return new GuardArgs(exited ? GuardStart.GameExited : GuardStart.Resume, Val("--game"), played, first, args.Contains("--silent"));
    }

    /// <summary>Bekçiye geçirilecek komut satırı (testlenebilir).</summary>
    public static string BuildArguments(string konsolExe, GuardInfo? info)
    {
        static string Q(string s) => "\"" + s.Replace("\"", "") + "\"";
        var a = $"--konsol {Q(konsolExe)}";
        if (info != null)
        {
            if (info.Dir.Length > 0) a += $" --dir {Q(info.Dir)}";
            if (info.GameId.Length > 0) a += $" --game {Q(info.GameId)}";
            a += $" --started {info.Started} --autoreturn {(info.AutoReturn ? 1 : 0)}";
            if (info.FirstSeen > 0) a += $" --firstseen {info.FirstSeen}";
        }
        return a;
    }

    // ---- Bilgi dosyası: Konsol, bekçi çalışırken başka yoldan açılırsa oturumu devralabilsin ----

    public static void Save(GuardInfo? info)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Dir);
            if (info == null) { if (File.Exists(InfoFile)) File.Delete(InfoFile); }
            else File.WriteAllText(InfoFile, JsonSerializer.Serialize(info));
        }
        catch { }
    }

    public static GuardInfo? Load()
    {
        try { return File.Exists(InfoFile) ? JsonSerializer.Deserialize<GuardInfo>(File.ReadAllText(InfoFile)) : null; }
        catch { return null; }
    }

    /// <summary>Bekçiyi başlatır; çalıştığı doğrulanınca true.</summary>
    public static bool Start(GuardInfo? info)
    {
        if (!Available) return false;
        try
        {
            Save(info);
            var exe = Environment.ProcessPath!;
            Process.Start(new ProcessStartInfo(ExePath, BuildArguments(exe, info)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory });
            for (var i = 0; i < 40; i++)
            {
                Thread.Sleep(50);
                if (IsRunning()) return true;
            }
        }
        catch { }
        return false;
    }
}

/// <summary>Oyun bitince yapılacak ortak işler (Konsol açıkken de, sessiz bitirirken de aynı).</summary>
public static class SessionFinisher
{
    /// <summary>Steam dışı oyunların oynama süresini ekler (Steam oyunları için Steam'in kendi kaydı geçerlidir).</summary>
    public static long RecordPlay(AppState state, string gameId, long playedSeconds)
    {
        if (playedSeconds < 30 || gameId.Length == 0 || gameId.StartsWith("steam:")) return 0;
        state.PlaySeconds[gameId] = state.PlaySeconds.GetValueOrDefault(gameId) + playedSeconds;
        state.Save();
        return state.PlaySeconds[gameId];
    }
}

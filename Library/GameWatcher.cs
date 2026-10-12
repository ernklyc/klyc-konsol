using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KlycKonsol.Library;

/// <summary>
/// Başlatılan oyunun süreçlerini kurulum klasörüne bakarak bulur ve hepsi kapanınca <see cref="Exited"/> olayını verir
/// (Konsol'un oyun kapanınca otomatik öne gelmesi için).
/// Açılışta (oyun/başlatıcı süreçleri belirene kadar) 2 sn'de bir tarar; süreçler bulunup yeni süreç gelmeyince
/// olay tabanlı bekler: oyun sürerken yoklama ve işlemci kullanımı yoktur.
/// </summary>
public sealed class GameWatcher : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    const uint QueryLimited = 0x1000;

    static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(180);   // oyun bu sürede hiç görünmezse vazgeç
    static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(25);     // yeni süreç gelmeyince olay tabanlı beklemeye geç
    static readonly TimeSpan Grace = TimeSpan.FromSeconds(6);           // hepsi kapandıktan sonra yeni süreç var mı diye bekle
    static readonly TimeSpan LaunchGrace = TimeSpan.FromSeconds(20);    // oyun yeni açılırken başlatıcı süreç kapanıp asıl oyun gecikmeli gelebilir
    static readonly TimeSpan LaunchPhase = TimeSpan.FromSeconds(120);

    readonly string _dir;
    readonly CancellationTokenSource _cts = new();
    readonly Dictionary<int, Process> _tracked = new();

    public event Action? Exited;

    /// <summary>Oyun süreçlerinin ilk görüldüğü an (oynama süresi başlangıcı).</summary>
    public DateTime? FirstSeenUtc { get; private set; }
    /// <summary>Süreçlerden en az birinin hâlâ çalıştığı son an (oynama süresi bitişi).</summary>
    public DateTime LastAliveUtc { get; private set; }
    public TimeSpan PlayTime => FirstSeenUtc == null ? TimeSpan.Zero : LastAliveUtc - FirstSeenUtc.Value;

    readonly DateTime _startedUtc;

    /// <param name="firstSeenUtc">Bekçiden devralınan oturumda oyunun ilk görüldüğü an.</param>
    /// <param name="startedUtc">Oyunun başlatıldığı an (açılış penceresi bundan ölçülür).</param>
    public GameWatcher(string installDir, DateTime? firstSeenUtc = null, DateTime? startedUtc = null)
    {
        _dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir)) + Path.DirectorySeparatorChar;
        _startedUtc = startedUtc ?? DateTime.UtcNow;
        if (firstSeenUtc != null) { FirstSeenUtc = firstSeenUtc; LastAliveUtc = DateTime.UtcNow; }
    }

    public void Start() => _ = Task.Run(LoopAsync);

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var p in _tracked.Values) { try { p.Dispose(); } catch { } }
    }

    static string? ImagePath(int pid)
    {
        var h = OpenProcess(QueryLimited, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Kurulum klasöründen çalışan yeni süreçleri izlemeye alır; yeni bulunan sayısını döndürür.</summary>
    int Scan()
    {
        var added = 0;
        foreach (var p in Process.GetProcesses())
        {
            var keep = false;
            try
            {
                if (_tracked.ContainsKey(p.Id)) continue;
                var path = ImagePath(p.Id);
                if (path != null && path.StartsWith(_dir, StringComparison.OrdinalIgnoreCase))
                {
                    _tracked[p.Id] = p;
                    keep = true;
                    added++;
                }
            }
            catch { }
            finally { if (!keep) p.Dispose(); }
        }
        return added;
    }

    static TimeSpan GraceNow(DateTime started) => DateTime.UtcNow - started < LaunchPhase ? LaunchGrace : Grace;

    int Alive() => _tracked.Values.Count(p => { try { return !p.HasExited; } catch { return false; } });

    async Task LoopAsync()
    {
        var ct = _cts.Token;
        var started = _startedUtc;
        var lastNew = DateTime.MinValue;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (Scan() > 0) { lastNew = DateTime.UtcNow; FirstSeenUtc ??= lastNew; }
                var found = _tracked.Count > 0;
                if (found && Alive() > 0) LastAliveUtc = DateTime.UtcNow;

                if (!found && DateTime.UtcNow - started > FindTimeout) return;

                // Açılış penceresinde bile: izlenen her şey kapandı ve bir süredir yeni süreç yok → oyun bitti.
                if (found && Alive() == 0 && DateTime.UtcNow - lastNew > GraceNow(started)) { Exited?.Invoke(); return; }

                if (found && DateTime.UtcNow - lastNew > SettleTime)
                {
                    // Olay tabanlı bekleme: tüm izlenen süreçler kapanana dek hiçbir şey çalışmaz.
                    var waits = _tracked.Values.Select(p => { try { return p.WaitForExitAsync(ct); } catch { return Task.CompletedTask; } }).ToArray();
                    try { await Task.WhenAll(waits); } catch (OperationCanceledException) { return; } catch { }
                    LastAliveUtc = DateTime.UtcNow;

                    await Task.Delay(GraceNow(started), ct);
                    if (Scan() == 0 && Alive() == 0) { Exited?.Invoke(); return; }
                    lastNew = DateTime.UtcNow;
                    continue;
                }
                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }
}

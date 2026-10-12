using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using KlycKonsol.Library;

namespace KlycKonsol.Ui;

/// <summary>
/// Kare süresi ölçer. Açıkken her kare arasındaki süreyi toplar; "kasma" yüzdesi = 25 ms'yi aşan kareler.
/// Sonuçlar %AppData%\KLYC-Konsol\perf.log dosyasına yazılır. Açma: --bench ya da --perf argümanı, F3.
/// </summary>
public static class PerfMeter
{
    static readonly List<double> Frames = new();
    static readonly StringBuilder Log = new();
    static long _last;
    static string _phase = "";
    static bool _hooked;
    static TimeSpan _cpuStart;
    static long _wallStart;

    public static bool Enabled { get; private set; }
    public static string LogPath => Path.Combine(AppPaths.Dir, "perf.log");

    public static void Enable()
    {
        if (Enabled) return;
        Enabled = true;
        Directory.CreateDirectory(AppPaths.Dir);
        Line($"=== {DateTime.Now:s} · Render Tier {System.Windows.Media.RenderCapability.Tier >> 16} · Sürüm {(IsDebug ? "Debug" : "Release")}");
        if (!_hooked) { CompositionTarget.Rendering += OnRender; _hooked = true; }
    }

    static bool IsDebug =>
#if DEBUG
        true;
#else
        false;
#endif

    static void OnRender(object? s, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        if (_last != 0 && _phase != "")
        {
            var ms = (now - _last) * 1000.0 / Stopwatch.Frequency;
            if (ms < 500) Frames.Add(ms);   // 500 ms üstü = sahnede animasyon yok (boşta), sayma
        }
        _last = now;
    }

    public static void Begin(string phase)
    {
        End();
        _phase = phase;
        Frames.Clear();
        _last = 0;
        _cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        _wallStart = Stopwatch.GetTimestamp();
    }

    public static void End()
    {
        if (_phase == "") return;
        if (Frames.Count > 5)
        {
            var sorted = Frames.OrderBy(x => x).ToList();
            var avg = Frames.Average();
            var p95 = sorted[(int)(sorted.Count * 0.95)];
            var max = sorted[^1];
            var slow = Frames.Count(x => x > 25) * 100.0 / Frames.Count;
            var cpu = (Process.GetCurrentProcess().TotalProcessorTime - _cpuStart).TotalSeconds / ((Stopwatch.GetTimestamp() - _wallStart) / (double)Stopwatch.Frequency) * 100;
            Line($"{_phase,-28} cpu=%{cpu,3:F0} kare={Frames.Count,4}  ort={avg,5:F1} ms ({1000 / avg,4:F0} fps)  p95={p95,5:F1} ms  en kötü={max,5:F1} ms  kasan kare=%{slow:F0}");
        }
        else Line($"{_phase,-28} (yeterli kare yok: {Frames.Count})");
        _phase = "";
    }

    public static void Note(string s) => Line("   · " + s);

    static void Line(string s)
    {
        Log.AppendLine(s);
        try { File.AppendAllText(LogPath, s + Environment.NewLine); } catch { }
    }
}

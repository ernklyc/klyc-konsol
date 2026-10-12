using System.Media;

namespace KlycKonsol.Input;

public enum Cue { Move, Select, Back, Tab, Error }

/// <summary>
/// Arayüz sesleri: dışarıdan dosya yok, kısa sinüs tonları bellekte üretilir. Yalnızca Konsol açıkken çalınır;
/// oyun sırasında hiçbir şey yapılmaz (zamanlayıcı / arka plan iş parçacığı yok).
/// </summary>
public static class UiSound
{
    const int Rate = 22050;
    public static bool Enabled { get; set; } = true;

    static readonly Dictionary<Cue, SoundPlayer> Players = new();
    static DateTime _last = DateTime.MinValue;

    /// <summary>Kısa, yumuşak (fade in/out) ton dizisi → 16 bit mono WAV.</summary>
    public static byte[] Wav(params (double Hz, int Ms)[] tones) => Wav(0.28, tones);

    public static byte[] Wav(double volume, params (double Hz, int Ms)[] tones)
    {
        var samples = new List<short>();
        foreach (var (hz, ms) in tones)
        {
            var n = Rate * ms / 1000;
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)Rate;
                var env = Math.Min(1.0, Math.Min(i / (Rate * 0.004), (n - i) / (Rate * 0.012)));   // 4 ms açılış, 12 ms sönüm: tıkırtı yok
                samples.Add((short)(Math.Sin(2 * Math.PI * hz * t) * env * volume * short.MaxValue));
            }
        }
        using var ms2 = new MemoryStream();
        using var w = new BinaryWriter(ms2);
        var dataLen = samples.Count * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms2.ToArray();
    }

    static readonly object Gate = new();

    /// <summary>Sesleri arka planda hazırla: ilk tuşta arayüz iş parçacığı WAV üretip yüklemesin (~25 ms takılma).</summary>
    public static void Warm() => Task.Run(() => { foreach (var c in Enum.GetValues<Cue>()) { try { Get(c); } catch { } } });

    static SoundPlayer Get(Cue cue)
    {
        lock (Gate) return GetLocked(cue);
    }

    static SoundPlayer GetLocked(Cue cue)
    {
        if (Players.TryGetValue(cue, out var p)) return p;
        var wav = cue switch
        {
            Cue.Move => Wav(0.18, (1250, 22)),
            Cue.Select => Wav(0.26, (880, 40), (1320, 70)),
            Cue.Back => Wav(0.22, (700, 40), (500, 60)),
            Cue.Tab => Wav(0.22, (990, 30), (1180, 40)),
            _ => Wav(0.24, (330, 90), (260, 110)),
        };
        p = new SoundPlayer(new MemoryStream(wav));
        p.Load();
        return Players[cue] = p;
    }

    public static void Play(Cue cue)
    {
        if (!Enabled) return;
        var now = DateTime.UtcNow;
        if (cue == Cue.Move && (now - _last).TotalMilliseconds < 70) return;   // tuşa basılı tutunca vızıldamasın
        _last = now;
        try { Get(cue).Play(); } catch { }
    }
}

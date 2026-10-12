namespace KlycKonsol.Input;

/// <summary>
/// Pulse'a gönderilen son komutun sonucunu izler: Pulse sonucu özetteki `lastCommand` içinde kimlikle bildirir.
/// Saf mantık (zaman dışarıdan verilir): bekleyen komut yoksa hiçbir şey yapmaz; yanıt gelmezse zaman aşımında dürüstçe "yanıt yok" der.
/// </summary>
public sealed class PulseCommandTracker
{
    public const int TimeoutSeconds = 25;

    string? _id;
    long _sentAt;

    public bool Pending => _id != null;

    public void Track(string id, long now) { _id = id; _sentAt = now; }

    /// <summary>Sonuç geldiyse (ya da süre dolduysa) gösterilecek ileti; hâlâ bekleniyorsa null.</summary>
    public (string Message, bool Ok)? Poll(PulseFeed? feed, long now)
    {
        if (_id == null) return null;
        if (feed?.LastCommand is { } c && c.Id == _id) { _id = null; return (c.Message, c.Ok); }
        if (now - _sentAt > TimeoutSeconds)
        {
            _id = null;
            return ("Pulse yanıt vermedi (Pulse açık mı ve Konsol'dan komuta izin verildi mi?)", false);
        }
        return null;
    }
}

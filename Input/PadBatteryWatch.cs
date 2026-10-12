namespace KlycKonsol.Input;

/// <summary>
/// Kablosuz kolun pili azalınca bir kez uyarır ("Düşük" → uyarı, "Boş" → ikinci, daha acil uyarı); pil yükselince ya da kol çıkınca sıfırlanır.
/// Saf mantık: Konsol ekrandayken NavBar her ~10 sn'de kol bilgisini buraya verir (oyun sırasında Konsol zaten kapalıdır).
/// </summary>
public sealed class PadBatteryWatch
{
    readonly Dictionary<int, int> _notified = new();   // yuva → en son uyarılan şiddet (1 düşük, 2 boş)

    static int Severity(PadInfo i) => !i.Connected || !i.Wireless ? 0 : i.Battery switch { "Boş" => 2, "Düşük" => 1, _ => 0 };

    /// <summary>Yeni uyarılar: (yuva, şiddet). Aynı seviye için tekrar uyarmaz.</summary>
    public List<(int Slot, int Severity)> Update(IEnumerable<PadInfo> infos)
    {
        var result = new List<(int, int)>();
        var seen = new HashSet<int>();
        foreach (var i in infos)
        {
            seen.Add(i.Slot);
            var sev = Severity(i);
            var last = _notified.TryGetValue(i.Slot, out var l) ? l : 0;
            if (sev > last) { _notified[i.Slot] = sev; result.Add((i.Slot, sev)); }
            else if (sev < last) { if (sev == 0) _notified.Remove(i.Slot); else _notified[i.Slot] = sev; }
        }
        foreach (var slot in _notified.Keys.Where(s => !seen.Contains(s)).ToList()) _notified.Remove(slot);
        return result;
    }

    public static string Message(int slot, int severity) => severity >= 2
        ? $"Controller {slot + 1} pili bitmek üzere — hemen şarj et"
        : $"Controller {slot + 1} pili azaldı — şarj etmeyi unutma";
}

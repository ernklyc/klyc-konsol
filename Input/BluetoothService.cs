using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace KlycKonsol.Input;

public sealed record BtDevice(string Id, string Name, bool Paired, bool Connected, bool CanPair, int? Signal)
{
    public DeviceInformation? Info { get; init; }
}

/// <summary>Eşleştirme sonucu: ekranda gösterilecek metin; PIN gösteriliyorsa Pin dolu.</summary>
public sealed record PairOutcome(bool Ok, string Message);

/// <summary>
/// Bluetooth (klasik + LE): açma/kapama, tarama, eşleştirme, eşlemeyi kaldırma — Windows'un kendi eşleştirme API'leriyle.
/// PIN gerektirmeyen cihazlar (kol, kulaklık, hoparlör, fare) ve "onayla / PIN eşleşti mi" soran cihazlar uygulama içinde eşleşir;
/// PIN yazdıran cihazlar (klavye vb.) için PIN ekrana yazılır. Bağlanma: eşleşmiş cihaz açılınca Windows kendiliğinden bağlanır.
/// </summary>
public static class BluetoothService
{
    const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    const string LeProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    static readonly string[] Props =
    {
        "System.Devices.Aep.IsPaired", "System.Devices.Aep.IsConnected", "System.Devices.Aep.CanPair", "System.Devices.Aep.SignalStrength",
    };

    // ---- Saf yardımcılar (test edilebilir) ------------------------------

    public static string StatusText(DevicePairingResultStatus s) => s switch
    {
        DevicePairingResultStatus.Paired => "Eşleşti",
        DevicePairingResultStatus.AlreadyPaired => "Zaten eşleşmiş",
        DevicePairingResultStatus.Failed => "Eşleşme başarısız (cihaz eşleşme modunda mı?)",
        DevicePairingResultStatus.AuthenticationFailure => "Kimlik doğrulama başarısız (PIN yanlış olabilir)",
        DevicePairingResultStatus.AuthenticationTimeout => "Cihaz zamanında yanıt vermedi",
        DevicePairingResultStatus.AuthenticationNotAllowed => "Cihaz eşleşmeye izin vermedi",
        DevicePairingResultStatus.ConnectionRejected => "Cihaz bağlantıyı reddetti",
        DevicePairingResultStatus.TooManyConnections => "Cihazın bağlantı sınırı dolu",
        DevicePairingResultStatus.RejectedByHandler => "Eşleşme reddedildi",
        DevicePairingResultStatus.OperationAlreadyInProgress => "Başka bir eşleştirme sürüyor",
        DevicePairingResultStatus.RequiredHandlerNotRegistered => "Bu cihaz özel bir eşleştirme yöntemi istiyor — Windows ayarlarını kullan",
        DevicePairingResultStatus.PairingCanceled => "Eşleşme iptal edildi",
        DevicePairingResultStatus.AccessDenied => "Erişim reddedildi",
        _ => $"Eşleşme tamamlanamadı ({s})",
    };

    /// <summary>Adı olmayan cihazlar (kimlikten başka bilgisi yok) listede gösterilmez; kalanlar: bağlı, eşleşmiş, sinyal sırasına göre.</summary>
    public static List<BtDevice> Order(IEnumerable<BtDevice> devices) =>
        devices.Where(d => !string.IsNullOrWhiteSpace(d.Name) && d.Name != d.Id)
            .GroupBy(d => d.Id).Select(g => g.First())
            .OrderByDescending(d => d.Connected).ThenByDescending(d => d.Paired).ThenByDescending(d => d.Signal ?? -200).ThenBy(d => d.Name)
            .ToList();

    // ---- Radyo ----------------------------------------------------------

    static async Task<Radio?> GetRadioAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return null;
            return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        }
        catch { return null; }
    }

    /// <summary>null = bağdaştırıcı yok / erişilemiyor.</summary>
    public static async Task<bool?> IsOnAsync()
    {
        var r = await GetRadioAsync();
        return r == null ? null : r.State == RadioState.On;
    }

    public static async Task<bool> SetOnAsync(bool on)
    {
        var r = await GetRadioAsync();
        if (r == null) return false;
        return await r.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
    }

    // ---- Tarama ---------------------------------------------------------

    /// <summary>Eşleşmiş + yakındaki cihazlar (klasik ve LE). Tarama birkaç saniye sürer.</summary>
    public static async Task<List<BtDevice>> ScanAsync()
    {
        var aqs = $"(System.Devices.Aep.ProtocolId:=\"{ClassicProtocol}\" OR System.Devices.Aep.ProtocolId:=\"{LeProtocol}\")";
        var found = await DeviceInformation.FindAllAsync(aqs, Props, DeviceInformationKind.AssociationEndpoint);
        return Order(found.Select(Map));
    }

    static BtDevice Map(DeviceInformation d)
    {
        bool B(string k) => d.Properties.TryGetValue(k, out var v) && v is bool b && b;
        int? sig = d.Properties.TryGetValue("System.Devices.Aep.SignalStrength", out var s) && s is int i ? i : null;
        return new BtDevice(d.Id, d.Name, d.Pairing.IsPaired, B("System.Devices.Aep.IsConnected"), d.Pairing.CanPair, sig) { Info = d };
    }

    // ---- Eşleştirme -----------------------------------------------------

    /// <param name="onPin">PIN ekranda gösterilecekse çağrılır (ör. klavye).</param>
    public static async Task<PairOutcome> PairAsync(BtDevice dev, Action<string> onPin)
    {
        if (dev.Info == null) return new PairOutcome(false, "Cihaz bilgisi yok");
        try
        {
            var custom = dev.Info.Pairing.Custom;
            void Handler(DeviceInformationCustomPairing _, DevicePairingRequestedEventArgs a)
            {
                switch (a.PairingKind)
                {
                    case DevicePairingKinds.ConfirmOnly:
                    case DevicePairingKinds.ConfirmPinMatch:
                        a.Accept(); break;
                    case DevicePairingKinds.DisplayPin:
                        onPin(a.Pin); a.Accept(); break;
                    case DevicePairingKinds.ProvidePin:
                        a.Accept("0000"); break;   // eski kulaklık / hoparlörlerin varsayılanı
                    default:
                        a.Accept(); break;
                }
            }
            custom.PairingRequested += Handler;
            try
            {
                var kinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin;
                var r = await custom.PairAsync(kinds, DevicePairingProtectionLevel.Default);
                var ok = r.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired;
                return new PairOutcome(ok, StatusText(r.Status));
            }
            finally { custom.PairingRequested -= Handler; }
        }
        catch (Exception ex) { return new PairOutcome(false, "Eşleştirilemedi: " + ex.Message); }
    }

    public static async Task<PairOutcome> UnpairAsync(BtDevice dev)
    {
        if (dev.Info == null) return new PairOutcome(false, "Cihaz bilgisi yok");
        try
        {
            var r = await dev.Info.Pairing.UnpairAsync();
            var ok = r.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired;
            return new PairOutcome(ok, ok ? "Eşleme kaldırıldı" : $"Kaldırılamadı ({r.Status})");
        }
        catch (Exception ex) { return new PairOutcome(false, "Kaldırılamadı: " + ex.Message); }
    }
}

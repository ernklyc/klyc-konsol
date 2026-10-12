using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace KlycKonsol.Input;

public sealed record WifiNetwork(string Ssid, int Signal, string Auth, bool Known, bool Connected)
{
    public bool IsOpen => WifiService.IsOpen(Auth);
}

/// <summary>
/// Wi‑Fi yönetimi (netsh wlan; yönetici izni gerekmez). Ayrıştırıcılar dilden bağımsız çalışır: alan adlarına değil
/// "SSID n :" kalıbına, yüzde değerine ve kimlik doğrulama değerinin (WPA2-Personal / Open …) biçimine bakar.
/// </summary>
public static class WifiService
{
    static readonly Regex SsidLine = new(@"^\s*SSID\s+\d+\s*:\s*(.*?)\s*$");
    static readonly Regex AuthValue = new(@":\s*((?:Open|A[cç][ıi]k|OWE|WEP|Shared|WPA\d?)[\w\- ]*?)\s*$", RegexOptions.IgnoreCase);
    static readonly Regex SignalLine = new(@":\s*(\d+)\s*%\s*$");

    // ---- Saf ayrıştırıcılar (test edilebilir) ---------------------------

    /// <summary>"netsh wlan show networks mode=bssid" çıktısı → ağ başına en güçlü sinyal. Gizli (adsız) ağlar atlanır.</summary>
    public static List<(string Ssid, int Signal, string Auth)> ParseNetworks(string output)
    {
        var list = new List<(string, int, string)>();
        string? ssid = null, auth = null;
        var best = 0;

        void Flush()
        {
            if (!string.IsNullOrEmpty(ssid) && list.All(x => x.Item1 != ssid)) list.Add((ssid, best, auth ?? "?"));
            ssid = null; auth = null; best = 0;
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var m = SsidLine.Match(line);
            if (m.Success) { Flush(); ssid = m.Groups[1].Value; continue; }
            if (ssid == null) continue;
            if (auth == null && AuthValue.Match(line) is { Success: true } a) auth = a.Groups[1].Value.Trim();
            else if (SignalLine.Match(line) is { Success: true } s) best = Math.Max(best, int.Parse(s.Groups[1].Value));
        }
        Flush();
        return list;
    }

    /// <summary>"netsh wlan show profiles" → kayıtlı profil adları.</summary>
    public static List<string> ParseProfiles(string output) =>
        Regex.Matches(output, @"^[ \t]*[^:\r\n]*Profil[^:\r\n]*:[ \t]*(\S.*?)[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .Where(s => s.Length > 0 && !s.StartsWith('<'))
            .Distinct().ToList();

    /// <summary>"netsh wlan show interfaces" → bağlı olunan ağ adı (bağlı değilse null). "BSSID" satırı eşleşmez.</summary>
    public static string? ParseConnectedSsid(string output)
    {
        var m = Regex.Match(output, @"^[ \t]*SSID[ \t]*:[ \t]*(\S.*?)[ \t]*\r?$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static bool IsOpen(string auth) => Regex.IsMatch(auth, @"^(open|a[cç][ıi]k)", RegexOptions.IgnoreCase);

    /// <summary>Şifreli ağ için profil XML'i; desteklenmeyen (kurumsal/WEP) ağlarda null.</summary>
    public static string? BuildProfileXml(string ssid, string auth, string password)
    {
        string authentication, encryption;
        var a = auth.ToUpperInvariant();
        if (IsOpen(auth)) { authentication = "open"; encryption = "none"; }
        else if (a.Contains("ENTERPRISE") || a.Contains("WEP") || a.Contains("SHARED")) return null;
        else if (a.StartsWith("WPA3")) { authentication = "WPA3SAE"; encryption = "AES"; }
        else if (a.StartsWith("WPA2")) { authentication = "WPA2PSK"; encryption = "AES"; }
        else if (a.StartsWith("WPA")) { authentication = "WPAPSK"; encryption = "TKIP"; }
        else return null;

        var name = SecurityElement.Escape(ssid);
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
        sb.Append($"<name>{name}</name><SSIDConfig><SSID><name>{name}</name></SSID></SSIDConfig>");
        sb.Append("<connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security>");
        sb.Append($"<authEncryption><authentication>{authentication}</authentication><encryption>{encryption}</encryption><useOneX>false</useOneX></authEncryption>");
        if (authentication != "open")
            sb.Append($"<sharedKey><keyType>passphrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password)}</keyMaterial></sharedKey>");
        sb.Append("</security></MSM></WLANProfile>");
        return sb.ToString();
    }

    // ---- netsh ----------------------------------------------------------

    static async Task<string> NetshAsync(string args)
    {
        var psi = new ProcessStartInfo("netsh.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var read = p.StandardOutput.ReadToEndAsync();
        var done = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(12)));
        if (done != read) { try { p.Kill(); } catch { } return ""; }
        return await read;
    }

    /// <summary>Wi‑Fi bağdaştırıcısı var mı? (Masaüstünde / kablolu bilgisayarda yok.)</summary>
    public static async Task<bool> HasAdapterAsync()
    {
        try { return Regex.IsMatch(await NetshAsync("wlan show interfaces"), @"^\s*(Name|Ad)\s*:", RegexOptions.Multiline | RegexOptions.IgnoreCase); }
        catch { return false; }
    }

    public static async Task<List<WifiNetwork>> ScanAsync()
    {
        var connected = ParseConnectedSsid(await NetshAsync("wlan show interfaces"));
        var known = ParseProfiles(await NetshAsync("wlan show profiles")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nets = ParseNetworks(await NetshAsync("wlan show networks mode=bssid"));
        return nets
            .Select(n => new WifiNetwork(n.Ssid, n.Signal, n.Auth, known.Contains(n.Ssid), string.Equals(n.Ssid, connected, StringComparison.Ordinal)))
            .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Known).ThenByDescending(n => n.Signal)
            .ToList();
    }

    /// <summary>Kayıtlı ya da açık ağa bağlanır; şifreli yeni ağ için önce <see cref="AddProfileAsync"/> çağrılmalı.</summary>
    public static async Task<bool> ConnectAsync(string ssid)
    {
        await NetshAsync($"wlan connect name=\"{ssid}\" ssid=\"{ssid}\"");
        // İstek döner dönmez bağlanmış olmayabilir: birkaç saniye bekleyip doğrula.
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(800);
            if (ParseConnectedSsid(await NetshAsync("wlan show interfaces")) == ssid) return true;
        }
        return false;
    }

    public static async Task<bool> AddProfileAsync(WifiNetwork net, string password)
    {
        var xml = BuildProfileXml(net.Ssid, net.Auth, password);
        if (xml == null) return false;
        var file = Path.Combine(Path.GetTempPath(), $"klyc-wifi-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(file, xml, new UTF8Encoding(false));
            await NetshAsync($"wlan add profile filename=\"{file}\" user=current");
            return ParseProfiles(await NetshAsync("wlan show profiles")).Contains(net.Ssid, StringComparer.OrdinalIgnoreCase);
        }
        finally { try { File.Delete(file); } catch { } }   // şifre diskte kalmasın
    }

    public static Task DisconnectAsync() => NetshAsync("wlan disconnect");
    public static Task ForgetAsync(string ssid) => NetshAsync($"wlan delete profile name=\"{ssid}\"");
}

using KlycKonsol.Input;
using Xunit;

namespace KlycKonsol.Tests;

public class WifiTests
{
    // Gerçek "netsh wlan show networks mode=bssid" biçimi (ağ adları/MAC'ler uydurma)
    const string Networks = """
        Interface name : Wi-Fi
        There are 4 networks currently visible.

        SSID 1 : Ev Agi
            Network type            : Infrastructure
            Authentication          : WPA2-Personal
            Encryption              : CCMP
            BSSID 1                 : 00:11:22:33:44:55
                 Signal             : 82%
                 Radio type         : 802.11ax
                 Band               : 2.4 GHz
                 Channel            : 8
                 Bss Load:
                     Connected Stations:         0
                     Channel Utilization:        74 (29 %)
            BSSID 2                 : 00:11:22:33:44:66
                 Signal             : 91%
                 Radio type         : 802.11ax
                 Band               : 5 GHz

        SSID 2 :
            Network type            : Infrastructure
            Authentication          : WPA2-Personal
            Encryption              : CCMP
            BSSID 1                 : 00:11:22:33:44:77
                 Signal             : 5%

        SSID 3 : Kafe WiFi
            Network type            : Infrastructure
            Authentication          : Open
            Encryption              : None
            BSSID 1                 : 00:11:22:33:44:88
                 Signal             : 40%

        SSID 4 : Yeni Nesil
            Network type            : Infrastructure
            Authentication          : WPA3-Personal
            Encryption              : CCMP
            BSSID 1                 : 00:11:22:33:44:99
                 Signal             : 63%
        """;

    const string TurkishNetworks = """
        SSID 1 : Komsu
            Ağ türü                 : Altyapı
            Kimlik Doğrulama        : WPA2-Personal
            Şifreleme               : CCMP
            BSSID 1                 : 00:11:22:33:44:55
                 Sinyal             : 70%
        SSID 2 : Acik Ag
            Ağ türü                 : Altyapı
            Kimlik Doğrulama        : Açık
            Şifreleme               : Yok
            BSSID 1                 : 00:11:22:33:44:56
                 Sinyal             : 33%
        """;

    [Fact]
    public void ParseNetworks_TakesStrongestSignalPerSsid_AndSkipsHiddenNetworks()
    {
        var nets = WifiService.ParseNetworks(Networks);
        Assert.Equal(3, nets.Count);
        Assert.Equal(("Ev Agi", 91, "WPA2-Personal"), nets[0]);   // 82 ve 91 → 91; "(29 %)" sinyal sayılmaz
        Assert.Equal(("Kafe WiFi", 40, "Open"), nets[1]);
        Assert.Equal(("Yeni Nesil", 63, "WPA3-Personal"), nets[2]);
    }

    [Fact]
    public void ParseNetworks_WorksWithLocalizedFieldNames()
    {
        var nets = WifiService.ParseNetworks(TurkishNetworks);
        Assert.Equal(2, nets.Count);
        Assert.Equal(("Komsu", 70, "WPA2-Personal"), nets[0]);
        Assert.True(WifiService.IsOpen(nets[1].Auth));
        Assert.Equal(33, nets[1].Signal);
    }

    [Fact]
    public void ParseProfiles_ReadsUserProfilesOnly()
    {
        const string output = """
            Profiles on interface Wi-Fi:

            Group policy profiles (read only)
            ---------------------------------
                <None>

            User profiles
            -------------
                All User Profile     : Ev Agi
                All User Profile     : Kafe WiFi
            """;
        Assert.Equal(new[] { "Ev Agi", "Kafe WiFi" }, WifiService.ParseProfiles(output));
        Assert.Equal(new[] { "Okul" }, WifiService.ParseProfiles("    Tüm Kullanıcı Profili : Okul"));
    }

    [Fact]
    public void ParseConnectedSsid_IgnoresBssidAndDisconnectedState()
    {
        const string connected = """
            Name                   : Wi-Fi
            State                  : connected
            SSID                   : Ev Agi
            BSSID                  : 00:11:22:33:44:55
            """;
        Assert.Equal("Ev Agi", WifiService.ParseConnectedSsid(connected));
        Assert.Null(WifiService.ParseConnectedSsid("    Name : Wi-Fi\n    State : disconnected\n"));
    }

    [Theory]
    [InlineData("WPA2-Personal", "WPA2PSK", "AES")]
    [InlineData("WPA3-Personal", "WPA3SAE", "AES")]
    [InlineData("WPA-Personal", "WPAPSK", "TKIP")]
    public void BuildProfileXml_MapsAuthentication(string auth, string expectedAuth, string expectedEnc)
    {
        var xml = WifiService.BuildProfileXml("Ev Agi", auth, "sifre123")!;
        Assert.Contains($"<authentication>{expectedAuth}</authentication>", xml);
        Assert.Contains($"<encryption>{expectedEnc}</encryption>", xml);
        Assert.Contains("<keyMaterial>sifre123</keyMaterial>", xml);
        System.Xml.Linq.XDocument.Parse(xml);   // geçerli XML
    }

    [Fact]
    public void BuildProfileXml_EscapesSpecialCharacters_AndRejectsEnterprise()
    {
        var xml = WifiService.BuildProfileXml("A&B <ağ>", "WPA2-Personal", "p&ss\"<>")!;
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        Assert.Contains("A&B <ağ>", doc.Descendants().First(e => e.Name.LocalName == "name").Value);
        Assert.Contains("p&ss\"<>", xml.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\""));
        Assert.Null(WifiService.BuildProfileXml("Kurum", "WPA2-Enterprise", "x"));
        Assert.DoesNotContain("keyMaterial", WifiService.BuildProfileXml("Kafe", "Open", "")!);
    }
}

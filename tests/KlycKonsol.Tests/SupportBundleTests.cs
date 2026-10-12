using System.IO.Compression;
using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

public class SupportBundleTests
{
    [Fact]
    public void Sanitize_ReplacesProfilePathUserAndMachine()
    {
        var text = @"Hata: C:\Users\demouser\AppData\Roaming\KLYC-Konsol\state.json bulunamadı. Kullanıcı DEMOUSER, makine DEMOPC.";
        var clean = SupportBundle.Sanitize(text, "demouser", "DEMOPC", @"C:\Users\demouser");
        Assert.DoesNotContain("demouser", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Users\<kullanici>\AppData\Roaming\KLYC-Konsol\state.json", clean);
        Assert.Contains("<bilgisayar>", clean);
    }

    [Fact]
    public void Sanitize_OtherUserProfilePathsAreGenericToo()
    {
        var clean = SupportBundle.Sanitize(@"D:\Users\baskasi\x e:\users\digeri\y", "demouser", "PC1", @"C:\Users\demouser");
        Assert.DoesNotContain("baskasi", clean);
        Assert.DoesNotContain("digeri", clean);
    }

    [Fact]
    public void Sanitize_DoesNotMangleWordsThatMerelyContainTheName()
    {
        // Kısa ya da başka sözcüğün içindeki eşleşme bozulmasın ("an" kullanıcı adı olsa da "yanlış" kalır)
        var clean = SupportBundle.Sanitize("yanlış ayar", "an", "PC", @"C:\Users\an");
        Assert.Equal("yanlış ayar", clean);
        Assert.Contains("<kullanici>", SupportBundle.Sanitize("ali dün geldi", "ali", "PC", @"C:\Users\x"));   // tam sözcük olarak geçince temizlenir
    }

    [Fact]
    public void Sanitize_EmptyAndNoMatchUnchanged()
    {
        Assert.Equal("", SupportBundle.Sanitize("", "demouser", "PC", @"C:\Users\demouser"));
        Assert.Equal("temiz metin", SupportBundle.Sanitize("temiz metin", "demouser", "DEMOPC", @"C:\Users\demouser"));
    }

    [Fact]
    public void DescribeState_HasFlagsAndCountsButNoPathsOrUrls()
    {
        var st = new AppState();
        st.GameFolders.Add(@"C:\Users\demouser\Games"); st.Bookmarks.Add(new Bookmark("Banka", "https://gizli.example/hesap"));
        var text = SupportBundle.DescribeState(st);
        Assert.Contains("StartupNeedsPad=True", text);
        Assert.Contains("GameFolders=1", text);
        Assert.Contains("Bookmarks=1", text);
        Assert.DoesNotContain("demouser", text);
        Assert.DoesNotContain("gizli.example", text);
        Assert.DoesNotContain("Banka", text);
    }

    [Fact]
    public void Create_WritesZipWithCleanLogs()
    {
        var data = AppPaths.Dir; Directory.CreateDirectory(data);
        var user = Environment.UserName;
        File.WriteAllText(Path.Combine(data, "konsol.log"), $@"2026-10-12 [test] C:\Users\{user}\x oyun açıldı");
        File.WriteAllText(Path.Combine(data, "crash.log"), "çökme " + Environment.MachineName);
        var outDir = Path.Combine(Path.GetTempPath(), "klyc-bundle-" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(outDir);
        try
        {
            var zip = SupportBundle.Create("sürüm bilgisi", outDir);
            Assert.True(File.Exists(zip));
            using var z = ZipFile.OpenRead(zip);
            var names = z.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("info.txt", names); Assert.Contains("konsol.log", names); Assert.Contains("crash.log", names);
            foreach (var e in z.Entries)
            {
                using var r = new StreamReader(e.Open());
                var body = r.ReadToEnd();
                if (user.Length >= 3) Assert.DoesNotContain(user, body, StringComparison.OrdinalIgnoreCase);
                if (Environment.MachineName.Length >= 3) Assert.DoesNotContain(Environment.MachineName, body, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { try { Directory.Delete(outDir, true); } catch { } }
    }

    [Fact]
    public void BuildInfo_ListsVersionInstallModeAndNoteAboutCleaning()
    {
        var t = SupportBundle.BuildInfo("0.9.1", true, "Windows 11", ".NET 8", "tr-TR", "1920x1080", 1, "çalışıyor · v1.10.1", "a=b", "Steam / Game: 3", new DateTime(2026, 10, 12, 20, 0, 0));
        Assert.Contains("Sürüm: 0.9.1", t); Assert.Contains("Setup.exe", t); Assert.Contains("Bağlı kol sayısı: 1", t); Assert.Contains("temizlenmiştir", t);
    }
}

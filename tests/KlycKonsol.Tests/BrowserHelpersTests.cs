using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

public class BrowserModesTests
{
    [Theory]
    [InlineData("https://www.youtube.com/tv", true)]
    [InlineData("https://www.youtube.com/tv#/watch?v=abc", true)]
    [InlineData("https://youtube.com/tv/", true)]
    [InlineData("https://www.youtube.com/tv/foo", true)]
    [InlineData("https://www.youtube.com/", false)]
    [InlineData("https://www.youtube.com/tvxyz", false)]
    [InlineData("https://www.youtube.com/watch?v=tv", false)]
    [InlineData("http://www.youtube.com/tv", false)]
    [InlineData("https://evil.com/www.youtube.com/tv", false)]
    [InlineData("https://www.youtube.com.evil.com/tv", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsYouTubeTv(string? url, bool expected) => Assert.Equal(expected, BrowserModes.IsYouTubeTv(url));

    [Theory]
    [InlineData("https://www.youtube.com/tv", true)]
    [InlineData("https://accounts.google.com/signin", true)]
    [InlineData("https://rr3---sn-x.googlevideo.com/videoplayback", true)]
    [InlineData("https://www.google.com.evil.com/", false)]
    [InlineData("https://notyoutube.com/", false)]
    [InlineData("https://twitch.tv/", false)]
    [InlineData("http://www.youtube.com/tv", false)]
    [InlineData(null, false)]
    public void StaysInTv(string? url, bool expected) => Assert.Equal(expected, BrowserModes.StaysInTv(url));
}

public class DownloadHelperTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "klyc-dl-" + Guid.NewGuid().ToString("N")[..8]);
    public DownloadHelperTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Theory]
    [InlineData("setup.exe", "setup.exe")]
    [InlineData("..\\..\\Windows\\evil.dll", "evil.dll")]
    [InlineData("a/b/c.txt", "c.txt")]
    [InlineData("rapor: 2024?.pdf", "rapor_ 2024_.pdf")]
    [InlineData("  nokta... ", "nokta")]
    [InlineData("", "indirme")]
    [InlineData(null, "indirme")]
    [InlineData("CON", "indirme_CON")]
    [InlineData("nul.txt", "indirme_nul.txt")]
    public void Sanitize(string? input, string expected) => Assert.Equal(expected, DownloadHelper.Sanitize(input));

    [Fact]
    public void Sanitize_LongNamesKeepExtension()
    {
        var s = DownloadHelper.Sanitize(new string('a', 300) + ".zip");
        Assert.True(s.Length <= 120);
        Assert.EndsWith(".zip", s);
    }

    [Fact]
    public void UniquePath_NeverOverwrites()
    {
        var first = DownloadHelper.UniquePath(_dir, "oyun.zip");
        Assert.Equal(Path.Combine(_dir, "oyun.zip"), first);
        File.WriteAllText(first, "x");
        var second = DownloadHelper.UniquePath(_dir, "oyun.zip");
        Assert.Equal(Path.Combine(_dir, "oyun (2).zip"), second);
        File.WriteAllText(second, "x");
        Assert.Equal(Path.Combine(_dir, "oyun (3).zip"), DownloadHelper.UniquePath(_dir, "oyun.zip"));
    }

    [Fact]
    public void UniquePath_RespectsPartialDownloads()
    {
        File.WriteAllText(Path.Combine(_dir, "a.bin.crdownload"), "x");
        Assert.Equal(Path.Combine(_dir, "a (2).bin"), DownloadHelper.UniquePath(_dir, "a.bin"));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    [InlineData(-1, "?")]
    public void FormatSize(long bytes, string expected)
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try { Assert.Equal(expected, DownloadHelper.FormatSize(bytes)); } finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Progress_WithAndWithoutTotal()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("%50 · 5 MB / 10 MB", DownloadHelper.Progress(5L * 1024 * 1024, 10L * 1024 * 1024));
            Assert.Equal("2 KB", DownloadHelper.Progress(2048, null));
            Assert.Equal("%100 · 2 KB / 1 KB", DownloadHelper.Progress(2048, 1024));   // taşan değer %100'de kalır
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void DownloadsDir_Exists() => Assert.True(Directory.Exists(DownloadHelper.DownloadsDir()));
}

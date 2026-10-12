using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

public class LibraryWatchTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "klyc-libwatch-" + Guid.NewGuid().ToString("N")[..8]);

    public LibraryWatchTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Signature_ChangesWhenManifestAddedChangedRemoved_AndIgnoresOtherFiles()
    {
        var roots = new[] { _dir };
        var empty = LibraryWatch.Signature(roots);
        Assert.Equal(empty, LibraryWatch.Signature(roots));   // kararlı

        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "x");
        Assert.Equal(empty, LibraryWatch.Signature(roots));   // ilgisiz dosya imzayı değiştirmez

        var acf = Path.Combine(_dir, "appmanifest_123.acf");
        File.WriteAllText(acf, "\"AppState\" { \"appid\" \"123\" }");
        var added = LibraryWatch.Signature(roots);
        Assert.NotEqual(empty, added);

        File.WriteAllText(acf, "\"AppState\" { \"appid\" \"123\" \"name\" \"Yeni Oyun\" }");
        File.SetLastWriteTimeUtc(acf, DateTime.UtcNow.AddMinutes(5));
        var changed = LibraryWatch.Signature(roots);
        Assert.NotEqual(added, changed);

        File.Delete(acf);
        Assert.Equal(empty, LibraryWatch.Signature(roots));
    }

    [Fact]
    public void Signature_MissingFolder_DoesNotThrow() =>
        Assert.NotEmpty(LibraryWatch.Signature(new[] { Path.Combine(_dir, "yok") }));

    [Fact]
    public void Watcher_FiresOnceAfterBurstOfEvents_ThenStopsAfterDispose()
    {
        var hits = 0;
        using var fired = new ManualResetEventSlim();
        var w = new LibraryWatch.Watcher(new[] { _dir }, () => { Interlocked.Increment(ref hits); fired.Set(); }, 250);
        Assert.True(w.Count > 0);
        for (var i = 0; i < 4; i++) File.WriteAllText(Path.Combine(_dir, $"appmanifest_{i}.acf"), "x");
        Assert.True(fired.Wait(5000), "olay gelmedi");
        Thread.Sleep(700);
        Assert.Equal(1, hits);   // art arda gelen olaylar tek bildirime indi

        w.Dispose();
        fired.Reset();
        File.WriteAllText(Path.Combine(_dir, "appmanifest_9.acf"), "x");
        Assert.False(fired.Wait(900));   // Dispose sonrası sessiz
    }
}

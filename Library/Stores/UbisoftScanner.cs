using KlycKonsol.Models;
using Microsoft.Win32;

namespace KlycKonsol.Library.Stores;

/// <summary>
/// Ubisoft Connect (Uplay) oyunları: HKLM\SOFTWARE\(WOW6432Node\)Ubisoft\Launcher\Installs\&lt;id&gt; altında InstallDir bulunur;
/// görünen ad "Uplay Install &lt;id&gt;" kaldırma anahtarındaki DisplayName (yoksa klasör adı). Başlatma: uplay://launch/&lt;id&gt;/0.
/// </summary>
public static class UbisoftScanner
{
    public sealed record UbiInfo(string Id, string InstallDir, string? DisplayName);

    public static List<Game> Scan()
    {
        var names = new Dictionary<string, string>();
        foreach (var k in StoreUtil.EnumerateSubKeys(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
        {
            using (k)
            {
                var n = System.IO.Path.GetFileName(k.Name);
                if (n.StartsWith("Uplay Install ", StringComparison.OrdinalIgnoreCase) && StoreUtil.Str(k, "DisplayName") is { } dn)
                    names[n["Uplay Install ".Length..].Trim()] = dn;
            }
        }

        var infos = new List<UbiInfo>();
        foreach (var k in StoreUtil.EnumerateSubKeys(RegistryHive.LocalMachine, @"SOFTWARE\Ubisoft\Launcher\Installs"))
        {
            using (k)
            {
                var id = System.IO.Path.GetFileName(k.Name);
                if (StoreUtil.Str(k, "InstallDir") is { Length: > 0 } dir)
                    infos.Add(new UbiInfo(id, dir, names.GetValueOrDefault(id)));
            }
        }
        return ToGames(infos, Directory.Exists);
    }

    public static List<Game> ToGames(IEnumerable<UbiInfo> infos, Func<string, bool> dirExists)
    {
        var games = new List<Game>();
        foreach (var i in infos.GroupBy(x => x.Id).Select(g => g.First()))
        {
            var dir = i.InstallDir.Replace('/', '\\').TrimEnd('\\');
            if (!dirExists(dir)) continue;   // kayıt kalmış, oyun silinmiş
            var title = i.DisplayName ?? System.IO.Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(title)) continue;
            games.Add(new Game
            {
                Title = title.Replace("™", "").Replace("®", "").Trim(),
                Source = "Ubisoft",
                Target = $"uplay://launch/{i.Id}/0",
                Id = $"ubi:{i.Id}",
                InstallDir = dir,
                Accent = Palette.For(title),
            });
        }
        return games;
    }
}

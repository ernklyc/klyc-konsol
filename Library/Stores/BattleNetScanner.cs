using KlycKonsol.Models;
using Microsoft.Win32;

namespace KlycKonsol.Library.Stores;

/// <summary>
/// Battle.net (Blizzard) oyunları: Windows "Uygulamalar ve özellikler" kaydında yayıncı Blizzard Entertainment olan girdiler.
/// Başlatma: biliniyorsa battlenet://&lt;kod&gt; (Battle.net istemcisi açılır ve oyunu başlatır); değilse DisplayIcon'daki exe.
/// </summary>
public static class BattleNetScanner
{
    public sealed record BnetInfo(string DisplayName, string? InstallLocation, string? DisplayIcon);

    /// <summary>Oyun adı → battlenet:// kodu.</summary>
    static readonly (string match, string code)[] Codes =
    {
        ("overwatch", "Pro"), ("world of warcraft", "WoW"), ("diablo iv", "Fen"), ("diablo iii", "D3"),
        ("diablo ii", "D2R"), ("starcraft ii", "S2"), ("starcraft remastered", "S1"), ("hearthstone", "WTCG"),
        ("heroes of the storm", "Hero"), ("warcraft iii", "W3"),
    };

    public static List<Game> Scan()
    {
        var infos = new List<BnetInfo>();
        foreach (var k in StoreUtil.EnumerateSubKeys(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
        {
            using (k)
            {
                var pub = StoreUtil.Str(k, "Publisher");
                var name = StoreUtil.Str(k, "DisplayName");
                if (pub == null || name == null || !pub.Contains("Blizzard", StringComparison.OrdinalIgnoreCase)) continue;
                infos.Add(new BnetInfo(name, StoreUtil.Str(k, "InstallLocation"), StoreUtil.Str(k, "DisplayIcon")));
            }
        }
        return ToGames(infos, File.Exists, Directory.Exists);
    }

    public static List<Game> ToGames(IEnumerable<BnetInfo> infos, Func<string, bool> fileExists, Func<string, bool> dirExists)
    {
        var games = new List<Game>();
        foreach (var i in infos.GroupBy(x => x.DisplayName).Select(g => g.First()))
        {
            if (i.DisplayName.Contains("Battle.net", StringComparison.OrdinalIgnoreCase)) continue;   // istemcinin kendisi
            var loc = i.InstallLocation?.Trim().Trim('"');
            if (string.IsNullOrEmpty(loc) || !dirExists(loc)) continue;

            var code = Codes.FirstOrDefault(c => i.DisplayName.Contains(c.match, StringComparison.OrdinalIgnoreCase)).code;
            var icon = i.DisplayIcon?.Split(',')[0].Trim('"');
            string? target = code != null ? $"battlenet://{code}" : (icon != null && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && fileExists(icon) ? icon : null);
            if (target == null) continue;

            var cover = icon != null && fileExists(icon) ? ShortcutScanner.LoadIcon(icon, null) : null;
            games.Add(new Game
            {
                Title = i.DisplayName.Trim(),
                Source = "Battle.net",
                Target = target,
                Id = $"bnet:{StoreUtil.Norm(i.DisplayName)}",
                InstallDir = loc,
                Cover = cover,
                IconCover = cover != null,
                Accent = Palette.For(i.DisplayName),
            });
        }
        return games;
    }
}

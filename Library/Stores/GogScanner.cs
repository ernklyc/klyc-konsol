using KlycKonsol.Models;
using Microsoft.Win32;

namespace KlycKonsol.Library.Stores;

/// <summary>
/// GOG Galaxy / GOG.com oyunları: HKLM\SOFTWARE\(WOW6432Node\)GOG.com\Games\&lt;gameID&gt; altında gameName, gameID, path (kurulum klasörü),
/// exe (ana çalıştırılabilir) ve launchParam bulunur. Başlatma: exe varsa doğrudan; yoksa GalaxyClient /command=runGame.
/// </summary>
public static class GogScanner
{
    public sealed record GogInfo(string GameId, string Name, string Path, string? Exe, string? Param);

    public static List<Game> Scan()
    {
        var infos = new List<GogInfo>();
        foreach (var key in StoreUtil.EnumerateSubKeys(RegistryHive.LocalMachine, @"SOFTWARE\GOG.com\Games"))
        {
            using (key)
            {
                var info = FromValues(new Dictionary<string, string?>
                {
                    ["gameID"] = key.GetValue("gameID")?.ToString(),
                    ["gameName"] = StoreUtil.Str(key, "gameName"),
                    ["path"] = StoreUtil.Str(key, "path"),
                    ["exe"] = StoreUtil.Str(key, "exe"),
                    ["launchParam"] = StoreUtil.Str(key, "launchParam"),
                    ["subkey"] = System.IO.Path.GetFileName(key.Name),
                });
                if (info != null) infos.Add(info);
            }
        }
        return ToGames(infos, File.Exists, FindGalaxy());
    }

    /// <summary>Kayıt defteri değerlerinden bir GOG girdisi (saf fonksiyon, test edilebilir).</summary>
    public static GogInfo? FromValues(IReadOnlyDictionary<string, string?> v)
    {
        var id = v.GetValueOrDefault("gameID") ?? v.GetValueOrDefault("subkey");
        var name = v.GetValueOrDefault("gameName");
        var path = v.GetValueOrDefault("path");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return null;
        return new GogInfo(id, name.Trim(), path.Trim(), v.GetValueOrDefault("exe"), v.GetValueOrDefault("launchParam"));
    }

    public static List<Game> ToGames(IEnumerable<GogInfo> infos, Func<string, bool> fileExists, string? galaxyExe)
    {
        var games = new List<Game>();
        foreach (var i in infos.GroupBy(x => x.GameId).Select(g => g.First()))
        {
            string? target = null, args = null;
            if (!string.IsNullOrWhiteSpace(i.Exe) && fileExists(i.Exe)) { target = i.Exe; args = i.Param; }
            else if (galaxyExe != null)
            {
                target = galaxyExe;
                args = $"/command=runGame /gameId={i.GameId} /path=\"{i.Path}\"";
            }
            if (target == null) continue;

            var icon = fileExists(i.Exe ?? "") ? ShortcutScanner.LoadIcon(i.Exe, null) : null;
            games.Add(new Game
            {
                Title = i.Name,
                Source = "GOG",
                Target = target,
                Args = args,
                Id = $"gog:{i.GameId}",
                InstallDir = i.Path,
                Cover = icon,
                IconCover = icon != null,
                Accent = Palette.For(i.Name),
            });
        }
        return games;
    }

    static string? FindGalaxy()
    {
        foreach (var pf in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var p = System.IO.Path.Combine(Environment.GetFolderPath(pf), "GOG Galaxy", "GalaxyClient.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}

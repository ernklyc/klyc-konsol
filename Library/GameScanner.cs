using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KlycKonsol.Models;
using Microsoft.Win32;

namespace KlycKonsol.Library;

public static class GameScanner
{
    static readonly HashSet<string> SteamIgnoredIds = new() { "228980" };
    static readonly string[] SteamIgnoredPrefixes = { "Steamworks", "Proton", "Steam Linux Runtime", "Steam Controller" };

    /// <summary>Mağaza sırası: aynı oyun birden çok yerde varsa listede ilk sıradaki tutulur.</summary>
    static readonly string[] Rank = { "steam:", "epic:", "xbox:", "ea:", "gog:", "ubi:", "riot:", "bnet:", "lnk:" };

    static int RankOf(Game g)
    {
        for (var i = 0; i < Rank.Length; i++) if (g.Id.StartsWith(Rank[i])) return i;
        return Rank.Length;
    }

    /// <summary>Tüm mağazalardaki oyunlar + elle eklenenler + uygulamalar. Aynı oyunun kopyaları tek girişte birleşir.</summary>
    public static List<Game> ScanAll(UserLibrary user, AppState state)
    {
        var all = new List<Game>();
        void Add(Func<IEnumerable<Game>> scan) { try { all.AddRange(scan()); } catch { } }

        Add(ScanSteam);
        Add(ScanEpic);
        Add(Stores.XboxScanner.Scan);
        Add(Stores.EaScanner.Scan);
        Add(Stores.GogScanner.Scan);
        Add(Stores.UbisoftScanner.Scan);
        Add(Stores.RiotScanner.Scan);
        Add(Stores.BattleNetScanner.Scan);
        Add(() => ShortcutScanner.Scan(ShortcutScanner.DefaultFolders().Concat(state.GameFolders)));

        var result = Dedupe(all);
        foreach (var e in user.Entries) result.Add(FromEntry(e));   // elle eklenenler her zaman kalır
        result.AddRange(DefaultApps());

        ArtService.ApplyCached(result);
        ControllerSupportService.ApplyCached(result, state);
        var steamUsage = SteamLocal.LoadUsage();
        foreach (var g in result)
        {
            g.IsFavorite = state.Favorites.Contains(g.Id);
            var last = state.LastPlayed.TryGetValue(g.Id, out var t) ? t : 0;
            long seconds = state.PlaySeconds.TryGetValue(g.Id, out var ps) ? ps : 0;
            if (g.Id.StartsWith("steam:") && steamUsage.TryGetValue(g.SteamAppId ?? "", out var u))
            {
                seconds = Math.Max(seconds, u.PlaytimeMinutes * 60);   // Steam kendi sürelerini tutar
                last = Math.Max(last, u.LastPlayed);
            }
            g.PlaySeconds = seconds;
            g.LastPlayed = last;
        }
        return result;
    }

    /// <summary>Önce kimliğe, sonra (takma adlar dahil) başlığa göre tekilleştirir; en iyi mağaza sırası kazanır.</summary>
    public static List<Game> Dedupe(IEnumerable<Game> games)
    {
        var byId = games.GroupBy(g => g.Id).Select(g => g.First());
        return byId
            .OrderBy(RankOf)
            .GroupBy(g => Stores.StoreUtil.CanonicalTitle(g.Title))
            .Select(g => g.First())
            .ToList();
    }

    static IEnumerable<Game> DefaultApps()
    {
        yield return new Game { Title = "Dosyalar", Kind = EntryKind.App, Glyph = "\uE838", Target = "explorer.exe", Id = "app:files", Accent = Palette.For("Dosyalar") };
        yield return new Game { Title = "Windows Ayarları", Kind = EntryKind.App, Glyph = "\uE713", Target = "ms-settings:", Id = "app:settings", Accent = Palette.For("Ayarlar") };
    }

    static Game FromEntry(UserEntry e)
    {
        var app = e.Kind.Equals("app", StringComparison.OrdinalIgnoreCase);
        return new Game
        {
            Title = e.Title,
            Source = app ? "" : "Eklenen",
            Kind = app ? EntryKind.App : EntryKind.Game,
            Target = e.Target,
            Args = e.Args,
            Id = $"user:{e.Title}",
            InstallDir = e.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(e.Target) ? Path.GetDirectoryName(e.Target) : null,
            Glyph = app ? "\uE7FC" : "\uE7FC",
            Cover = e.Image != null && File.Exists(e.Image) ? LoadImage(e.Image, 460) : null,
            Support = app ? PadSupport.Unknown : (e.Controller ?? true) ? PadSupport.Full : PadSupport.None,
            Accent = Palette.For(e.Title),
        };
    }

    // ---- Steam ----------------------------------------------------------

    static IEnumerable<Game> ScanSteam()
    {
        var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        if (string.IsNullOrEmpty(steam)) yield break;
        steam = steam.Replace('/', '\\');

        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;

            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string text;
                try { text = File.ReadAllText(acf); } catch { continue; }

                var id = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                var installDirName = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                if (id == "" || name == "" || SteamIgnoredIds.Contains(id)) continue;
                if (SteamIgnoredPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

                yield return new Game
                {
                    Title = name,
                    Source = "Steam",
                    Target = $"steam://rungameid/{id}",
                    Id = $"steam:{id}",
                    SteamAppId = id,
                    InstallDir = installDirName == "" ? null : Path.Combine(lib, "steamapps", "common", installDirName),
                    Cover = FindSteamFile(steam, id, new[] { "library_600x900*.jpg", "header*.jpg" }, 460, out _),
                    HeroPath = FindSteamPath(steam, id, "library_hero.jpg"),
                    Accent = Palette.For(name),
                };
            }
        }
    }

    static string? FindSteamPath(string steamRoot, string appId, string pattern)
    {
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        var flat = Path.Combine(cache, $"{appId}_{pattern}");
        if (File.Exists(flat)) return flat;

        var dir = Path.Combine(cache, appId);
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).FirstOrDefault();
    }

    static ImageSource? FindSteamFile(string steamRoot, string appId, string[] patterns, int width, out string? path)
    {
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        path = null;
        if (!Directory.Exists(cache)) return null;

        foreach (var pattern in patterns)
        {
            var flat = Path.Combine(cache, $"{appId}_{pattern}");
            var hit = Directory.EnumerateFiles(cache, $"{appId}_{pattern}", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (hit == null)
            {
                var dir = Path.Combine(cache, appId);
                if (Directory.Exists(dir))
                    hit = Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).FirstOrDefault();
            }
            if (hit != null) { path = hit; return LoadImage(hit, width); }
        }
        return null;
    }

    // ---- Epic -----------------------------------------------------------

    static IEnumerable<Game> ScanEpic()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;

        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            Game? game = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                string Str(string k) => r.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";

                if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
                var app = Str("AppName");
                var main = Str("MainGameAppName");
                if (main != "" && main != app) continue; // DLC

                var name = Str("DisplayName");
                var ns = Str("CatalogNamespace");
                var item = Str("CatalogItemId");
                if (name == "" || ns == "" || item == "") continue;

                game = new Game
                {
                    Title = name,
                    Source = "Epic",
                    Target = $"com.epicgames.launcher://apps/{ns}%3A{item}%3A{app}?action=launch&silent=true",
                    Id = $"epic:{app}",
                    Meta = item,
                    InstallDir = Str("InstallLocation") is { Length: > 0 } loc ? loc : null,
                    Accent = Palette.For(name),
                };
            }
            catch { }
            if (game != null) yield return game;
        }
    }

    // ---- Ortak ----------------------------------------------------------

    public static ImageSource? LoadImage(string path, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}

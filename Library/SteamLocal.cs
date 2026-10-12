using Microsoft.Win32;

namespace KlycKonsol.Library;

/// <summary>Steam'in yerel kullanıcı verisi: son oturum açan hesabın oynama süreleri (dakika) ve son oynama zamanları.</summary>
public static class SteamLocal
{
    public sealed record AppUsage(string AppId, long PlaytimeMinutes, long LastPlayed);

    public static string? SteamRoot()
    {
        var p = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        return string.IsNullOrEmpty(p) ? null : p.Replace('/', '\\');
    }

    /// <summary>loginusers.vdf'ten en son kullanılan hesabın 32 bit kimliği (userdata klasör adı).</summary>
    public static string? MostRecentUserId(string steamRoot)
    {
        var f = Path.Combine(steamRoot, "config", "loginusers.vdf");
        if (!File.Exists(f)) return null;
        return MostRecentUserIdFrom(File.ReadAllText(f));
    }

    public static string? MostRecentUserIdFrom(string loginUsersVdf)
    {
        var users = Vdf.Parse(loginUsersVdf)["users"];
        if (users == null) return null;

        string? best = null;
        long bestTs = -1;
        foreach (var (id64, node) in users.Children)
        {
            var recent = node.Str("MostRecent") == "1";
            var ts = node.Long("Timestamp") + (recent ? long.MaxValue / 2 : 0);   // MostRecent=1 her zaman kazanır
            if (ts > bestTs && long.TryParse(id64, out var v)) { bestTs = ts; best = (v - 76561197960265728L).ToString(); }
        }
        return best;
    }

    public static List<AppUsage> ReadUsage(string localConfigVdf)
    {
        var apps = Vdf.Parse(localConfigVdf)["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["apps"]
                   ?? Vdf.Parse(localConfigVdf)["UserLocalConfigStore"]?["Software"]?["valve"]?["Steam"]?["apps"];
        var list = new List<AppUsage>();
        if (apps == null) return list;
        foreach (var (id, n) in apps.Children)
        {
            var minutes = n.Long("Playtime");
            var last = n.Long("LastPlayed");
            if (minutes > 0 || last > 0) list.Add(new AppUsage(id, minutes, last));
        }
        return list;
    }

    public static Dictionary<string, AppUsage> LoadUsage()
    {
        try
        {
            var root = SteamRoot();
            if (root == null) return new();
            var uid = MostRecentUserId(root);
            if (uid == null) return new();
            var f = Path.Combine(root, "userdata", uid, "config", "localconfig.vdf");
            if (!File.Exists(f)) return new();
            return ReadUsage(File.ReadAllText(f)).GroupBy(a => a.AppId).ToDictionary(g => g.Key, g => g.First());
        }
        catch { return new(); }
    }
}

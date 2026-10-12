using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KlycKonsol.Models;

namespace KlycKonsol.Library;

/// <summary>
/// Masaüstündeki "Game" klasörü (ve eklenen diğer klasörler) gibi yerlerdeki .url / .lnk kısayollarından oyun listesi çıkarır.
/// Steam (steam://rungameid) ve Epic (com.epicgames.launcher://) kısayolları kimlikleriyle tanınır; Store/Xbox uygulamaları
/// (hedefi boş .lnk) ve herhangi bir .exe kısayolu da başlatılabilir (kısayol dosyası kabuk üzerinden açılır).
/// </summary>
public static class ShortcutScanner
{
    public static IEnumerable<string> DefaultFolders()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);   // OneDrive yönlendirmesini de verir
        foreach (var name in new[] { "Game", "Games", "Oyunlar" })
        {
            var p = Path.Combine(desktop, name);
            if (Directory.Exists(p)) yield return p;
        }
    }

    public static List<Game> Scan(IEnumerable<string> folders)
    {
        var result = new List<Game>();
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly).Where(f => f.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)).ToList(); }
            catch { continue; }

            foreach (var file in files)
            {
                try { var g = Parse(file); if (g != null) result.Add(g); } catch { }
            }
        }
        return result;
    }

    static Game? Parse(string file)
    {
        var title = Path.GetFileNameWithoutExtension(file).Trim();
        if (title.Length == 0) return null;

        string? url = null, iconFile = null, targetPath = null;
        var isUrl = file.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

        if (isUrl)
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url = line[4..].Trim();
                else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) iconFile = line[9..].Trim();
            }
            if (string.IsNullOrEmpty(url)) return null;
        }
        else
        {
            dynamic? sc = null;
            try
            {
                var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                sc = ((dynamic)shell).CreateShortcut(file);
                targetPath = (string)sc.TargetPath;
                var loc = (string)sc.IconLocation;       // "yol,indeks"
                iconFile = loc.Split(',')[0].Trim().Trim('"');
            }
            catch { }
        }

        string id, source;
        string? steamId = null, meta = null;
        var steam = url == null ? null : Regex.Match(url, @"steam://rungameid/(\d+)", RegexOptions.IgnoreCase);
        var epic = url == null ? null : Regex.Match(url, @"com\.epicgames\.launcher://apps/([^%?]+)%3A([^%?]+)%3A([^?]+)", RegexOptions.IgnoreCase);

        if (steam is { Success: true }) { steamId = steam.Groups[1].Value; id = $"steam:{steamId}"; source = "Steam"; }
        else if (epic is { Success: true }) { meta = epic.Groups[2].Value; id = $"epic:{epic.Groups[3].Value}"; source = "Epic"; }
        else
        {
            id = "lnk:" + new string(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            source = !isUrl && string.IsNullOrEmpty(targetPath) ? "Xbox / Store" : "Kısayol";
        }

        var icon = LoadIcon(iconFile, targetPath);
        return new Game
        {
            Title = title.Replace("™", "").Replace("®", "").Trim(),
            Source = source,
            Target = url ?? file,            // .url: doğrudan URL; .lnk: kısayol dosyasının kendisi (Store uygulamaları için de çalışır)
            Id = id,
            Meta = meta,
            SteamAppId = steamId,
            Cover = icon,
            IconCover = icon != null,
            Accent = Palette.For(title),
        };
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    /// <summary>Kısayolun simgesini yükler (.ico ya da .exe içinden). Başarısızsa null.</summary>
    public static ImageSource? LoadIcon(string? iconFile, string? targetPath)
    {
        foreach (var path in new[] { iconFile, targetPath })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            try
            {
                if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    var dec = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
                    best.Freeze();
                    return best;
                }
                var large = new IntPtr[1];
                if (ExtractIconEx(path, 0, large, null, 1) == 0 || large[0] == IntPtr.Zero) continue;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(large[0], System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DestroyIcon(large[0]); }
            }
            catch { }
        }
        return null;
    }
}

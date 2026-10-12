using System.Xml.Linq;
using KlycKonsol.Models;
using Microsoft.Win32;

namespace KlycKonsol.Library.Stores;

/// <summary>
/// Xbox / Game Pass (Microsoft Store GDK) oyunları. Oyunlar sürücü köklerindeki "XboxGames\&lt;oyun&gt;\Content" klasörüne kurulur;
/// orada appxmanifest.xml ve MicrosoftGame.config bulunur. Başlatma: shell:AppsFolder\&lt;PackageFamilyName&gt;!&lt;AppId&gt;.
/// PackageFamilyName, paket kayıt defterinden (AppModel\Repository\Packages) okunur (Get-AppxPackage'dan çok daha hızlı).
/// </summary>
public static class XboxScanner
{
    public sealed record XboxInfo(string IdentityName, string AppId, string Title, string? LogoRelPath);

    const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public static List<Game> Scan() => Scan(StoreUtil.FixedDriveRoots(), FindFamilyName);

    /// <summary>Test edilebilir sürüm: kökler ve PackageFamilyName çözücüsü dışarıdan verilir.</summary>
    public static List<Game> Scan(IEnumerable<string> driveRoots, Func<string, string?> familyNameOf)
    {
        var games = new List<Game>();
        foreach (var root in driveRoots)
        {
            var xboxRoot = Path.Combine(root, "XboxGames");
            if (!Directory.Exists(xboxRoot)) continue;

            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(xboxRoot).ToList(); } catch { continue; }

            foreach (var dir in dirs)
            {
                var content = Path.Combine(dir, "Content");
                if (!Directory.Exists(content)) continue;
                try
                {
                    var manifest = FindFile(content, "appxmanifest.xml");
                    if (manifest == null) continue;
                    var msgc = FindFile(content, "MicrosoftGame.config");

                    var info = Parse(File.ReadAllText(manifest), msgc == null ? null : File.ReadAllText(msgc), Path.GetFileName(dir));
                    if (info == null) continue;

                    var pfn = familyNameOf(info.IdentityName);
                    if (pfn == null) continue;   // kayıtlı değil: kurulum yarım ya da kaldırılmış

                    string? logo = null;
                    if (info.LogoRelPath != null)
                    {
                        var p = Path.Combine(content, info.LogoRelPath.Replace('/', '\\'));
                        if (File.Exists(p)) logo = p;
                    }
                    var icon = logo != null ? GameScanner.LoadImage(logo, 460) : null;

                    games.Add(new Game
                    {
                        Title = info.Title,
                        Source = "Xbox",
                        Target = $"shell:AppsFolder\\{pfn}!{info.AppId}",
                        Id = $"xbox:{info.IdentityName}",
                        InstallDir = content,
                        Cover = icon,
                        IconCover = icon != null,
                        Accent = Palette.For(info.Title),
                    });
                }
                catch { }
            }
        }
        return games;
    }

    static string? FindFile(string dir, string name) =>
        Directory.EnumerateFiles(dir).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));

    static string? Attr(XElement? e, string name) => e?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    static XElement? Child(XElement? e, string name) => e?.Descendants().FirstOrDefault(x => x.Name.LocalName == name);

    /// <summary>appxmanifest.xml + (varsa) MicrosoftGame.config içeriğinden oyunun kimliği, görünen adı ve logosu.</summary>
    public static XboxInfo? Parse(string manifestXml, string? msgcXml, string folderName)
    {
        var manifest = XDocument.Parse(manifestXml).Root;
        var identity = Attr(Child(manifest, "Identity"), "Name");
        var appId = Attr(Child(manifest, "Application"), "Id");
        if (string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(appId)) return null;

        XElement? shell = null;
        if (msgcXml != null)
        {
            try { shell = Child(XDocument.Parse(msgcXml).Root, "ShellVisuals"); } catch { }
        }

        var title = Attr(shell, "DefaultDisplayName");
        if (string.IsNullOrWhiteSpace(title) || title.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase))
        {
            var d = Child(Child(manifest, "Properties"), "DisplayName")?.Value;
            title = !string.IsNullOrWhiteSpace(d) && !d.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase)
                ? d
                : folderName.Replace("- ", ": ").Trim();   // klasör adında ":" yerine "-" kullanılır
        }

        var logo = Attr(shell, "Square480x480Logo") ?? Attr(shell, "Square150x150Logo") ?? Attr(shell, "StoreLogo")
                   ?? Attr(Child(manifest, "VisualElements"), "Square150x150Logo")
                   ?? Attr(Child(manifest, "VisualElements"), "Square44x44Logo");

        return new XboxInfo(identity, appId, title!, logo);
    }

    /// <summary>"Name_Version_Arch__PublisherId" paket anahtarından PackageFamilyName ("Name_PublisherId") bulur.</summary>
    static string? FindFamilyName(string identityName)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(PackagesKey);
            if (k == null) return null;
            foreach (var full in k.GetSubKeyNames())
            {
                if (!full.StartsWith(identityName + "_", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = full.Split('_');
                if (parts.Length < 5 || !parts[0].Equals(identityName, StringComparison.OrdinalIgnoreCase)) continue;
                return identityName + "_" + parts[^1];
            }
        }
        catch { }
        return null;
    }
}

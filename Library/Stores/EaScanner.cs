using System.Text.RegularExpressions;
using System.Xml.Linq;
using KlycKonsol.Models;
using Microsoft.Win32;

namespace KlycKonsol.Library.Stores;

/// <summary>
/// EA app / Origin oyunları. EA app, oyunları kurulum klasörlerine koyar (varsayılan "Program Files\EA Games"; asıl klasör
/// ProgramData\EA Desktop\machine.ini içindeki machine.downloadinplacedir). Her oyunun klasöründe __Installer\installerdata.xml
/// (başlık, contentID, çalıştırılabilir dosya) bulunur. Başlatma: exe varsa doğrudan; yoksa origin2://game/launch.
/// </summary>
public static class EaScanner
{
    public sealed record EaInfo(string Title, string? ContentId, string? ExeRaw);

    public static List<Game> Scan()
    {
        var roots = new List<string>();
        try
        {
            var ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EA Desktop", "machine.ini");
            if (File.Exists(ini))
            {
                var m = Regex.Match(File.ReadAllText(ini), @"^machine\.downloadinplacedir=(.+)$", RegexOptions.Multiline);
                if (m.Success) roots.Add(m.Groups[1].Value.Trim());
            }
        }
        catch { }
        foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var b = Environment.GetFolderPath(pf);
            roots.Add(Path.Combine(b, "EA Games"));
            roots.Add(Path.Combine(b, "Origin Games"));
        }
        return Scan(roots);
    }

    public static List<Game> Scan(IEnumerable<string> roots)
    {
        var games = new List<Game>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(root).ToList(); } catch { continue; }

            foreach (var dir in dirs)
            {
                try
                {
                    var xml = new[] { "__Installer", "Installer" }
                        .Select(d => Path.Combine(dir, d, "installerdata.xml")).FirstOrDefault(File.Exists);
                    if (xml == null) continue;

                    var info = Parse(File.ReadAllText(xml));
                    if (info == null) continue;

                    var exe = ResolveExe(info.ExeRaw, dir);
                    var title = info.Title.Replace("™", "").Replace("®", "").Trim();

                    games.Add(new Game
                    {
                        Title = title,
                        Source = "EA",
                        Target = exe ?? (info.ContentId != null ? $"origin2://game/launch/?offerIds={info.ContentId}" : null),
                        Id = $"ea:{StoreUtil.Norm(title)}",
                        InstallDir = dir,
                        Accent = Palette.For(title),
                        Cover = exe != null ? ShortcutScanner.LoadIcon(exe, null) : null,
                        IconCover = exe != null,
                    });
                }
                catch { }
            }
        }
        return games.Where(g => g.Target != null).ToList();
    }

    /// <summary>installerdata.xml içeriğinden başlık (en_US öncelikli), contentID ve başlatıcı dosya yolu.</summary>
    public static EaInfo? Parse(string xmlText)
    {
        var root = XDocument.Parse(xmlText.TrimStart('﻿')).Root;
        if (root == null) return null;

        var titles = root.Descendants().Where(e => e.Name.LocalName == "gameTitle").ToList();
        var title = (titles.FirstOrDefault(t => (string?)t.Attribute("locale") == "en_US") ?? titles.FirstOrDefault())?.Value?.Trim();
        if (string.IsNullOrEmpty(title)) return null;

        var content = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "contentID")?.Value?.Trim();

        // Birden çok başlatıcı olabilir (x86 / x64); x64 olanı tercih et.
        var launchers = root.Descendants().Where(e => e.Name.LocalName == "launcher")
            .Select(l => l.Descendants().FirstOrDefault(x => x.Name.LocalName == "filePath")?.Value?.Trim())
            .Where(p => !string.IsNullOrEmpty(p)).ToList();
        var exe = launchers.FirstOrDefault(p => p!.Contains("x64", StringComparison.OrdinalIgnoreCase)
                                                 || p.Contains("64", StringComparison.Ordinal)) ?? launchers.FirstOrDefault();
        return new EaInfo(title, content, exe);
    }

    /// <summary>"[HKEY_LOCAL_MACHINE\...\Install Dir]game.exe" biçimini çözer; düz yol ise oyun klasörüne göre birleştirir.</summary>
    public static string? ResolveExe(string? raw, string gameDir)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var m = Regex.Match(raw, @"^\[(HKEY_[A-Z_]+)\\(.+)\\([^\\\]]+)\](.*)$");
        string? path = null;
        if (m.Success)
        {
            var hive = m.Groups[1].Value switch
            {
                "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
                "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
                _ => (RegistryHive?)null,
            };
            if (hive != null)
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using var b = RegistryKey.OpenBaseKey(hive.Value, view);
                        using var k = b.OpenSubKey(m.Groups[2].Value);
                        if (k?.GetValue(m.Groups[3].Value) is string v)
                        {
                            path = Path.Combine(v, m.Groups[4].Value.TrimStart('\\'));
                            if (File.Exists(path)) return path;
                        }
                    }
                    catch { }
                }
            }
            // Kayıt çözülemedi: dosya adını oyun klasöründe ara.
            raw = m.Groups[4].Value.TrimStart('\\');
        }

        var rel = Path.Combine(gameDir, raw.TrimStart('\\', '/'));
        if (File.Exists(rel)) return rel;
        var byName = Path.GetFileName(raw);
        try
        {
            return Directory.EnumerateFiles(gameDir, byName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { return null; }
    }
}

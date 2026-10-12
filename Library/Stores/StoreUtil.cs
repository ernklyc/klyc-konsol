using Microsoft.Win32;

namespace KlycKonsol.Library.Stores;

/// <summary>Mağaza tarayıcılarının ortak yardımcıları.</summary>
public static class StoreUtil
{
    /// <summary>Başlık eşleştirme anahtarı: küçük harf, harf+rakam dışındakiler atılır (™, ®, :, - vb.).</summary>
    public static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Kısayol adı → mağazadaki gerçek ad (aynı oyunun kopyalarını birleştirmek için).</summary>
    static readonly Dictionary<string, string> Aliases = new()
    {
        ["pal"] = "palworld",
        ["fifa25"] = "easportsfc25",
        ["forza4"] = "forzahorizon4",
        ["cs2"] = "counterstrike2",
        ["gta5"] = "grandtheftautov",
        ["rdr2"] = "reddeadredemption2",
    };

    public static string CanonicalTitle(string title)
    {
        var n = Norm(title);
        return Aliases.TryGetValue(n, out var a) ? a : n;
    }

    public static IEnumerable<string> FixedDriveRoots()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { yield break; }
        foreach (var d in drives)
        {
            bool ok;
            try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
            if (ok) yield return d.RootDirectory.FullName;
        }
    }

    /// <summary>HKLM'de hem 64 hem 32 bit görünümünde bir alt anahtarın çocuklarını gezer (Windows'un yönlendirmesine güvenir).</summary>
    public static IEnumerable<RegistryKey> EnumerateSubKeys(RegistryHive hive, string path)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            RegistryKey? baseKey = null, key = null;
            try
            {
                baseKey = RegistryKey.OpenBaseKey(hive, view);
                key = baseKey.OpenSubKey(path);
            }
            catch { }
            if (key == null) { baseKey?.Dispose(); continue; }

            string[] names;
            try { names = key.GetSubKeyNames(); } catch { names = Array.Empty<string>(); }
            foreach (var n in names)
            {
                RegistryKey? sub = null;
                try { sub = key.OpenSubKey(n); } catch { }
                if (sub != null) yield return sub;
            }
            key.Dispose();
            baseKey?.Dispose();
        }
    }

    public static string? Str(RegistryKey k, string name) => k.GetValue(name) as string;
}

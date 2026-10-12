using System.Runtime.InteropServices;
using System.Text;

namespace KlycKonsol.Library;

/// <summary>Tarayıcı indirmeleri için saf yardımcılar: güvenli dosya adı, çakışmasız yol, boyut metni, İndirilenler klasörü.</summary>
public static class DownloadHelper
{
    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Dosya adından yol/geçersiz karakterleri ve Windows'un ayırdığı adları temizler; boş kalırsa "indirme".</summary>
    public static string Sanitize(string? name)
    {
        var n = Path.GetFileName((name ?? "").Replace('/', '\\'));   // yol parçalarını (..\..\x) at
        var sb = new StringBuilder();
        foreach (var c in n) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || char.IsControl(c) ? '_' : c);
        var s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length > 120)
        {
            var ext = Path.GetExtension(s);
            s = ext.Length is > 0 and <= 10 ? s[..(120 - ext.Length)] + ext : s[..120];
        }
        var stem = Path.GetFileNameWithoutExtension(s);
        if (s.Length == 0 || ReservedNames.Contains(stem)) return "indirme" + (s.Length == 0 ? "" : "_" + s);
        return s;
    }

    /// <summary>Klasörde aynı ad varsa "ad (2).uzantı" üretir; mevcut dosyanın üzerine asla yazmaz.</summary>
    public static string UniquePath(string dir, string fileName)
    {
        var name = Sanitize(fileName);
        var path = Path.Combine(dir, name);
        if (!File.Exists(path) && !File.Exists(path + ".crdownload")) return path;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; i < 1000; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p) && !File.Exists(p + ".crdownload")) return p;
        }
        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 0 => "?",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} MB",
        _ => $"{bytes / 1073741824.0:0.##} GB",
    };

    /// <summary>İndirme ilerlemesi: "%42 · 12,3 MB / 29 MB" (toplam bilinmiyorsa yalnızca indirilen).</summary>
    public static string Progress(long received, long? total)
    {
        if (total is not > 0) return FormatSize(received);
        var pct = (int)Math.Clamp(received * 100 / total.Value, 0, 100);
        return $"%{pct} · {FormatSize(received)} / {FormatSize(total.Value)}";
    }

    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

    /// <summary>Kullanıcının İndirilenler klasörü (taşınmış olsa bile).</summary>
    public static string DownloadsDir()
    {
        // Testler için: KLYC_DOWNLOADS_DIR ile başka klasöre yönlendirilir (gerçek İndirilenler'e dosya düşmesin)
        var over = Environment.GetEnvironmentVariable("KLYC_DOWNLOADS_DIR");
        if (!string.IsNullOrWhiteSpace(over)) { try { Directory.CreateDirectory(over); return over; } catch { } }
        try { if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0 && Directory.Exists(p)) return p; }
        catch { }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try { Directory.CreateDirectory(fallback); } catch { }
        return fallback;
    }
}

using System.Text;

namespace KlycKonsol.Library;

/// <summary>
/// Konsol'un küçük günlüğü (<c>%AppData%\KLYC-Konsol\konsol.log</c>): önemli kararlar (oyun başlatma, Pulse ile güç planı paylaşımı, açılışta kol bekleme, plan geri yükleme).
/// Gerçek kullanımda "ne oldu?" sorusunu sonradan yanıtlamak ve "Sorun bildir" paketine koymak içindir. Kişisel veri yazılmaz (oyun adı ve kimliği dışında);
/// 512 KB'ı geçince eskisi <c>konsol.log.1</c> olur. Hata verirse sessizce yutar, hiçbir akışı bozmaz.
/// </summary>
public static class AppLog
{
    const long MaxBytes = 512 * 1024;
    static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Dir, "konsol.log");

    public static void Write(string area, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Dir);
                var f = new FileInfo(FilePath);
                if (f.Exists && f.Length > MaxBytes) File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{area}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* günlük hiçbir zaman asıl işi bozmamalı */ }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KlycKonsol.Library;

/// <summary>
/// Yeni kurulan / kaldırılan oyunları kendiliğinden fark etmek için: Steam (appmanifest_*.acf), Epic (*.item) ve kısayol klasörlerindeki
/// (.url/.lnk) dosyaların imzasını (ad + boyut + değişim zamanı) hesaplar. Konsol görünürken <see cref="Watcher"/> değişimi anında bildirir;
/// Konsol gizliyken (oyun sırasında) hiçbir şey çalışmaz — geri dönüşte imza bir kez karşılaştırılır.
/// </summary>
public static class LibraryWatch
{
    static readonly string[] Patterns = { "*.acf", "*.item", "*.url", "*.lnk" };

    /// <summary>İzlenecek klasörler (yalnızca var olanlar): Steam kütüphaneleri, Epic manifestleri, kısayol klasörleri.</summary>
    public static List<string> Roots(AppState state, string? steamPathOverride = null)
    {
        var roots = new List<string>();
        try
        {
            var steam = steamPathOverride ?? Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(steam))
            {
                steam = steam.Replace('/', '\\');
                var libs = new List<string> { steam };
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                roots.AddRange(libs.Select(l => Path.Combine(l, "steamapps")));
            }
        }
        catch { }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests"));
        try { roots.AddRange(ShortcutScanner.DefaultFolders().Concat(state.GameFolders)); } catch { }
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Klasörlerdeki ilgili dosyaların kısa özeti; bir dosya eklenir, silinir ya da değişirse imza değişir.</summary>
    public static string Signature(IEnumerable<string> roots)
    {
        var sb = new StringBuilder();
        foreach (var root in roots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var pattern in Patterns)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(); }
                catch { continue; }
                foreach (var f in files)
                {
                    try { var i = new FileInfo(f); sb.Append(f).Append('|').Append(i.Length).Append('|').Append(i.LastWriteTimeUtc.Ticks).Append('\n'); }
                    catch { }
                }
            }
        }
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Dosya olaylarını bekleten (debounce) izleyici; Dispose edilince tamamen durur.</summary>
    public sealed class Watcher : IDisposable
    {
        readonly List<FileSystemWatcher> _watchers = new();
        readonly System.Threading.Timer _timer;
        readonly Action _changed;
        readonly int _debounceMs;
        bool _disposed;

        public Watcher(IEnumerable<string> roots, Action changed, int debounceMs = 4000)
        {
            _changed = changed; _debounceMs = debounceMs;
            _timer = new System.Threading.Timer(_ => { if (!_disposed) _changed(); });
            foreach (var root in roots)
            {
                foreach (var pattern in Patterns)
                {
                    try
                    {
                        var w = new FileSystemWatcher(root, pattern) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, IncludeSubdirectories = false };
                        w.Created += OnEvent; w.Deleted += OnEvent; w.Changed += OnEvent; w.Renamed += OnEvent;
                        w.EnableRaisingEvents = true;
                        _watchers.Add(w);
                    }
                    catch { }
                }
            }
        }

        public int Count => _watchers.Count;

        void OnEvent(object? s, FileSystemEventArgs e) { if (!_disposed) _timer.Change(_debounceMs, Timeout.Infinite); }

        public void Dispose()
        {
            _disposed = true;
            foreach (var w in _watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
            _watchers.Clear();
            _timer.Dispose();
        }
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KlycKonsol.Library;

/// <summary>
/// Basit çeviri: kaynak dil Türkçe (kodda yazılı metinler). Sözlük gömülü <c>Localization/en.tsv</c>'dir (Türkçe &lt;TAB&gt; İngilizce, satır sonu için \n).
/// Dil İngilizceyse <see cref="T"/> sözlükteki karşılığı verir; yoksa Türkçe kalır (yarım çeviri bile bir şeyi bozmaz).
/// Çalışma anında oluşan metinler ("Hades kapandı", "3 dk önce", "Pil: Dolu · Kablolu") için <see cref="Translate"/> kullanılır: sözlükteki
/// "{0} kapandı" gibi şablonlarla eşleştirir, " · " gibi ayraçlarla birleştirilmiş parçaları ayrı ayrı çevirir.
/// </summary>
public static class Loc
{
    static Dictionary<string, string>? _map;
    static List<Template>? _templates;
    static readonly object Gate = new();

    sealed record Template(Regex Rx, int[] Order, string En);

    public static bool IsEnglish { get; private set; }

    /// <summary>"tr", "en" ya da "auto" (Windows Türkçeyse Türkçe, değilse İngilizce).</summary>
    public static void Configure(string? setting, CultureInfo? ui = null)
    {
        var s = (setting ?? "auto").Trim().ToLowerInvariant();
        if (s is not ("tr" or "en")) s = (ui ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "tr" ? "tr" : "en";
        IsEnglish = s == "en";
        lock (Gate) _memo.Clear();
        if (IsEnglish)
        {
            var c = CultureInfo.GetCultureInfo("en-US");   // tarih, ay adı ve ondalık ayracı da İngilizce
            CultureInfo.DefaultThreadCurrentCulture = c; CultureInfo.DefaultThreadCurrentUICulture = c;
            CultureInfo.CurrentCulture = c; CultureInfo.CurrentUICulture = c;
        }
    }

    /// <summary>Sabit metnin İngilizcesi (dil İngilizceyse ve sözlükte varsa); yoksa metin aynen.</summary>
    public static string T(string tr)
    {
        if (!IsEnglish || string.IsNullOrEmpty(tr)) return tr;
        return Map().TryGetValue(tr, out var en) ? en : tr;
    }

    public static string F(string trFormat, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(trFormat), args);

    public static bool Has(string tr) => Map().ContainsKey(tr);
    public static int Count => Map().Count;
    public static IReadOnlyDictionary<string, string> Entries => Map();

    // ---- Çalışma anında oluşan metinler ---------------------------------------------------

    static readonly string[] Separators = { "  ·  ", " · ", "\n", ". ", ": ", ", ", "   " };

    /// <summary>Dil Türkçeyse ya da eşleşme yoksa metin aynen; İngilizceyse sözlük/şablon/parça parça çeviri. Sonuçlar önbelleğe alınır (aynı metin her turda yeniden çözülmesin).</summary>
    public static string Translate(string text)
    {
        if (!IsEnglish || string.IsNullOrWhiteSpace(text)) return text;
        lock (Gate)
        {
            if (_memo.TryGetValue(text, out var hit)) return hit;
        }
        var result = TranslateCore(text, 0) ?? text;
        lock (Gate)
        {
            if (_memo.Count >= MemoLimit) _memo.Clear();   // canlı değişen metinler (sayaçlar) sınırsız büyümesin
            _memo[text] = result;
        }
        return result;
    }

    const int MemoLimit = 4000;
    static readonly Dictionary<string, string> _memo = new(StringComparer.Ordinal);

    static string? TranslateCore(string text, int depth)
    {
        var map = Map();
        if (map.TryGetValue(text, out var direct)) return direct;
        var trimmed = text.Trim();
        if (trimmed.Length != text.Length && map.TryGetValue(trimmed, out var d2)) return text.Replace(trimmed, d2);
        var tpl = FromTemplate(text);
        if (tpl is not null) return tpl;
        if (depth >= 3) return null;
        foreach (var sep in Separators)
        {
            if (!text.Contains(sep, StringComparison.Ordinal)) continue;
            var parts = text.Split(sep);
            if (parts.Length < 2) continue;
            var changed = false;
            var sb = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(sep);
                var part = parts[i];
                var r = part.Length == 0 ? null : TranslateCore(part, depth + 1);
                if (r is not null) { changed = true; sb.Append(r); } else sb.Append(part);
            }
            if (changed) return sb.ToString();
        }
        return null;
    }

    static string? FromTemplate(string text)
    {
        foreach (var t in Templates())
        {
            var m = t.Rx.Match(text);
            if (!m.Success) continue;
            var args = new object?[t.Order.Max() + 1];
            for (var i = 0; i < t.Order.Length; i++)
            {
                var g = m.Groups[i + 1].Value;
                args[t.Order[i]] = TranslateCore(g, 3) ?? g;   // iç metin de çevrilebilir (ör. durum adı)
            }
            try { return string.Format(CultureInfo.CurrentCulture, t.En, args); } catch { return null; }
        }
        return null;
    }

    static List<Template> Templates()
    {
        if (_templates is not null) return _templates;
        lock (Gate)
        {
            if (_templates is not null) return _templates;
            var list = new List<Template>();
            var ph = new Regex(@"\{(\d+)\}");
            foreach (var (tr, en) in Map())
            {
                var ms = ph.Matches(tr);
                if (ms.Count == 0 || ph.Replace(tr, "").Trim().Length < 4) continue;
                var sb = new StringBuilder("^"); var order = new List<int>(); var pos = 0;
                foreach (Match m in ms)
                {
                    sb.Append(Regex.Escape(tr[pos..m.Index])).Append("(.*?)");
                    order.Add(int.Parse(m.Groups[1].Value));
                    pos = m.Index + m.Length;
                }
                sb.Append(Regex.Escape(tr[pos..])).Append('$');
                list.Add(new Template(new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), order.ToArray(), en));
            }
            _templates = list.OrderByDescending(t => t.Rx.ToString().Length).ToList();   // en özgül şablon önce
            return _templates;
        }
    }

    static Dictionary<string, string> Map()
    {
        if (_map is not null) return _map;
        lock (Gate)
        {
            if (_map is not null) return _map;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            var asm = typeof(Loc).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith("en.tsv", StringComparison.Ordinal)))
            {
                using var st = asm.GetManifestResourceStream(name);
                if (st is null) continue;
                using var rd = new StreamReader(st, Encoding.UTF8);
                string? line;
                while ((line = rd.ReadLine()) is not null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    var val = Unescape(line[(tab + 1)..]);
                    if (val.Length > 0) d[Unescape(line[..tab])] = val;
                }
            }
            _map = d;
            return d;
        }
    }

    static string Unescape(string s) => s.Replace("\\n", "\n");
}

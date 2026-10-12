using System.Text.RegularExpressions;
using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

/// <summary>Loc genel durum tutar (dil): bu sınıftaki testler birbirine karışmasın diye tek koleksiyonda sırayla koşar.</summary>
[Collection("Loc")]
public class LocTests : IDisposable
{
    public LocTests() => Loc.Configure("en");
    public void Dispose() => Loc.Configure("tr");

    [Fact]
    public void Dictionary_HasEntries_AndNoEmptyValues()
    {
        Assert.True(Loc.Count > 300);
        foreach (var (k, v) in Loc.Entries) Assert.False(string.IsNullOrWhiteSpace(v), "boş çeviri: " + k);
    }

    [Fact]
    public void Turkish_LeavesTextUntouched()
    {
        Loc.Configure("tr");
        Assert.Equal("Açık", Loc.Translate("Açık"));
        Assert.Equal("Açık", Loc.T("Açık"));
    }

    [Fact]
    public void Auto_FollowsWindowsLanguage()
    {
        Loc.Configure("auto", new System.Globalization.CultureInfo("tr-TR"));
        Assert.False(Loc.IsEnglish);
        Loc.Configure("auto", new System.Globalization.CultureInfo("de-DE"));
        Assert.True(Loc.IsEnglish);
    }

    [Fact]
    public void Exact_Template_AndSeparatorParts_AreTranslated()
    {
        Assert.Equal("On", Loc.Translate("Açık"));
        Assert.Equal("Off", Loc.Translate("Kapalı"));
        Assert.Equal("Açık ve bilinmeyen şey", Loc.Translate("Açık ve bilinmeyen şey"));   // tanınmayan metin bozulmaz
        Assert.Equal("On · Off", Loc.Translate("Açık · Kapalı"));
        Assert.Equal("  On  ", Loc.Translate("  Açık  "));
    }

    [Fact]
    public void Template_FillsRuntimeValues()
    {
        var tr = Loc.Entries.Keys.First(k => k.Contains("{0}") && !k.Contains("{1}") && k.Replace("{0}", "").Trim().Length > 6);
        var sample = tr.Replace("{0}", "Hades");
        var en = Loc.Translate(sample);
        Assert.NotEqual(sample, en);
        Assert.Contains("Hades", en);
    }

    [Fact]
    public void English_UsesEnglishCulture()
    {
        Assert.Equal("en", System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void Localizer_TranslatesTextBlocksButtonsAndRuns_AndLeavesUnknownAlone()
    {
        string? tb = null, btn = null, run = null, unknown = null, pass2 = null;
        var t = new Thread(() =>
        {
            var panel = new System.Windows.Controls.StackPanel();
            var a = new System.Windows.Controls.TextBlock { Text = "Açık" };
            var b = new System.Windows.Controls.Button { Content = "Kapalı" };
            var c = new System.Windows.Controls.TextBlock();
            c.Inlines.Add(new System.Windows.Documents.Run("Açık"));
            c.Inlines.Add(new System.Windows.Documents.Run(" bambaşka bilinmeyen metin"));
            panel.Children.Add(a); panel.Children.Add(b); panel.Children.Add(c);
            // Görsel ağaç (VisualTreeHelper) yerleşim sonrası kurulur
            var win = new System.Windows.Window { Content = panel, Width = 200, Height = 200, ShowInTaskbar = false, WindowStyle = System.Windows.WindowStyle.None, Left = -3000 };
            win.Show();
            var loc = new KlycKonsol.Ui.Localizer(() => win);
            var n = loc.Pass();
            tb = a.Text; btn = (string)b.Content; run = ((System.Windows.Documents.Run)c.Inlines.FirstInline!).Text; unknown = ((System.Windows.Documents.Run)c.Inlines.LastInline!).Text;
            pass2 = loc.Pass().ToString();
            win.Close();
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        Assert.Equal("On", tb);
        Assert.Equal("Off", btn);
        Assert.Equal("On", run);
        Assert.Equal(" bambaşka bilinmeyen metin", unknown);
        Assert.Equal("0", pass2);   // ikinci tur yeni bir şey çevirmez (zaten İngilizce)
    }

    /// <summary>Kaynak koddaki kullanıcıya görünen sabit Türkçe metinlerin sözlükte olduğunu denetler: yeni bir Toast/başlık eklenip çevirisi unutulursa burada kırılır.</summary>
    [Fact]
    public void EveryVisibleSourceLiteral_HasATranslation()
    {
        var root = FindRoot();
        if (root is null) return;   // kaynak ağacı yoksa (paketten çalışıyorsa) atla
        bool Ours(string f) { var p = f.Replace('\\', '/'); return !p.Contains("/obj/") && !p.Contains("/bin/") && !p.Contains("/tests/"); }
        var csFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(Ours);
        var xamlFiles = Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories).Where(Ours);
        var tr = new Regex("[çğıöşüÇĞİÖŞÜ]");
        var lit = new Regex("\\$?\"((?:[^\"\\\\]|\\\\.)*)\"");
        // Türkçe harf içermese de kullanıcıya gösterilen sabitler: doğrudan Text = "..", Toast(".."), Confirm("..") vb.
        var shown = new Regex(@"(?:\b(?:Text|Title|Caption|Right|Content|Header|Label|ToolTip)\s*=\s*|Toast\(|Confirm\()(\$?""(?:[^""\\]|\\.)*"")");
        var xamlAttr = new Regex(@"\b(?:Text|Content|ToolTip|Header|Title|Caption)=""([^""{][^""]*)""");
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        void Check(string file, string s, bool interp)
        {
            if (s.Length < 2 || s.StartsWith("http") || s.StartsWith("pack://")) return;
            var key = ToKey(s, interp);
            if (key is null || !Regex.IsMatch(key, "[A-Za-zÇĞİÖŞÜçğıöşü]{2,}")) return;
            if (!Loc.Has(key) && Loc.Translate(key) == key) missing.Add(Path.GetFileName(file) + ": " + key);
        }
        foreach (var f in csFiles)
        {
            foreach (var raw in File.ReadAllLines(f))
            {
                var ls = raw.TrimStart();
                if (ls.StartsWith("//") || ls.StartsWith("*") || raw.Contains("AppLog.") || raw.Contains("Journal.") || raw.Contains("Console.")) continue;
                var seen = new HashSet<string>();
                foreach (Match m in lit.Matches(raw))
                    if (tr.IsMatch(m.Groups[1].Value) && seen.Add(m.Value)) Check(f, m.Groups[1].Value, m.Value.StartsWith('$'));
                foreach (Match m in shown.Matches(raw))
                {
                    var l = lit.Match(m.Groups[1].Value);
                    if (l.Success && seen.Add(l.Value)) Check(f, l.Groups[1].Value, l.Value.StartsWith('$'));
                }
            }
        }
        foreach (var f in xamlFiles)
            foreach (var raw in File.ReadAllLines(f))
                foreach (Match m in xamlAttr.Matches(raw)) Check(f, m.Groups[1].Value.Replace("&quot;", "\""), false);
        // Çeviri istemeyen, Türkçe harf içeren ama kullanıcıya gösterilmeyen sabitler (anahtarlar, günlük, yol adları) bu listede
        missing.RemoveWhere(m => Allowed.Any(a => m.Contains(a, StringComparison.Ordinal)));
        Assert.True(missing.Count == 0, "Çevirisi eksik metinler (" + missing.Count + "):\n" + string.Join("\n", missing.Take(80)));
    }

    // Kullanıcıya İngilizce gösterilmesi gerekmeyen sabitler (ör. markalar, dosya adları, günlük satırları)
    static readonly string[] Allowed = { "KLYC", "KONSOL", "Dil / Language", "Language changes", ": Türkçe", ": English", ": Bluetooth", ": Start", ": görev", ": işlem", "Ctrl + Alt + K", "buymeacoffee", "klyc-konsol/issues", ": &#x", ": LB", ": RB", "Wi‑Fi", "PerfMeter.cs", "TrayIcon.cs", "WifiService.cs", "SupportBundle.cs", "ShowPage(", "tuş {0}", "güç planını yönetecek", "asdfghjkl", "qwertyuı", "zxcvbnm" };

    static string? ToKey(string body, bool interpolated)
    {
        if (body.StartsWith("@")) return null;
        body = body.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\'", "'");
        if (!interpolated) return body;
        var n = 0;
        body = Regex.Replace(body, @"(?<!\{)\{(?!\{)[^{}]*(?:\([^()]*\)[^{}]*)*\}(?!\})", _ => "{" + n++ + "}");
        return body.Replace("{{", "{").Replace("}}", "}");
    }

    static string? FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "KlycKonsol.csproj"))) d = d.Parent;
        return d?.FullName;
    }
}

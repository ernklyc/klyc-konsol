using KlycKonsol.Library;

namespace KlycKonsol.Pages;

/// <summary>Tarayıcının açılış sayfası: büyük, controller ile tıklanabilir hızlı bağlantılar.</summary>
static class StartPage
{
    record Link(string Name, string Url, string Color, string Letter);

    static readonly Link[] Links =
    {
        new("YouTube", "https://www.youtube.com/", "#E5453B", "▶"),
        new("Google", "https://www.google.com/", "#4C7BE0", "G"),
        new("Twitch", "https://www.twitch.tv/", "#8A5CF0", "T"),
        new("Netflix", "https://www.netflix.com/", "#C8202D", "N"),
        new("Disney+", "https://www.disneyplus.com/", "#2A52C9", "D"),
        new("Prime Video", "https://www.primevideo.com/", "#2A9FD8", "P"),
        new("Spotify", "https://open.spotify.com/", "#2FA84F", "S"),
        new("Reddit", "https://www.reddit.com/", "#E8632B", "R"),
        new("X", "https://x.com/", "#303644", "X"),
        new("Steam Mağaza", "https://store.steampowered.com/", "#2B4A6B", "S"),
        new("Epic Games", "https://store.epicgames.com/", "#34373F", "E"),
        new("Vikipedi", "https://tr.wikipedia.org/", "#59606E", "W"),
    };

    public static string Html(IReadOnlyList<KlycKonsol.Library.Bookmark> bookmarks)
    {
        {
            string Enc(string v) => System.Net.WebUtility.HtmlEncode(v);
            var marks = string.Concat(bookmarks.Select(b =>
                $"<a class='tile' href='{Enc(b.Url)}' style='--c:#6178AD'><span class='ic'>★</span><span class='nm'>{Enc(b.Title)}</span></a>"));
            var tiles = string.Concat(Links.Select(l =>
                $"<a class='tile' href='{(Loc.IsEnglish ? l.Url.Replace("tr.wikipedia", "en.wikipedia") : l.Url)}' style='--c:{l.Color}'><span class='ic'>{l.Letter}</span><span class='nm'>{Loc.T(l.Name)}</span></a>"));
            var markSection = marks.Length == 0 ? "" : $"<h2>{Loc.T("Yer imleri")}</h2><div class='grid'>{marks}</div><h2>{Loc.T("Hızlı bağlantılar")}</h2>";
            var lang = Loc.IsEnglish ? "en" : "tr";
            var hint = Enc(Loc.T("Sol çubukla imleci gez, A ile tıkla · Y: adres/arama · X: yazı kutusuna yaz"));
            var search = Enc(Loc.T("Google'da ara…"));
            var title = Loc.T("Tarayıcı");
            return $$"""
<!doctype html><html lang="{{lang}}"><head><meta charset="utf-8"><title>KLYC {{title}}</title>
<style>
  *{box-sizing:border-box}
  html,body{margin:0;height:100%}
  body{background:radial-gradient(900px 500px at 0 0,#6178ad33,transparent),radial-gradient(1000px 700px at 100% 100%,#212b4d66,transparent),#0b0d11;
       color:#f2f5fa;font-family:'Segoe UI Variable Display','Segoe UI',sans-serif;padding:30px 40px}
  h1{font-size:34px;margin:0 0 4px;font-weight:800}
  h1 b{color:#6178ad}
  h2{font-size:18px;color:#a9b4c8;margin:6px 0 12px;font-weight:600}
  p{color:#a9b4c8;font-size:16px;margin:0 0 20px}
  form{margin:0 0 24px}
  input{width:100%;height:56px;border-radius:28px;border:1.5px solid #ffffff2e;background:#ffffff14;color:#f2f5fa;font-size:21px;padding:0 26px;outline:none}
  input:focus{border-color:#fff;background:#ffffff22}
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:16px}
  .tile{display:flex;align-items:center;gap:18px;height:84px;padding:0 20px;border-radius:22px;text-decoration:none;color:#f2f5fa;
        background:linear-gradient(135deg,color-mix(in srgb,var(--c) 55%,#0b0d11),color-mix(in srgb,var(--c) 22%,#0b0d11));border:1.5px solid #ffffff1f;transition:transform .15s,border-color .15s}
  .tile:hover{transform:scale(1.05);border-color:#fff}
  .ic{width:46px;height:46px;border-radius:14px;background:#ffffff26;display:flex;align-items:center;justify-content:center;font-size:22px;font-weight:800}
  .nm{font-size:20px;font-weight:600}
</style></head><body>
<h1>KLYC <b>{{title}}</b></h1>
<p>{{hint}}</p>
<form action="https://www.google.com/search" method="get"><input name="q" placeholder="{{search}}" autocomplete="off"></form>
{{markSection}}<div class="grid">{{tiles}}</div>
</body></html>
""";
        }
    }
}

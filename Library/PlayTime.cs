namespace KlycKonsol.Library;

public static class PlayTime
{
    /// <summary>Saniyeyi "12 sa 30 dk" / "45 dk" / "1 dk'dan az" olarak yazar (İngilizcede "12 h 30 min"); 0 ise boş.</summary>
    public static string Format(long seconds)
    {
        if (seconds <= 0) return "";
        if (seconds < 60) return Loc.T("1 dk'dan az");
        var minutes = seconds / 60;
        var dk = Loc.T("dk");
        if (minutes < 60) return $"{minutes} {dk}";
        var h = minutes / 60;
        var m = minutes % 60;
        var sa = Loc.T("sa");
        return h >= 100 || m == 0 ? $"{h} {sa}" : $"{h} {sa} {m} {dk}";
    }
}

namespace KlycKonsol.Library;

/// <summary>Kullanıcının adlandırdığı oyun grubu (ör. "Co-op", "Çocuklar için"). Kimlikler oyunun Game.Id'sidir.</summary>
public sealed class GameCollection
{
    public string Name { get; set; } = "";
    public List<string> Ids { get; set; } = new();
}

/// <summary>Koleksiyonlar için saf mantık (durum nesnesi üzerinde): ad kuralları, ekle/çıkar, boş koleksiyonun kaldırılması.</summary>
public static class Collections
{
    public const int MaxCollections = 8;
    public const int MaxNameLength = 20;

    /// <summary>Boşlukları kırpar, tekrarlı boşlukları teke indirir ve uzunluğu sınırlar; boş kalırsa null.</summary>
    public static string? CleanName(string? raw)
    {
        var s = string.Join(' ', (raw ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (s.Length > MaxNameLength) s = s[..MaxNameLength].TrimEnd();
        return s.Length == 0 || s.Any(char.IsControl) ? null : s;
    }

    public static GameCollection? Find(IEnumerable<GameCollection> all, string? name) =>
        all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase));

    public static bool Contains(IEnumerable<GameCollection> all, string name, string gameId) =>
        Find(all, name)?.Ids.Contains(gameId) == true;

    /// <summary>Oyunu koleksiyona ekler (yoksa koleksiyonu oluşturur); zaten içindeyse çıkarır, koleksiyon boşalırsa kaldırır. Dönüş: (eklendi mi, sonuç adı) — sınır/ad hatasında null.</summary>
    public static (bool Added, string Name)? Toggle(List<GameCollection> all, string rawName, string gameId)
    {
        var name = CleanName(rawName);
        if (name is null || string.IsNullOrEmpty(gameId)) return null;
        var c = Find(all, name);
        if (c is null)
        {
            if (all.Count >= MaxCollections) return null;
            c = new GameCollection { Name = name };
            all.Add(c);
        }
        if (c.Ids.Remove(gameId))
        {
            if (c.Ids.Count == 0) all.Remove(c);
            return (false, c.Name);
        }
        c.Ids.Add(gameId);
        return (true, c.Name);
    }

    /// <summary>Koleksiyondaki oyunlar (kütüphanede olmayanlar yok sayılır), koleksiyona eklenme sırasıyla.</summary>
    public static List<T> Members<T>(GameCollection? c, IEnumerable<T> games, Func<T, string> id)
    {
        if (c is null) return new List<T>();
        var byId = games.GroupBy(id).ToDictionary(g => g.Key, g => g.First());
        return c.Ids.Where(byId.ContainsKey).Select(i => byId[i]).ToList();
    }
}

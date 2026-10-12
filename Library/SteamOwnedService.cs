using KlycKonsol.Models;

namespace KlycKonsol.Library;

/// <summary>
/// "Yüklü değil" listesi: Steam hesabında oynanmış (yerel kayıtta oynama süresi / son oynama olan) ama şu an kurulu olmayan oyunlar.
/// Adlar ve kol desteği mağaza API'sinden alınıp state.json'da önbelleğe alınır. Başlatma = steam://install/&lt;id&gt;.
/// </summary>
public static class SteamOwnedService
{
    static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(1300);
    const int MaxPerSession = 90;

    /// <summary>Önbellekteki bilgilerden kurulu olmayan oyun nesneleri (internet gerekmez).</summary>
    public static List<Game> BuildFromCache(AppState state, IEnumerable<Game> installed, IReadOnlyDictionary<string, SteamLocal.AppUsage> usage)
    {
        var installedIds = installed.Select(g => g.SteamAppId).Where(x => x != null).ToHashSet();
        var result = new List<Game>();
        foreach (var (appId, info) in state.SteamOwned)
        {
            if (info.Type != "game" || string.IsNullOrWhiteSpace(info.Name)) continue;
            if (installedIds.Contains(appId)) continue;

            var id = $"steamowned:{appId}";
            usage.TryGetValue(appId, out var u);
            var g = new Game
            {
                Title = info.Name,
                Source = "Steam",
                Target = $"steam://install/{appId}",
                Id = id,
                SteamAppId = appId,
                Availability = GameAvailability.NotInstalled,
                Accent = Palette.For(info.Name),
                PlaySeconds = (u?.PlaytimeMinutes ?? 0) * 60,
                LastPlayed = u?.LastPlayed ?? 0,
            };
            if (state.Support.TryGetValue(id, out var sv)) g.Support = ControllerSupportService.ParseName(sv);
            result.Add(g);
        }
        ArtService.ApplyCached(result);
        return result;
    }

    /// <summary>Bilinmeyen oyunları (oynama süresine göre) sırayla mağazadan sorgular.</summary>
    public static async Task ResolveAsync(AppState state, IEnumerable<Game> installed, IReadOnlyDictionary<string, SteamLocal.AppUsage> usage,
        Action onChanged, CancellationToken ct)
    {
        var installedIds = installed.Select(g => g.SteamAppId).Where(x => x != null).ToHashSet();
        var todo = usage.Values
            .Where(u => (u.PlaytimeMinutes > 0 || u.LastPlayed > 0) && !installedIds.Contains(u.AppId) && !state.SteamOwned.ContainsKey(u.AppId))
            .OrderByDescending(u => u.PlaytimeMinutes)
            .Take(MaxPerSession)
            .ToList();

        foreach (var u in todo)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var basic = await ControllerSupportService.QueryBasicAsync(u.AppId, ct);
                if (basic != null)
                {
                    state.SteamOwned[u.AppId] = new OwnedInfo(basic.Name, basic.Type);
                    if (basic.Type == "game") ControllerSupportService.RecordSupport($"steamowned:{u.AppId}", basic.Support, state);
                    state.Save();
                    System.Windows.Application.Current.Dispatcher.Invoke(onChanged);
                }
            }
            catch (OperationCanceledException) { return; }
            catch { }

            try { await Task.Delay(Gap, ct); } catch (OperationCanceledException) { return; }
        }
    }
}

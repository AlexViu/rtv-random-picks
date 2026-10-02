using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RtvRandomPicks;

/// <summary>Reads a Workshop Collection from the Steam Web API (no key needed), with a disk cache.</summary>
public class WorkshopService
{
    private const string ApiBase = "https://api.steampowered.com/ISteamRemoteStorage/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ILogger _logger;

    public WorkshopService(ILogger logger) => _logger = logger;

    private record CacheFile(string CollectionId, DateTime FetchedAt, Dictionary<string, MapInfo> Maps);

    public async Task<Dictionary<string, MapInfo>> GetMapsAsync(string collectionId, string cachePath, int cacheHours)
    {
        var cache = LoadCache(cachePath);
        bool sameCollection = cache?.CollectionId == collectionId;
        if (sameCollection && (DateTime.UtcNow - cache!.FetchedAt).TotalHours <= cacheHours)
            return cache.Maps;

        try
        {
            var maps = await FetchAsync(collectionId);
            _logger.LogInformation("[RTV] Fetched {Count} maps from workshop collection {Id}.", maps.Count, collectionId);
            if (maps.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllText(cachePath, JsonSerializer.Serialize(new CacheFile(collectionId, DateTime.UtcNow, maps)));
                return maps;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("[RTV] Error fetching workshop collection: {Error}", ex.Message);
        }

        // Steam unreachable: a stale cache is better than no maps
        return sameCollection ? cache!.Maps : new();
    }

    private static async Task<Dictionary<string, MapInfo>> FetchAsync(string collectionId)
    {
        using var collection = await PostAsync("GetCollectionDetails/v1/", new()
        {
            ["collectioncount"] = "1",
            ["publishedfileids[0]"] = collectionId
        });

        var details = collection.RootElement.GetProperty("response").GetProperty("collectiondetails")[0];
        if (!details.TryGetProperty("children", out var children)) return new();

        var ids = children.EnumerateArray().Select(c => c.GetProperty("publishedfileid").GetString()!).ToList();
        if (ids.Count == 0) return new();

        var fields = new Dictionary<string, string> { ["itemcount"] = ids.Count.ToString() };
        for (int i = 0; i < ids.Count; i++)
            fields[$"publishedfileids[{i}]"] = ids[i];

        using var files = await PostAsync("GetPublishedFileDetails/v1/", fields);

        var maps = new Dictionary<string, MapInfo>();
        foreach (var file in files.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
        {
            // Removed or hidden items come back without a title
            if (!file.TryGetProperty("title", out var title)) continue;
            string id = file.GetProperty("publishedfileid").GetString()!;
            maps[id] = new MapInfo { WS = true, Display = title.GetString() ?? id, MapId = id };
        }
        return maps;
    }

    private static async Task<JsonDocument> PostAsync(string method, Dictionary<string, string> fields)
    {
        var resp = await Http.PostAsync(ApiBase + method, new FormUrlEncodedContent(fields));
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    private static CacheFile? LoadCache(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }
}

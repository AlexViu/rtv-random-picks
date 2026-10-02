using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace RtvRandomPicks;

public class MapService
{
    private readonly ILogger _logger;
    private Dictionary<string, MapInfo> _maps = new(StringComparer.OrdinalIgnoreCase);

    public MapService(ILogger logger) => _logger = logger;

    public IReadOnlyDictionary<string, MapInfo> Maps => _maps;

    /// <summary>Loads the static map list. A missing file is fine when a Workshop Collection is used.</summary>
    public void Load(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, MapInfo>>(File.ReadAllText(filePath));
            if (parsed == null) return;

            _maps = new Dictionary<string, MapInfo>(parsed, StringComparer.OrdinalIgnoreCase);
            _logger.LogInformation("[RTV] {Count} maps loaded from {File}.", _maps.Count, Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError("[RTV] Error reading map file: {Error}", ex.Message);
        }
    }

    /// <summary>Adds workshop maps (keyed by workshop id). Entries from the static file win.</summary>
    public void MergeWorkshopMaps(Dictionary<string, MapInfo> workshopMaps)
    {
        var staticIds = _maps.Values.Select(m => m.MapId).Where(id => id != "").ToHashSet();
        foreach (var kv in workshopMaps)
            if (!staticIds.Contains(kv.Key))
                _maps.TryAdd(kv.Key, kv.Value);
    }

    public string DisplayName(string key) =>
        _maps.TryGetValue(key, out var info) && info.Display != "" ? info.Display : key;

    public void ChangeMap(string key)
    {
        if (!_maps.TryGetValue(key, out var info))
        {
            _logger.LogError("[RTV] Map '{Map}' not found in the list.", key);
            return;
        }

        if (!info.WS)
            Server.ExecuteCommand($"changelevel {key}");
        else if (info.MapId != "")
            Server.ExecuteCommand($"host_workshop_map {info.MapId}");
        else
            Server.ExecuteCommand($"ds_workshop_changelevel {key}");
    }
}

using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace RtvRandomPicks;

public class RtvConfig : BasePluginConfig
{
    /// <summary>Fraction of players that must type !rtv to start the vote.</summary>
    [JsonPropertyName("RtvThreshold")]
    public float RtvThreshold { get; set; } = 0.6f;

    /// <summary>Seconds after map start (and after a failed vote) before RTV is allowed.</summary>
    [JsonPropertyName("RtvDelaySeconds")]
    public int RtvDelaySeconds { get; set; } = 90;

    [JsonPropertyName("VoteSeconds")]
    public int VoteSeconds { get; set; } = 30;

    /// <summary>Number of maps in the vote (nominations first, the rest picked at random).</summary>
    [JsonPropertyName("MapsInVote")]
    public int MapsInVote { get; set; } = 5;

    /// <summary>How many previously played maps are kept out of the random picks.</summary>
    [JsonPropertyName("ExcludeRecentMaps")]
    public int ExcludeRecentMaps { get; set; } = 3;

    /// <summary>Adds a "Don't change" option to RTV votes.</summary>
    [JsonPropertyName("DontChangeOption")]
    public bool DontChangeOption { get; set; } = true;

    /// <summary>Seconds before mp_timelimit runs out to start the end-of-map vote. 0 disables it.</summary>
    [JsonPropertyName("EndVoteSecondsBeforeEnd")]
    public int EndVoteSecondsBeforeEnd { get; set; } = 120;

    /// <summary>Minutes added by the "Extend map" option of the end-of-map vote. 0 removes the option.</summary>
    [JsonPropertyName("ExtendMinutes")]
    public int ExtendMinutes { get; set; } = 10;

    /// <summary>How many times a map can be extended.</summary>
    [JsonPropertyName("MaxExtends")]
    public int MaxExtends { get; set; } = 2;

    /// <summary>Static map list, relative to this plugin's config folder.</summary>
    [JsonPropertyName("MapsFile")]
    public string MapsFile { get; set; } = "rtv_maps.json";

    /// <summary>Workshop Collection to pull maps from. Empty = read host_workshop_collection.</summary>
    [JsonPropertyName("WorkshopCollectionId")]
    public string WorkshopCollectionId { get; set; } = "";

    [JsonPropertyName("WorkshopCacheHours")]
    public int WorkshopCacheHours { get; set; } = 24;

    /// <summary>Maps left out of RTV, nominations and votes (map name, display name or workshop id),
    /// e.g. collection maps that should only be played when an admin changes to them.</summary>
    [JsonPropertyName("ExcludedMaps")]
    public List<string> ExcludedMaps { get; set; } = [];
}

using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace RtvRandomPicks;

public class RtvPlugin : BasePlugin, IPluginConfig<RtvConfig>
{
    public override string ModuleName => "RtvRandomPicks";
    public override string ModuleVersion => "1.0.1";
    public override string ModuleDescription => "Classic Rock The Vote: random map picks, nominations and end-of-map vote";

    public RtvConfig Config { get; set; } = new();

    // Vote option that keeps the current map: "Don't change" (RTV) or "Extend map" (end-of-map vote)
    private const string StayKey = "#stay";

    private static string Prefix => $" {ChatColors.Green}[RTV]{ChatColors.Default}";

    private MapService _maps = null!;
    private WorkshopService _workshop = null!;
    private readonly WasdMenuManager _menu = new();

    private readonly HashSet<int> _rtvVoters = new();
    private readonly Dictionary<int, string> _nominations = new(); // player slot -> map key

    private bool _voteInProgress;
    private bool _voteIsEndOfMap;
    private readonly Dictionary<string, int> _votes = new();
    private readonly HashSet<int> _voted = new();
    private Timer? _voteTimer;

    // Seconds since this map started. Also counts on an empty server, so the map still
    // changes when its timelimit runs out (see OnSecond).
    private int _elapsed;
    private int _rtvAllowedAt;
    private bool _endVoteDone;
    private int _extends;
    private string? _nextMap;   // winner of the end-of-map vote, applied at round end
    private bool _changing;

    // Maps we changed to, oldest first; the last one is the current map. Survives map changes.
    private readonly List<string> _recent = new();

    public void OnConfigParsed(RtvConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        _maps = new MapService(Logger);
        _workshop = new WorkshopService(Logger);

        RegisterListener<Listeners.OnMapStart>(_ => StartMap(Config.RtvDelaySeconds));
        RegisterListener<Listeners.OnClientDisconnectPost>(OnClientDisconnect);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        AddCommandListener("say", OnSay);
        AddCommandListener("say_team", OnSay);

        if (hotReload)
        {
            // OnAllPluginsLoaded doesn't fire on hot reload
            RegisterListener<Listeners.OnTick>(_menu.OnTick);
            StartMap(rtvDelay: 0);
        }
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        // Registered after every plugin has loaded so the menu's OnTick runs last and its
        // PrintToCenterHtml isn't overwritten by other center-screen HUDs (e.g. SharpTimer)
        if (!hotReload) RegisterListener<Listeners.OnTick>(_menu.OnTick);
    }

    public override void Unload(bool hotReload) => _menu.CloseAll();

    private void StartMap(int rtvDelay)
    {
        _rtvVoters.Clear();
        _nominations.Clear();
        _votes.Clear();
        _voted.Clear();
        _voteInProgress = false;
        _voteTimer = null;
        _elapsed = 0;
        _rtvAllowedAt = rtvDelay;
        _endVoteDone = false;
        _extends = 0;
        _nextMap = null;
        _changing = false;

        _maps.Load(Path.Combine(ConfigDirectory, Config.MapsFile));
        AddTimer(3f, FetchWorkshopMaps, TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(1f, OnSecond, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    // ── Events ────────────────────────────────────────────────────────────────

    private void OnSecond()
    {
        // Counts on an empty server too. CS2 keeps its own mp_timelimit clock running while the
        // server is empty, and when it runs out it ends the map with an empty "changelevel" for
        // workshop maps, leaving the server on a map that never loads. Changing to a random map
        // ourselves when the time is up gets there first.
        _elapsed++;

        if (_changing || _voteInProgress) return;

        float limit = TimeLimitSeconds();
        if (limit <= 0) return;

        float remaining = limit - _elapsed;
        if (remaining <= 0)
            ChangeOnTimeUp();
        else if (!_endVoteDone && Config.EndVoteSecondsBeforeEnd > 0 && Humans().Any() &&
                 remaining <= Math.Max(Config.EndVoteSecondsBeforeEnd, Config.VoteSeconds + 10))
            StartVote(endOfMap: true);
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (_nextMap != null) ScheduleChange(_nextMap, 3f);
        return HookResult.Continue;
    }

    private void OnClientDisconnect(int slot)
    {
        _menu.Remove(slot);
        _nominations.Remove(slot);

        // Fewer players can mean the remaining RTV votes now reach the threshold
        if (_rtvVoters.Remove(slot) && _rtvVoters.Count > 0)
            Server.NextFrame(CheckRtvThreshold);
    }

    // "rtv", "nominate" and "timeleft" also work without the "!" prefix, as in SourceMod
    private HookResult OnSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return HookResult.Continue;

        switch (info.GetArg(1).Trim().ToLowerInvariant())
        {
            case "rtv": Rtv(player); break;
            case "nominate": Nominate(player, ""); break;
            case "timeleft": Timeleft(player); break;
        }
        return HookResult.Continue;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [ConsoleCommand("css_rtv", "Vote to change the map")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRtvCommand(CCSPlayerController? caller, CommandInfo _) => Rtv(caller!);

    [ConsoleCommand("css_nominate", "Nominate a map for the next vote")]
    [CommandHelper(usage: "[map name]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNominateCommand(CCSPlayerController? caller, CommandInfo info) =>
        Nominate(caller!, info.ArgCount > 1 ? info.GetArg(1) : "");

    [ConsoleCommand("css_timeleft", "Show remaining time on the current map")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTimeleftCommand(CCSPlayerController? caller, CommandInfo _) => Timeleft(caller!);

    [ConsoleCommand("css_nomlist", "Show current map nominations")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNomlistCommand(CCSPlayerController? caller, CommandInfo _)
    {
        var maps = _nominations.Values.Distinct().ToList();
        if (maps.Count == 0)
        {
            PrintToPlayer(caller!, "nomlist.empty");
            return;
        }

        PrintToPlayer(caller!, "nomlist.header");
        foreach (var key in maps)
            caller!.PrintToChat($"  {ChatColors.Yellow}{_maps.DisplayName(key)}");
    }

    [ConsoleCommand("css_forcertv", "Force a map vote")]
    [RequiresPermissions("@css/changemap")]
    public void OnForceRtvCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (_voteInProgress || _changing)
        {
            info.ReplyToCommand("[RTV] A vote or map change is already in progress.");
            return;
        }

        PrintToAll("rtv.forced", caller?.PlayerName ?? "Console");
        StartVote(endOfMap: false);
    }

    // ── RTV ───────────────────────────────────────────────────────────────────

    private void Rtv(CCSPlayerController player)
    {
        if (_changing || _elapsed < _rtvAllowedAt)
        {
            PrintToPlayer(player, "rtv.not_available");
            return;
        }
        if (_voteInProgress)
        {
            PrintToPlayer(player, "rtv.vote_in_progress");
            return;
        }
        if (!_rtvVoters.Add(player.Slot))
        {
            PrintToPlayer(player, "rtv.already_voted", _rtvVoters.Count, RtvVotesRequired());
            return;
        }

        PrintToAll("rtv.wants_change", player.PlayerName, _rtvVoters.Count, RtvVotesRequired());
        CheckRtvThreshold();
    }

    private int RtvVotesRequired() =>
        Math.Max(1, (int)Math.Ceiling(Humans().Count() * Config.RtvThreshold));

    private void CheckRtvThreshold()
    {
        if (_voteInProgress || _changing || _rtvVoters.Count < RtvVotesRequired()) return;

        // The end-of-map vote already picked the next map: go there right away
        if (_nextMap != null)
            ScheduleChange(_nextMap, 0f);
        else
            StartVote(endOfMap: false);
    }

    // ── Nominations ───────────────────────────────────────────────────────────

    private void Nominate(CCSPlayerController player, string filter)
    {
        if (_voteInProgress || _changing || _nextMap != null)
        {
            PrintToPlayer(player, "nominate.unavailable");
            return;
        }

        var matches = _maps.Maps.Keys
            .Where(k => !IsCurrentMap(k) &&
                        (k.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                         _maps.DisplayName(k).Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(_maps.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
        {
            if (filter == "")
                PrintToPlayer(player, "vote.no_maps");
            else
                PrintToPlayer(player, "nominate.not_found", filter);
            return;
        }
        if (matches.Count == 1 && filter != "")
        {
            AddNomination(player, matches[0]);
            return;
        }

        var menu = new WasdMenu(Localize(player, "nominate.menu_title"));
        foreach (var key in matches)
        {
            menu.Add(_maps.DisplayName(key), (p, _) =>
            {
                _menu.Close(p);
                AddNomination(p, key);
            });
        }
        _menu.Open(player, menu, freeze: true);
    }

    private void AddNomination(CCSPlayerController player, string key)
    {
        if (_voteInProgress || _changing || _nextMap != null) return;

        string name = _maps.DisplayName(key);
        if (_nominations.Any(n => n.Value == key))
        {
            PrintToPlayer(player, "nominate.already", name);
            return;
        }

        bool isChange = _nominations.ContainsKey(player.Slot);
        _nominations[player.Slot] = key;
        PrintToAll(isChange ? "nominate.changed" : "nominate.success", player.PlayerName, name);
    }

    // ── Map vote ──────────────────────────────────────────────────────────────

    private void StartVote(bool endOfMap)
    {
        if (_voteInProgress || _changing) return;

        var candidates = PickCandidates();
        if (candidates.Count == 0)
        {
            PrintToAll("vote.no_maps");
            // Otherwise the end-of-map check would retry (and print this) every second
            if (endOfMap) _endVoteDone = true;
            return;
        }

        _voteInProgress = true;
        _voteIsEndOfMap = endOfMap;
        if (endOfMap) _endVoteDone = true;
        _votes.Clear();
        _voted.Clear();

        var menu = new WasdMenu(Localizer["vote.menu_title"]);
        foreach (var key in candidates)
            AddVoteOption(menu, key, _maps.DisplayName(key));

        if (endOfMap && Config.ExtendMinutes > 0 && _extends < Config.MaxExtends)
            AddVoteOption(menu, StayKey, Localizer["vote.option_extend", Config.ExtendMinutes]);
        else if (!endOfMap && Config.DontChangeOption)
            AddVoteOption(menu, StayKey, Localizer["vote.option_dont_change"]);

        PrintToAll(endOfMap ? "vote.started_end_of_map" : "vote.started", Config.VoteSeconds);
        // Not frozen: the vote pops up mid-round and nobody asked for it
        foreach (var player in Humans())
            _menu.Open(player, menu, freeze: false);

        _voteTimer = AddTimer(Config.VoteSeconds, EndVote, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void AddVoteOption(WasdMenu menu, string key, string label)
    {
        _votes[key] = 0;
        menu.Add(label, (player, option) =>
        {
            if (!_voteInProgress || !_voted.Add(player.Slot)) return;

            option.Count = ++_votes[key];
            _menu.Close(player);
            _menu.RefreshAll();
            PrintToPlayer(player, "vote.voted_for", label);

            // Everyone has voted: no point in waiting for the timer
            if (Humans().All(p => _voted.Contains(p.Slot)))
            {
                _voteTimer?.Kill();
                EndVote();
            }
        });
    }

    /// <summary>Nominated maps first, then random picks that avoid the recently played maps.</summary>
    private List<string> PickCandidates()
    {
        var picks = _nominations.Values
            .Distinct()
            .Where(k => _maps.Maps.ContainsKey(k) && !IsCurrentMap(k))
            .Take(Config.MapsInVote)
            .ToList();

        int needed = Config.MapsInVote - picks.Count;
        var pool = _maps.Maps.Keys.Where(k => !picks.Contains(k) && !IsCurrentMap(k)).ToList();

        // A small map list can't afford to skip recent maps
        var fresh = pool.Where(k => !_recent.Contains(k)).ToList();
        if (fresh.Count >= needed) pool = fresh;

        picks.AddRange(pool.OrderBy(_ => Random.Shared.Next()).Take(needed));
        return picks;
    }

    private void EndVote()
    {
        if (!_voteInProgress) return;
        _voteInProgress = false;
        _voteTimer = null;
        _menu.CloseAll();
        _rtvVoters.Clear();

        int total = _votes.Values.Sum();
        int best = _votes.Values.Max();
        if (total == 0)
        {
            // End-of-map vote: a random map is picked when the time runs out
            PrintToAll("vote.nobody_voted");
            _rtvAllowedAt = _elapsed + Config.RtvDelaySeconds;
            return;
        }

        // Ties are broken at random
        var tied = _votes.Where(v => v.Value == best).Select(v => v.Key).ToList();
        string winner = tied[Random.Shared.Next(tied.Count)];
        int percent = best * 100 / total;

        if (winner == StayKey)
        {
            if (_voteIsEndOfMap)
            {
                ExtendMap();
                PrintToAll("vote.extended", Config.ExtendMinutes, percent, total);
            }
            else
            {
                PrintToAll("vote.dont_change", percent, total);
            }
            _rtvAllowedAt = _elapsed + Config.RtvDelaySeconds;
            return;
        }

        PrintToAll("vote.finished", _maps.DisplayName(winner), percent, total);
        if (_voteIsEndOfMap)
            _nextMap = winner;
        else
            ScheduleChange(winner, 0f);
    }

    private void ExtendMap()
    {
        _extends++;
        _endVoteDone = false;
        SetTimeLimit(TimeLimitSeconds() / 60f + Config.ExtendMinutes);
    }

    // ── Map change ────────────────────────────────────────────────────────────

    private void ChangeOnTimeUp()
    {
        string? target = _nextMap;
        if (target == null)
        {
            target = PickCandidates().LastOrDefault(); // last = a random pick, not a nomination
            if (target == null) return;
            PrintToAll("vote.time_up_random");
        }
        ScheduleChange(target, 3f);
    }

    private void ScheduleChange(string key, float delay)
    {
        if (_changing) return;
        _changing = true;
        _nextMap = null;

        PrintToAll("vote.changing", _maps.DisplayName(key));
        if (delay <= 0)
            Change();
        else
            AddTimer(delay, Change, TimerFlags.STOP_ON_MAPCHANGE);

        void Change()
        {
            // Don't carry this map's extensions over to the next one
            if (_extends > 0)
                SetTimeLimit(TimeLimitSeconds() / 60f - _extends * Config.ExtendMinutes);

            _recent.Remove(key);
            _recent.Add(key);
            while (_recent.Count > Config.ExcludeRecentMaps + 1)
                _recent.RemoveAt(0);

            _maps.ChangeMap(key);
        }
    }

    // ── Timelimit ─────────────────────────────────────────────────────────────

    private void Timeleft(CCSPlayerController player)
    {
        float limit = TimeLimitSeconds();
        if (limit <= 0)
        {
            PrintToPlayer(player, "timeleft.no_limit");
            return;
        }

        int remaining = Math.Max(0, (int)limit - _elapsed);
        PrintToPlayer(player, "timeleft.remaining", remaining / 60, $"{remaining % 60:D2}");
        if (_nextMap != null)
            PrintToPlayer(player, "timeleft.next_map", _maps.DisplayName(_nextMap));
    }

    private static float TimeLimitSeconds() =>
        (ConVar.Find("mp_timelimit")?.GetPrimitiveValue<float>() ?? 0f) * 60f;

    private static void SetTimeLimit(float minutes) =>
        Server.ExecuteCommand($"mp_timelimit {minutes.ToString(CultureInfo.InvariantCulture)}");

    // ── Workshop collection ───────────────────────────────────────────────────

    private void FetchWorkshopMaps()
    {
        string collectionId = WorkshopCollectionId();
        if (collectionId == "")
        {
            if (_maps.Maps.Count == 0)
                Logger.LogWarning("[RTV] No maps: {File} is missing or empty and no workshop collection was found. " +
                                  "Set WorkshopCollectionId in the config or start the server with +host_workshop_collection <id>.",
                    Config.MapsFile);
            return;
        }

        string cachePath = Path.Combine(ConfigDirectory, "workshop_cache.json");
        _workshop.GetMapsAsync(collectionId, cachePath, Config.WorkshopCacheHours).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully) return;
            Server.NextFrame(() =>
            {
                _maps.MergeWorkshopMaps(t.Result);
                Logger.LogInformation("[RTV] Workshop collection {Id}: {Count} maps. Map list now has {Total} maps.",
                    collectionId, t.Result.Count, _maps.Maps.Count);
            });
        });
    }

    private string WorkshopCollectionId()
    {
        if (Config.WorkshopCollectionId != "") return Config.WorkshopCollectionId;

        string? id = ConVar.Find("host_workshop_collection")?.StringValue;

        // Not always exposed as a convar: fall back to the server's launch options
        if (string.IsNullOrEmpty(id) || id == "0")
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => a.Equals("+host_workshop_collection", StringComparison.OrdinalIgnoreCase));
            id = i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
        }

        return id.All(char.IsDigit) && id != "0" ? id : "";
    }

    [ConsoleCommand("css_rtv_maps", "Show where the RTV map list comes from")]
    [RequiresPermissions("@css/changemap")]
    public void OnMapsCommand(CCSPlayerController? caller, CommandInfo info)
    {
        string collectionId = WorkshopCollectionId();
        info.ReplyToCommand($"[RTV] {_maps.Maps.Count} maps. Workshop collection: {(collectionId == "" ? "none" : collectionId)}. Maps file: {Path.Combine(ConfigDirectory, Config.MapsFile)}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ConfigDirectory =>
        Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", ModuleName));

    // Workshop maps are keyed by workshop id, so the engine's map name only identifies
    // static entries; for the rest we rely on remembering the map we changed to.
    private bool IsCurrentMap(string key) =>
        key.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase) ||
        _maps.DisplayName(key).Equals(Server.MapName, StringComparison.OrdinalIgnoreCase) ||
        key == _recent.LastOrDefault();

    private static IEnumerable<CCSPlayerController> Humans() =>
        Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV);

    private string Localize(CCSPlayerController player, string key, params object[] args)
    {
        using (new WithTemporaryCulture(player.GetLanguage()))
            return Localizer[key, args];
    }

    private void PrintToPlayer(CCSPlayerController player, string key, params object[] args) =>
        player.PrintToChat($"{Prefix} {Localize(player, key, args)}");

    private void PrintToAll(string key, params object[] args)
    {
        foreach (var player in Humans())
            PrintToPlayer(player, key, args);
    }
}

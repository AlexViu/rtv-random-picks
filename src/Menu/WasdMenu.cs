using CounterStrikeSharp.API.Core;

namespace RtvRandomPicks;

public class WasdMenuOption
{
    public string Display { get; init; } = "";
    public Action<CCSPlayerController, WasdMenuOption> OnChoose { get; init; } = null!;

    /// <summary>Shown next to the option when greater than zero (live vote count).</summary>
    public int Count { get; set; }
}

public class WasdMenu
{
    public string Title { get; }
    public List<WasdMenuOption> Options { get; } = new();

    public WasdMenu(string title) => Title = title;

    public void Add(string display, Action<CCSPlayerController, WasdMenuOption> onChoose) =>
        Options.Add(new WasdMenuOption { Display = display, OnChoose = onChoose });
}

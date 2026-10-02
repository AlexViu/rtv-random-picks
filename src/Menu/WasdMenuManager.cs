using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace RtvRandomPicks;

/// <summary>Center-screen menu driven by W/S (move), E (select) and R (close).</summary>
public class WasdMenuManager
{
    private const int VisibleOptions = 6;

    private class OpenMenuState
    {
        public CCSPlayerController Player = null!;
        public WasdMenu Menu = null!;
        public int Selected;
        public PlayerButtons Buttons;
        public bool Frozen;
        public string Html = "";
    }

    private readonly Dictionary<int, OpenMenuState> _open = new();

    /// <param name="freeze">Hold the player in place while the menu is open, so W/S only scroll.</param>
    public void Open(CCSPlayerController player, WasdMenu menu, bool freeze)
    {
        if (menu.Options.Count == 0) return;

        Close(player);
        var state = new OpenMenuState { Player = player, Menu = menu, Buttons = player.Buttons, Frozen = freeze };
        _open[player.Slot] = state;
        if (freeze) SetMoveType(player, MoveType_t.MOVETYPE_NONE);
        Render(state);
    }

    public void Close(CCSPlayerController player)
    {
        if (!_open.Remove(player.Slot, out var state)) return;
        if (!player.IsValid) return;
        if (state.Frozen) SetMoveType(player, MoveType_t.MOVETYPE_WALK);
        player.PrintToCenterHtml(" ");
    }

    /// <summary>Forgets a disconnected player without touching the (now invalid) controller.</summary>
    public void Remove(int slot) => _open.Remove(slot);

    public void CloseAll()
    {
        foreach (var state in _open.Values.ToList())
            Close(state.Player);
    }

    /// <summary>Re-renders open menus, e.g. after a vote count changed.</summary>
    public void RefreshAll()
    {
        foreach (var state in _open.Values)
            Render(state);
    }

    public void OnTick()
    {
        foreach (var state in _open.Values.ToList())
        {
            var player = state.Player;

            // An earlier callback this tick may have closed or replaced this menu
            if (!_open.TryGetValue(player.Slot, out var current) || current != state) continue;

            if (!player.IsValid)
            {
                _open.Remove(player.Slot);
                continue;
            }

            var pressed = player.Buttons & ~state.Buttons;
            state.Buttons = player.Buttons;
            int count = state.Menu.Options.Count;

            if ((pressed & PlayerButtons.Forward) != 0)
            {
                state.Selected = (state.Selected + count - 1) % count;
                Render(state);
            }
            else if ((pressed & PlayerButtons.Back) != 0)
            {
                state.Selected = (state.Selected + 1) % count;
                Render(state);
            }
            else if ((pressed & PlayerButtons.Use) != 0)
            {
                var option = state.Menu.Options[state.Selected];
                option.OnChoose(player, option);
            }
            else if ((pressed & PlayerButtons.Reload) != 0)
            {
                Close(player);
            }

            // Center HTML fades out unless it is re-sent every tick
            if (_open.ContainsKey(player.Slot))
                player.PrintToCenterHtml(state.Html);
        }
    }

    private static void Render(OpenMenuState state)
    {
        var options = state.Menu.Options;
        int first = Math.Clamp(state.Selected - VisibleOptions + 1, 0, Math.Max(0, options.Count - VisibleOptions));
        int last = Math.Min(options.Count, first + VisibleOptions);

        var sb = new StringBuilder();
        sb.Append($"<b><font color='#ff4444' class='fontSize-m'>{state.Menu.Title}</font></b><br>");

        for (int i = first; i < last; i++)
        {
            var option = options[i];
            string count = option.Count > 0 ? $" <font color='#88ff88'>({option.Count})</font>" : "";

            if (i == state.Selected)
                sb.Append($"<font color='yellow'>►[</font> <font color='#9acd32' class='fontSize-m'>{option.Display}</font>{count} <font color='yellow'>]◄</font><br>");
            else
                sb.Append($"<font color='white' class='fontSize-m'>{option.Display}</font>{count}<br>");
        }

        if (last < options.Count)
            sb.Append("<font color='gray'>▼ ▼ ▼</font><br>");

        sb.Append("<font color='#ff3333' class='fontSize-sm'>Move: <font color='#f5a142'>[W/S]</font> Select: <font color='#f5a142'>[E]</font> Exit: <font color='#f5a142'>[R]</font></font>");
        state.Html = sb.ToString();
    }

    private static void SetMoveType(CCSPlayerController player, MoveType_t moveType)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid) return;

        pawn.MoveType = moveType;
        Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", moveType);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }
}

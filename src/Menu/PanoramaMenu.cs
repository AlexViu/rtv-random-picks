using System.Runtime.CompilerServices;
using CounterStrikeSharp.API.Core;
using RandomPicksHud.Api;

namespace RtvRandomPicks;

/// <summary>
/// Shows a <see cref="WasdMenu"/> in the RandomPicksHud Panorama menu. Only this class touches RandomPicksHudApi:
/// if that shared DLL or the plugin is missing, the calls fail here and the center-screen menu is used instead.
/// </summary>
internal static class PanoramaMenu
{
    // ModuleName, so servers can send this plugin back to its own menu (MenuExcludedPlugins in RandomPicksHud)
    private const string PluginName = "RtvRandomPicks";

    /// <returns>A handle for <see cref="Refresh"/>, or null to use the center-screen menu.</returns>
    public static object? TryOpen(CCSPlayerController player, WasdMenu menu, bool freeze, Action onClose)
    {
        try { return Open(player, menu, freeze, onClose); }
        catch (Exception) { return null; }
    }

    public static void Close(CCSPlayerController player)
    {
        try { CloseCore(player); }
        catch (Exception) { }
    }

    public static void Refresh(CCSPlayerController player, WasdMenu menu, object handle)
    {
        try { RefreshCore(player, menu, handle); }
        catch (Exception) { }
    }

    private static string Count(WasdMenuOption option) => option.Count > 0 ? option.Count.ToString() : "";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object? Open(CCSPlayerController player, WasdMenu menu, bool freeze, Action onClose)
    {
        var api = IRpMenuApi.Capability.Get();
        if (api is null || !api.IsEnabled(player, PluginName)) return null;

        var rp = new RpMenu(menu.Title) { Freeze = freeze, TimeLimit = menu.TimeLimit, OnClose = _ => onClose() };
        foreach (var option in menu.Options)
            rp.Add(option.Display, (p, _) => option.OnChoose(p, option), Count(option));
        api.Open(player, rp);
        return api.IsOpen(player) ? rp : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CloseCore(CCSPlayerController player) => IRpMenuApi.Capability.Get()?.Close(player);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RefreshCore(CCSPlayerController player, WasdMenu menu, object handle)
    {
        if (handle is not RpMenu rp) return;
        for (int i = 0; i < rp.Items.Count && i < menu.Options.Count; i++)
            rp.Items[i].Value = Count(menu.Options[i]);
        IRpMenuApi.Capability.Get()?.Refresh(player);
    }
}

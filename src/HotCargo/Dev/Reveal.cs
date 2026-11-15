using Cosmoteer.Modes;
using HarmonyLib;

namespace HotCargo.Dev;

/// <summary>
/// Screenshot scripts only: "reveal on" gives the local player's team unlimited sight and radar, so ships and loot far from the
/// player's ships are drawn instead of radar blips (fog of war off). "reveal off" ends it. It changes what the game treats as seen
/// (player auto-fire, AI aggro checks of that team), so game tests must not use it, and it is local: never in co-op.
/// </summary>
internal static class Reveal
{
    /// <summary>The team that sees everything, or null.</summary>
    static int? _team;

    internal static void Register() => Autotest.Commands["reveal"] = arg => _team = arg.Trim() switch
    {
        "on" => (Autotest.Game ?? throw new InvalidOperationException("reveal needs a game")).LocalPlayerState.Team,
        "off" => null,
        var other => throw new InvalidOperationException($"reveal needs on or off, not '{other}'"),
    };

    [HarmonyPatch(typeof(SimModeManager), nameof(SimModeManager.TeamHasUnlimitedSightRange))]
    static class SightPatch
    {
        static void Postfix(int team, ref bool __result) { if (_team == team) __result = true; }
    }

    [HarmonyPatch(typeof(SimModeManager), nameof(SimModeManager.TeamHasUnlimitedRadar))]
    static class RadarPatch
    {
        static void Postfix(int team, ref bool __result) { if (_team == team) __result = true; }
    }
}

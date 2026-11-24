using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Modes.Career.Comms;
using Halfling;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 2010 to 2014: where the real time of this machine goes (the screen, then the fleet activity), and the ship design work of
/// every player. Read only: the screen is local GUI state, the design work comes from the ship update inputs that the sim runs.
/// </summary>
static class RealTime
{
    /// <summary>
    /// Spec 2011: what this machine's screen shows now. The event page and the comms window are this machine's floating windows (an
    /// unopened hail is no window), the map, the build tool and the pause are the game's own state.
    /// </summary>
    internal static ScreenState Screen(Run run)
    {
        var game = run.Game;
        var windows = game.Gui?.FloatingWindows.Children;
        return new ScreenState(
            Menu: App.Director.PeekState() != game,
            Event: windows?.OfType<Events.EventDialog>().Any() == true,
            Map: game.ShowMap,
            Comms: windows?.OfType<CommWindow>().Any() == true,
            Editor: game.Gui?.ShipGui?.BuildButton?.IsSelected == true,
            Paused: game.Sim?.IsPaused.Displayed == true);
    }

    /// <summary>
    /// Spec 2013: the kind of a ship update input. Null for one that is no design change (crew hire, repairs). The blueprint tools
    /// send BlueprintsOnly; "make it so" sends ApplyBlueprintMetadata; a live placement, an undo or a redo outside blueprint mode
    /// sends Build alone.
    /// </summary>
    internal static DesignChange? Kind(UpdateShipFlags flags) =>
        (flags & UpdateShipFlags.Build) == 0 || (flags & UpdateShipFlags.ManualCrewUpdate) != 0 ? null
        : (flags & UpdateShipFlags.BlueprintsOnly) != 0 ? DesignChange.Edit
        : (flags & UpdateShipFlags.ApplyBlueprintMetadata) != 0 ? DesignChange.Apply
        : DesignChange.Live;

    /// <summary>
    /// Runs on every machine at the same lockstep point, so each file counts the design work of all players. The counts come before
    /// the input runs: the input can change its update info. Limit: an input that the sim refuses (a ship that left, a player without
    /// command of it) still counts. Revisit if refused inputs show up in the counts.
    /// </summary>
    [HarmonyPatch(typeof(MPUpdateShipInput), nameof(MPUpdateShipInput.Execute))]
    static class DesignPatch
    {
        static void Prefix(MPUpdateShipInput __instance, GameRoot game, int playerIndex)
        {
            if (Session.Current is not { } s || game != s.Game || Kind(__instance.Flags) is not { } kind) return;
            Errors.Guard("telemetry design", () =>
            {
                var sui = __instance.UpdateInfo;
                if (!sui.HasShip) return;
                var (added, removed) = kind == DesignChange.Edit
                    ? (sui.BlueprintPartsToAdd.Count, sui.BlueprintPartsToRemove.Count)
                    : (sui.PartsToAdd.Count, sui.PartsToRemove.Count);
                s.OnDesign(kind, sui.Ship, playerIndex, added, removed);
            });
        }
    }
}

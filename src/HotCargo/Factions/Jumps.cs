using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Ftl;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Network;
using Halfling.Random;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Ux;

namespace HotCargo.Factions;

/// <summary>
/// Spec 650 and 653: relay jumps and jumps in combat. Vanilla has one way to change system: the ships fly to a Hyper-Jump Relay, spin
/// up their hyperdrives and pay hyperium (GameRoot.DoHumanPlayersFtlJump). Every jump removes a share of the heat: less when hunters are
/// near the relay at the jump start, more when none are.
/// </summary>
static class Jumps
{
    /// <summary>
    /// The jump that the next system switch completes: its kind and the ships that jump. Set in the JumpToNode prefix, which runs from
    /// MP input execution behind the jump barriers on every machine. Not saved: a save never holds a jump in flight.
    /// </summary>
    static (Run Run, JumpKind Kind, ObjectID[] Ships)? _pending;

    /// <summary>The kind fixed when DoHumanPlayersFtlJump charges the fuel. JumpToNode runs later, after a barrier, and must not see a newer switch.</summary>
    static JumpKind? _charged;

    /// <summary>A jump waits for the vanilla save block to clear (spec 653). One waiter at a time.</summary>
    static bool _waiting;

    /// <summary>Tests: the kind of the next JumpToNode call, for jumps without the FTL animation.</summary>
    internal static JumpKind? TestOverride;

    internal static JumpKind PendingKind(Run run) => _pending is { } p && p.Run == run ? p.Kind : JumpKind.View;

    /// <summary>Spec 650: hunters (live or in stasis) within RelayWatchRadius of the relay that leads toward the destination.</summary>
    internal static int HuntersNearRelay(Run run, SimRoot sim, MapNode destination)
    {
        if (run.Game.Map.Nodes.GetFirstNodeInPathTo(destination, requireJumpable: true) is not { } first) return 0;
        if (FtlGate.GetGateForDestination(sim, first) is not { } gate) return 0;
        var at = gate.DetWorldCenter;
        float r = run.T.Factions.RelayWatchRadius;
        return SimQueries.TaggedShips(sim, HunterSpawner.HunterTag).Count(h => h.Location.DistanceTo(at) <= r);
    }

    /// <summary>Spec 650: the kind of a jump to the destination if it started now. Sim state only.</summary>
    internal static JumpKind KindNow(Run run, SimRoot sim, MapNode destination) =>
        HuntersNearRelay(run, sim, destination) > 0 ? JumpKind.RelayWatched : JumpKind.RelayClear;

    /// <summary>
    /// Spec 653: vanilla drops a jump while game saving is blocked (multi-step explosions, self-destructing parts, jump-in invulnerability),
    /// and greys out ENGAGE. In a fight that is most of the time. Here the jump waits until the block clears, like the vanilla
    /// MPJumpToNodeInput, and the fight goes on meanwhile. The jump kind is fixed when the jump really starts.
    /// </summary>
    [HarmonyPatch(typeof(GameRoot), nameof(GameRoot.DoHumanPlayersFtlJump))]
    static class ChargePatch
    {
        static bool Prefix(GameRoot __instance)
        {
            if (Run.Current is not { } run || run.Game != __instance || __instance.DestinationMapNode.Confirmed is not { } dest || __instance.IsDoingNodeJump) return true;
            if (__instance.IsGameSavingBlocked)
            {
                if (!_waiting) WaitThenJump(run);
                return false;
            }
            _charged = KindNow(run, __instance.Sim, dest);
            JumpStarts++;
            Log.Info($"jump starts to {dest.Name}: {_charged}");
            return true;
        }
    }

    /// <summary>Jumps that started (the vanilla jump ran past its save check). For tests.</summary>
    internal static int JumpStarts;

    static async void WaitThenJump(Run run)
    {
        _waiting = true;
        var game = run.Game;
        Log.Info("jump waits for the save block to clear (combat effects)");
        try
        {
            while (game.IsGameSavingBlocked)
            {
                await game.NetManager.UntilTick();
                if (Run.Current != run || game.DestinationMapNode.Confirmed is null || game.IsDoingNodeJump) return;
            }
        }
        finally { _waiting = false; }
        Errors.Guard("jump after save block", game.DoHumanPlayersFtlJump);
    }

    /// <summary>Only DoHumanPlayersFtlJump passes doFtlAnimation = true. A false call is a view switch (MPJumpToNodeInput) or a test jump.</summary>
    [HarmonyPatch(typeof(GameRoot), nameof(GameRoot.JumpToNode))]
    static class SwitchPatch
    {
        static void Prefix(GameRoot __instance, MapNode node, bool doFtlAnimation, List<Ship>? shipsToJump)
        {
            if (Run.Current is not { } run || run.Game != __instance || node == __instance.CurrentMapNode) return;
            var kind = TestOverride ?? (doFtlAnimation ? _charged ?? KindNow(run, __instance.Sim, node) : JumpKind.View);
            TestOverride = null;
            _charged = null;
            _pending = (run, kind, shipsToJump?.Select(s => s.UniqueID).ToArray() ?? []);
            Journey.Lockdown.OnSwitch(run, node);
            Journey.LeftPayments.OnSwitch(run, kind != JumpKind.View);
            Log.Info($"jump to {node.Name}: {kind}, {_pending.Value.Ships.Length} ships");
        }
    }

    [HarmonyPatch(typeof(FtlJumpGui), "OnUpdatingUIState")]
    static class GuiUpdatePatch
    {
        static void Postfix(FtlJumpGui __instance)
        {
            if (!__instance.SelfActive || Run.Current is not { } run || run.Game != __instance.Game) return;
            UxKit.Guard("jump window", () =>
            {
                var game = __instance.Game;
                // Spec 653: ENGAGE stays active while game saving is blocked. The jump waits for the block in ChargePatch.
                var ready = game.Sim.Ships.LocalPlayerSignificantShips.Where(s => s.Ftl.IsReadyToJump).ToList();
                if (game.DestinationMapNode.Confirmed is { } dest)
                {
                    game.Map.GetTotalFtlJumpFuelCost(ready, dest, out bool canAfford, out _);
                    __instance._jumpBtn.SelfInputActive = canAfford && !game.PlayersReadyToJump.Contains(game.NetManager.LocalPlayerID)
                        && game.Sim.Ships.HumanPlayerSignificantShips.Any(s => s.Ftl.IsReadyToJump);
                }
            });
        }
    }
}

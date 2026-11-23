using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Data;
using HarmonyLib;
using HotCargo.Ai;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// Spec 36, 137. Vanilla AggroEnemies scores a target by its firepower, and CareerSimModeManager.ModifyAIAttackValue divides
/// the score of civilian ships by 10,000, so AI never picks an unarmed freighter while a warship is in sight.
/// Convoy raiders value the convoy freighter like a ship with MissionTunables.EscortRaiderFocus times their own firepower.
/// Hold-out wave ships value their objective's target the same way, with the profile's TargetFocus (Claim Jumpers: the barge).
/// Vanilla then keeps the target until another scores TargetSwitchThresholdFactor (6) times higher, with distance to the power 1.5,
/// so a raider turns on a guard only when the guard is much closer than the target: in its path.
/// Leftover convoy raiders keep this after the mission ends. That is harmless: the freighter is then a normal civilian ship.
/// </summary>
[HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.ModifyAIAttackValue))]
static class ConvoyTargetPatch
{
    static readonly ID<SimObjectSpawner> RaiderTag = new("hc_escort_raider"), FreighterTag = new("hc_escort_ship");

    static void Postfix(Ship sourceShip, Ship targetShip, ref float value)
    {
        if (Run.Current is not { } run) return;
        try
        {
            float focus = sourceShip.HasTag(RaiderTag) && targetShip.HasTag(FreighterTag)
                ? MissionArea.TunablesFor(run.Game).EscortRaiderFocus
                : Mechanics.HoldOutObjective.FocusOn(run, sourceShip, targetShip);
            if (focus > 0f) value = Math.Max(value, Firepower.Of(sourceShip) * focus);
        }
        catch (Exception ex) { Errors.Report("convoy raider target", ex.Message, ex); }
    }
}

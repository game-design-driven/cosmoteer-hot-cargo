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
            // Spec 1893: the escorts that the mod spawns in code (the Blind Courier, the Ore Tender) have no tags.
            float focus = sourceShip.HasTag(RaiderTag) && (targetShip.HasTag(FreighterTag) || EscortHail.IsEscortShip(run, targetShip.UniqueID))
                ? MissionArea.TunablesFor(run.Game).EscortRaiderFocus
                : Mechanics.HoldOutObjective.FocusOn(run, sourceShip, targetShip);
            if (focus > 0f) value = Math.Max(value, Firepower.Of(sourceShip) * focus);
        }
        catch (Exception ex) { Errors.Report("convoy raider target", ex.Message, ex); }
    }
}

/// <summary>
/// Spec 1885. Vanilla career AI attacks only ships within the radar of the human team (CareerSimModeManager.AllowAIToAttack), so raiders
/// never fired on an escort that no player saw, even live next to it. Raiders of an escort mission (convoy, chain and guard raids, Mining
/// Escort waves) may attack the escort of an accepted escort mission anywhere. Sim state only, so co-op stays in step.
/// </summary>
[HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.AllowAIToAttack))]
static class EscortRaiderAttackPatch
{
    static readonly ID<SimObjectSpawner>[] RaiderTags = [new("hc_escort_raider"), Mechanics.MiningEscortObjective.WaveTag];
    static readonly ID<SimObjectSpawner> FreighterTag = new("hc_escort_ship");

    static void Postfix(Ship sourceShip, Ship targetShip, ref bool __result)
    {
        if (__result || Run.Current is not { } run || !RaiderTags.Any(sourceShip.HasTag)) return;
        // Spec 2431: the Mayday victim carries the convoy freighter tag.
        try { __result = targetShip.HasTag(FreighterTag) || EscortHail.IsEscortShip(run, targetShip.UniqueID); }
        catch (Exception ex) { Errors.Report("escort raider attack", ex.Message, ex); }
    }
}

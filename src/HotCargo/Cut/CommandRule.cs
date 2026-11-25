using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>
/// Spec 2476: after every split of a human player's ship (a cut or an explosion), each new piece without command (no working control
/// room and no working Computer) becomes a derelict, whatever crew or beds it has. Runs after the vanilla rule
/// (CareerSimModeManager.OnShipSplit, GetAllegianceForReducedShip), so pieces with command keep the vanilla outcome. The piece that stays
/// the ship keeps the vanilla rule, as does a ship that loses its control room without a split, so its crew can rebuild it.
/// </summary>
[HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.OnShipSplit))]
static class CommandRule
{
    static void Postfix(CareerSimModeManager __instance, IReadOnlyList<Ship> newShips) => Errors.Guard("split command rule", () =>
    {
        foreach (var ship in newShips)
        {
            if (!ship.IsHumanPlayerShip || ship.Commands.HasSignificantCommandParts || Autonomy.Autonomous.HasWorkingComputer(ship)) continue;
            Log.Info($"cut: {ship.Metadata.PlainShipName} ({ship.Parts.Count} parts, {ship.Crew.Assigned.Count} crew) has no command after a split and becomes a derelict");
            __instance.SetReducedShipAllegiance(ship, -3, null);
        }
    });
}

using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling.Collections;
using HotCargo.Core;

namespace HotCargo.Ai;

/// <summary>
/// Spec 1533. Firepower of a ship for Hot Cargo decisions: the vanilla rating of each weapon that vanilla would estimate
/// (not point defense, attack toggle on), each capped at FirepowerWeaponCap. Reads sim state only, so co-op machines agree.
/// Telemetry keeps the vanilla total (WeaponManager.GetTotalFirePower), which counts weapons in defend mode and has no cap.
/// </summary>
public static class Firepower
{
    public static float Of(Ship ship)
    {
        var run = Run.Current ?? throw new InvalidOperationException("firepower needs an active run for its cap");
        var selfTargets = new ReversibleDictionary<Weapon, Part>();
        ship.Weapons.GetSelfTargetMap(selfTargets, confirmed: true);
        var cache = new Dictionary<Weapon, float>();
        var ratings = new List<float>();
        foreach (var weapon in ship.Weapons)
            if (weapon.ShouldEstimateFirepower && !selfTargets.ContainsKey(weapon))
                ratings.Add(weapon.GetAIFirepowerRating(selfTargets, cache));
        return AiModel.Firepower(ratings, run.T.Tactics.FirepowerWeaponCap);
    }
}

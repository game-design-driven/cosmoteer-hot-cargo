using Cosmoteer.Data;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Debris;

/// <summary>
/// Spec 1247 to 1249: a part that breaks on a ship that the players did not build drops less loose salvage, and an asteroid part
/// drops more ore. Vanilla drops the resources of a removed part in PartsManager.OnPartDestroyed, through
/// SimNuggetManager.DropNuggets with source Part. A finished deconstruction ends in the same method, so only a removal without
/// a queued deconstruction counts as a break. Storage contents (source Storage) and parts that float free stay vanilla.
/// </summary>
static class SalvageDrops
{
    /// <summary>The ship whose part breaks right now, while vanilla drops that part's resources. Null outside a break. Sim thread only.</summary>
    [ThreadStatic] static Ship? _breaking;

    /// <summary>The run tunables, when this sim is the run's sim (vanilla game modes and other sims stay vanilla).</summary>
    static DebrisTunables? Tunables(Ship ship) => Run.Current is { } run && run.Game.Sim == ship.Sim ? run.T.Debris : null;

    [HarmonyPatch(typeof(PartsManager), "OnPartDestroyed")]
    static class BreakPatch
    {
        // __state keeps the outer mark: a break can cause another one inside it, and the inner one must not clear the outer mark.
        static void Prefix(object? sender, out Ship? __state)
        {
            __state = _breaking;
            bool isBreak = sender is Part { Ship: { } ship } part
                && ship.OptionalConstructionManager?.IsDeconstructionQueued(part, includeUnderlying: true) != true;
            _breaking = isBreak ? ((Part)sender!).Ship : null;
        }

        // A void finalizer lets an exception of the original pass on; it only restores the mark.
        static void Finalizer(Ship? __state) => _breaking = __state;
    }

    /// <summary>
    /// Spec 1247: the chance cut. Vanilla rolls each unit against the chance, so a lower chance keeps the same number of sim rolls.
    /// Asteroids are mining (spec 1248), and parts that the players built are their own losses: both stay out.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetNuggetDropChance))]
    static class SalvagePatch
    {
        // No Errors.Guard: the body has no call that can throw.
        static void Postfix(NuggetDropSource dropSource, bool isPlayer, ref float __result)
        {
            if (_breaking is not { } ship || dropSource != NuggetDropSource.Part || isPlayer || ship.Rules.IsAsteroid) return;
            if (Tunables(ship) is { } t) __result *= t.SalvageDropFactor;
        }
    }

    /// <summary>
    /// Spec 1248: the ore bonus scales the quantity, not the chance: a mining laser already drops ore at 100 %. The extra fraction is
    /// rolled from a hash of the asteroid and the part's place, so the sim's own random rolls do not move.
    /// </summary>
    [HarmonyPatch(typeof(SimNuggetManager), nameof(SimNuggetManager.DropNuggets),
        [typeof(ID<ResourceRules>), typeof(int), typeof(RotRect), typeof(NuggetDropSource), typeof(float), typeof(bool), typeof(bool), typeof(Ship), typeof(Vector2)])]
    static class OrePatch
    {
        static void Prefix(ref int quantity, RotRect worldRect, NuggetDropSource dropSource, float salvageFraction)
        {
            if (_breaking is not { Rules.IsAsteroid: true } rock || dropSource != NuggetDropSource.Part || quantity <= 0) return;
            if (Tunables(rock) is not { } t) return;
            int before = quantity;
            float factor = DebrisModel.OreFactor(salvageFraction, t.MiningYieldFactor, t.LaserMiningYieldFactor);
            // A full hash of the place: the first FastRand value of nearby seeds is not well spread (all ten parts of a test rock rolled low).
            ulong place = ((ulong)BitConverter.SingleToUInt32Bits(worldRect.Center.X) << 32) | BitConverter.SingleToUInt32Bits(worldRect.Center.Y);
            quantity = DebrisModel.ScaleQuantity(before, factor, Ai.PersonalityModel.Unit(place ^ (uint)rock.UniqueID, 0));
        }
    }
}

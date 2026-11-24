using Cosmoteer.Data;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
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
    /// Spec 1247, 1832, 1834: the chance cut, by the resource's base price: cheap resources × SalvageDropFactor, expensive components a mild cut.
    /// The base resources of a laser break (LaserBaseResources: steel, coil) get LaserBaseDropFactor for the salvage share instead.
    /// Other resources reach the roll only with their weapon units (LaserPatch), so they get the weapon cut.
    /// Vanilla rolls each unit against the chance, so a lower chance keeps the same number of sim rolls.
    /// Asteroids are mining (spec 1248), and parts that the players built are their own losses: both stay out.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetNuggetDropChance))]
    static class SalvagePatch
    {
        // No Errors.Guard: the body has no call that can throw.
        static void Postfix(ResourceRules rr, NuggetDropSource dropSource, float salvageFraction, bool isPlayer, ref float __result)
        {
            if (_breaking is not { } ship || dropSource != NuggetDropSource.Part || isPlayer || ship.Rules.IsAsteroid) return;
            if (Tunables(ship) is not { } t) return;
            __result *= IsLaserBase(t, rr)
                ? DebrisModel.ByLaserShare(salvageFraction, t.SalvageDropFactor, t.LaserBaseDropFactor)
                : DebrisModel.SalvageFactor(rr.BuyPrice, t.SalvageDropFactor, t.SalvageComponentDropFactor, t.SalvageComponentPrice);
        }
    }

    static bool IsLaserBase(DebrisTunables t, ResourceRules rr) => t.LaserBaseResources.Contains(rr.ID.ToString());

    /// <summary>
    /// Spec 1834: a laser break drops a sure share of a part's resources. Before vanilla rolls, the units split by the salvage share:
    /// the laser units drop LaserDropShare for sure, and the rest goes on to the roll as a weapon break (salvage share 0).
    /// LaserBaseResources (steel, coil) keep the roll (SalvagePatch). The rounding rolls come from a hash of the place and the resource,
    /// so the sim's own random rolls do not move. Ships that the players built, asteroids, tech and no-cargo resources stay out.
    /// </summary>
    [HarmonyPatch(typeof(SimNuggetManager), nameof(SimNuggetManager.DropNuggets),
        [typeof(ID<ResourceRules>), typeof(int), typeof(RotRect), typeof(NuggetDropSource), typeof(float), typeof(bool), typeof(bool), typeof(Ship), typeof(Vector2)])]
    static class LaserPatch
    {
        static void Prefix(SimNuggetManager __instance, ID<ResourceRules> resourceType, ref int quantity, RotRect worldRect, NuggetDropSource dropSource,
            ref float salvageFraction, bool destroyed, bool isPlayer, Vector2 inheritVelocity)
        {
            if (_breaking is not { } ship || dropSource != NuggetDropSource.Part || isPlayer || ship.Rules.IsAsteroid || quantity <= 0 || salvageFraction <= 0f) return;
            if (Tunables(ship) is not { } t) return;
            var rr = Cosmoteer.Extensions.GetResourceRules(resourceType);
            if (rr.ID == Tech.Vaults.TechId || !IsCargo(rr) || IsLaserBase(t, rr)) return;
            ulong seed = PlaceSeed(worldRect) ^ (uint)ship.UniqueID ^ ((ulong)(uint)HotCargo.ShipAi.ShipAiModel.StableHash(rr.ID.ToString()) << 16);
            var (sure, rolled) = DebrisModel.LaserSplit(quantity, salvageFraction, t.LaserDropShare, Ai.PersonalityModel.Unit(seed, 0), Ai.PersonalityModel.Unit(seed, 1));
            // The vanilla drop without a roll, with the same velocity rules as the end of this method.
            if (sure > 0) __instance.DropNuggets(resourceType, sure, worldRect, inheritVelocity, destroyed, false);
            quantity = rolled;
            salvageFraction = 0f;
        }
    }

    /// <summary>A full hash of a drop's place: the first FastRand value of nearby seeds is not well spread (all ten parts of a test rock rolled low).</summary>
    static ulong PlaceSeed(RotRect worldRect) =>
        ((ulong)BitConverter.SingleToUInt32Bits(worldRect.Center.X) << 32) | BitConverter.SingleToUInt32Bits(worldRect.Center.Y);

    /// <summary>
    /// Spec 2370: cargo comes back in full. While a storage bay tile (vanilla FlexResourceGrid) breaks, its contents drop 1:1, on every
    /// ship (thieves, pirates, hunters, scavengers, players). Resources that killed crew carry drop 1:1 too. Vanilla drops half
    /// (StorageDestroyed 50 %). Tech keeps its own rules (spec 65, 1549): it is left out. Part internals (batteries, ammo, factory buffers,
    /// typed grids) stay vanilla: they are not cargo. Spec 1835: a resource that vanilla never drops from a bay that weapons break
    /// (StorageDestroyed 0 %: ammo) drops none from weapons and LaserBayAmmoShare (0) of the vanilla chance from a salvage beam.
    /// True while a bay tile drops its contents. Sim thread only.
    /// </summary>
    [ThreadStatic] static bool _bayBreaking;

    [HarmonyPatch]
    static class BayPatch
    {
        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(Cosmoteer.Ships.Parts.Resources.FlexResourceGrid.ResourceTile)).Single(m => m.Name.EndsWith("OnDestroying", StringComparison.Ordinal));

        static void Prefix(out bool __state)
        {
            __state = _bayBreaking;
            _bayBreaking = true;
        }

        static void Finalizer(bool __state) => _bayBreaking = __state;
    }

    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetNuggetDropChance))]
    static class CargoPatch
    {
        // No Errors.Guard: the body has no call that can throw.
        static void Postfix(ResourceRules rr, NuggetDropSource dropSource, ref float __result)
        {
            if (Run.Current is not { } run || rr.ID == Tech.Vaults.TechId || !IsCargo(rr)) return;
            if (rr.DropRates.StorageDestroyed <= 0f)
            {
                // Ammo: vanilla drops it from a bay only by the salvage share (StorageSalvaged); only that share of it drops. The mod's own
                // items keep the rates of their rules (the Tracer Beacon: shots destroy it, careful salvage frees it).
                if (dropSource == NuggetDropSource.Storage && _bayBreaking && !IsModResource(rr)) __result *= run.T.Debris.LaserBayAmmoShare;
                return;
            }
            if (dropSource == NuggetDropSource.Crew || dropSource == NuggetDropSource.Storage && _bayBreaking) __result = 1f;
        }
    }

    /// <summary>
    /// Spec 1833: a resource without a stack size (vanilla battery, fire extinguisher, heat, ion energy) is no cargo. Vanilla never drops
    /// it loose: a loose one is invisible, and the vanilla collect tool divides by its stack size (ResourceManager.GetCapacityTotal),
    /// which crashed a playtest host.
    /// </summary>
    internal static bool IsCargo(ResourceRules rr) => rr.MaxStackSize > 0;

    /// <summary>The mod's own resources (its items): their IDs start with the mod author's namespace, the part of the mod ID up to its dot.</summary>
    internal static bool IsModResource(ResourceRules rr) => rr.ID.ToString().StartsWith(ModPrefix, StringComparison.Ordinal);

    static readonly string ModPrefix = Mod.HarmonyId[..(Mod.HarmonyId.IndexOf('.') + 1)];

    /// <summary>
    /// Spec 1833: removes loose resources that are no cargo, from saves of builds that dropped them. Sim tick, nugget order, so every
    /// co-op machine removes the same ones. Returns the units removed.
    /// </summary>
    internal static int RemoveLooseNonCargo(SimRoot sim)
    {
        int units = 0;
        for (int i = sim.Nuggets.Count - 1; i >= 0; i--)
        {
            var n = sim.Nuggets[i];
            if (IsCargo(Cosmoteer.Extensions.GetResourceRules(n.ResourceType))) continue;
            units += n.Quantity;
            Log.Info($"debris: removed a loose {n.ResourceType} x{n.Quantity}: no cargo (spec 1833)");
            sim.Nuggets.RemoveNugget(n);
        }
        return units;
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
            float factor = DebrisModel.ByLaserShare(salvageFraction, t.MiningYieldFactor, t.LaserMiningYieldFactor);
            quantity = DebrisModel.ScaleQuantity(before, factor, Ai.PersonalityModel.Unit(PlaceSeed(worldRect) ^ (uint)rock.UniqueID, 0));
        }
    }
}

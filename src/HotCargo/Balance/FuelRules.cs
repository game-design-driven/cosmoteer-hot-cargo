using Cosmoteer;
using Cosmoteer.Modes.Career;
using Halfling;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Balance;

/// <summary>
/// Spec 300, 1980 to 1982: the fuel values of the vanilla rules files come from the Balance tunables. They go into the loaded game rules
/// (GameApp.Rules), which every game of the process shares:
/// - before a new galaxy is made (the first system and its stations generate before the run starts), with TunablesFile.ForNewGame;
/// - at every game start and load, with the run's tunables;
/// - when the tunables change in a game: every value except the stack size, which waits for the next new or loaded game, because
///   storage tiles that hold more than a smaller stack would break.
/// Every co-op machine applies the same values at the same point: the defaults or the save at the start, the host's input later.
/// The multiplayer rules hash (Rules.RulesHash, which the game list and the join check compare) is computed from the rules files
/// before the first change, so these values never block a join.
/// </summary>
static class FuelRules
{
    internal static void Register()
    {
        Run.Hooks.GameStarted += run => Apply(run.T.Balance, gameStart: true);
        Run.Hooks.TunablesApplied += run => Apply(run.T.Balance, gameStart: false);
    }

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.InitGalaxyMap))]
    static class NewGamePatch
    {
        static void Prefix(CareerGameModeManager __instance) =>
            Errors.Guard("fuel tunables", () => Apply(TunablesFile.ForNewGame(__instance.Game).Balance, gameStart: true));
    }

    /// <summary>
    /// Spec 1836: the stack of steel and coil (BuildStackSize). The stack picture steps by stack / pictures, so a full tile shows the full
    /// picture. Limit: a name taken off BuildStackResources keeps the stack that it had until the game restarts; revisit if the list changes in play.
    /// </summary>
    static void ApplyBuildStacks(BalanceTunables t)
    {
        foreach (string id in t.BuildStackResources)
        {
            var rr = GameApp.Rules.Resources.FirstOrDefault(r => r.ID.ToString() == id) ?? throw new InvalidOperationException($"Balance.BuildStackResources: no resource {id}");
            rr.MaxStackSize = t.BuildStackSize;
            if (rr.StackSprites is { Length: > 0 } pictures) rr.ResourcesPerStackSprite = (t.BuildStackSize + pictures.Length - 1) / pictures.Length;
        }
    }

    internal static void Apply(BalanceTunables t, bool gameStart)
    {
        _ = GameApp.Rules.RulesHash;
        var fuel = GameApp.Rules.FtlFuelResource;
        var resource = fuel.GetResourceRules();
        resource.BuyPrice = t.FuelPrice;
        if (gameStart)
        {
            resource.MaxStackSize = t.FuelStackSize;
            ApplyBuildStacks(t);
        }
        var career = GameApp.Rules.CareerMode;
        // The ship classes that a career allows (the terran class): their jump fuel costs.
        foreach (var ship in GameApp.Rules.Ships.Where(s => career.AllowedShipClasses.Contains(s.ID)))
        {
            ship.MapJumpFuelCostPerMassDistance = t.MapJumpFuelCostPerMassDistance;
            ship.LocalJumpFuelCostPerMassDistance = t.LocalJumpFuelCostPerMassDistance;
        }
        // The station fuel stock of every sector type (they all inherit the hyperium row of sector_basic).
        foreach (var row in career.SectorTypes.SelectMany(s => s.TradeRoutes?.StationResourceTradeDeltas ?? []).Where(r => r.ResourceType == fuel))
        {
            row.PercentOfTypedTiles = new Range<float>(t.StationFuelTypedMin, t.StationFuelTypedMax);
            row.PercentOfUntypedTiles = new Range<float>(t.StationFuelUntypedMin, t.StationFuelUntypedMax);
        }
    }
}

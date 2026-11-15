using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Resources;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Debt;

/// <summary>
/// Spec 1417: a build pre-purchases its resources in one go with no money check (vanilla MPUpdateShipInput, construction mode
/// CrewResources). In a run, it buys only what the player can pay: money plus the credit left with the overdraft lender. The rest
/// waits, so the build waits, as with too little money in vanilla. Same loop as ResourceManager.PrePurchaseBuyableResources, with a budget.
/// </summary>
[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.PrePurchaseBuyableResources))]
static class CreditCap
{
    static bool Prefix(ResourceManager __instance, ID<ResourceRules> resourceType, int amount, out int totalCost, ref int __result)
    {
        totalCost = 0;
        if (amount <= 0 || Run.Current is not { } run || __instance.Ship is not { } ship || ship.Sim is not { } sim || sim != run.Game.Sim
            || run.State.Result != RunResult.Running || !ship.IsHumanPlayerShip) return true;
        int player = ship.Metadata.PlayerIndex;
        long budget = run.Game.GetPlayerState(player, deterministic: true).Money + Lenders.CreditLeft(run, sim, player);
        var sources = new List<Ship>();
        __instance.GetBuyableConstructionResourceShips(sources, resourceType);
        int bought = 0;
        foreach (var source in sources)
        {
            int price = sim.Mode.GetResourceBuyCost(resourceType, source.Metadata.PlayerIndex);
            int take = Math.Min(amount, source.Resources.GetBuyableConstructionResources(resourceType));
            if (price > 0) take = (int)Math.Min(take, Math.Max(0, budget / price));
            if (take <= 0) continue;
            source.Resources.AddPrePurchaseResources(ship, resourceType, take, price);
            totalCost += take * price;
            budget -= (long)take * price;
            bought += take;
            amount -= take;
            if (amount <= 0) break;
        }
        __result = bought;
        return false;
    }
}

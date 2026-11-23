using Cosmoteer;
using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Events;

/// <summary>
/// Spec 1103, 1875: what the players hold of a resource, the one rule for event needs, event costs, hand-over lines and the lost check.
/// Units in the storage of human ships, plus units that the players' crew carry: aboard, on another ship, or in space, also with no home ship.
/// </summary>
internal static class FleetHold
{
    /// <summary>The players' crew: the crew of every human ship (wherever they are), and the players' homeless crew in space. Each soul once.</summary>
    static IEnumerable<CrewSoul> Crew(SimRoot sim)
    {
        var humans = sim.RequireGame().HumanPlayersIndexes;
        return Vaults.HumanShips(sim).SelectMany(s => s.Crew.Assigned)
            .Concat(sim.Crew.InSpace.Select(c => c.Soul).Where(s => s.HomeShip is null && humans.Contains(s.PlayerIndex)));
    }

    static int Stored(Ship ship, ID<ResourceRules> resource) =>
        ship.Resources.GetResourceTotal(resource, includeStored: true, includeCarried: false, includeOffShipCarried: false);

    internal static int Units(SimRoot sim, ID<ResourceRules> resource) => Vaults.HumanShips(sim).Sum(s => Stored(s, resource)) + Carried(sim, resource);

    internal static int Carried(SimRoot sim, ID<ResourceRules> resource) => Crew(sim).Sum(c => c.CarriedResource.QuantityOfType(resource));

    /// <summary>Units of every resource type, for the event snapshot: one pass over the crew.</summary>
    internal static Dictionary<string, int> AllUnits(SimRoot sim)
    {
        var ships = Vaults.HumanShips(sim).ToList();
        var units = new Dictionary<string, int>();
        foreach (ResourceRules r in GameApp.Rules.Resources) units[r.ID.ToString()] = ships.Sum(s => Stored(s, r.ID));
        foreach (var soul in Crew(sim))
            if (soul.CarriedResource is { Quantity: > 0 } carried) units[carried.Type.ToString()] += carried.Quantity;
        return units;
    }

    /// <summary>
    /// Takes up to <paramref name="amount"/> units: from storage first (by player index, then ship ID), then from the carrying crew (by soul ID).
    /// Sim logic: the order is the same on every machine. Returns the units taken.
    /// </summary>
    internal static int Take(SimRoot sim, ID<ResourceRules> resource, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "take a positive amount");
        int left = amount;
        foreach (var ship in Vaults.HumanShips(sim).OrderBy(s => s.Metadata.PlayerIndex).ThenBy(s => s.UniqueID))
        {
            if (left == 0) return amount;
            left -= ship.Resources.RemoveResources(resource, left);
        }
        foreach (var soul in Crew(sim).Where(c => c.CarriedResource.QuantityOfType(resource) > 0).OrderBy(c => c.UniqueID))
        {
            if (left == 0) return amount;
            left -= ((IResourceSource)soul).SubtractResources(left);
        }
        return amount - left;
    }
}

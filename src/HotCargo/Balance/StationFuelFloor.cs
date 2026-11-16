using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships.Special;
using Cosmoteer.Modes.Career.TradeRoutes;
using Cosmoteer.Resources;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Balance;

/// <summary>
/// Spec 304: every station keeps a floor of fuel for sale, so a run never gets stuck on fuel (relay jumps cost fuel, spec 300).
/// A station fills its stock when it first loads (ShipSpawner.SimulateTrading, then the NPC fuel fraction), and trade ships move it toward the rules target later
/// (TradeRoutesManager.GetDesiredTradeDeltas, the one source of trade ship deltas). The first patch tops the stock up to the floor;
/// the second keeps trade ships from taking it below. Players can buy all of it. Both run in the sim.
/// Limit: a station that refuses the players (factions, spec 657) does not sell its floor. Revisit if refusals start below Strike heat.
/// </summary>
static class StationFuelFloor
{
    static ID<ResourceRules> Fuel => GameApp.Rules.FtlFuelResource;

    /// <summary>
    /// The floor in the system of this sim, or null with no active run in its game. A system switch builds the new sim before the game
    /// switches to it, so this uses the sim's own map node, not the current one.
    /// </summary>
    static int? Floor(SimRoot sim)
    {
        if (Run.Current is not { } run || sim.Game != run.Game || sim.MapNode is not { } node) return null;
        int tier = run.Mode.GetMapNodeData(node).TierRange?.Min ?? 1;
        var t = run.T.Balance;
        return BalanceModel.StationFuelFloor(GameData.TierValueMaximum(tier + 1), t.StationFuelPerMillion, t.StationFuelMinimum);
    }

    /// <summary>
    /// The fuel that a station holds, which every path compares with the floor: the trade stock of MetaShip.GetTotalTradeResources
    /// (stored fuel minus planned pickups, without fuel that crew carry), plus the fuel that its own crew move from one of its
    /// storages to another. Vanilla counts such a move as a pickup until the crew drop the fuel, so the stock for sale dips for some
    /// seconds (often after a station loads), but no fuel leaves the station.
    /// </summary>
    static int HeldFuel(Ship station)
    {
        int held = station.Resources.GetResourceTotal(Fuel, includeStored: true, includeCarried: false, includeAnticipatedPickups: true);
        foreach (var job in ResourceTransferJob.GetTransferJobsFor(station, MPValueType.Confirmed))
        {
            if (job.ResourceType != Fuel || job.Ship != station || job.Source is not { IncludeInShipDeltas: true } source
                || source.Ship != station || job.Sink is not ISimpleResourceStorage || job.Sink.Ship != station) continue;
            // The planned pickup as ResourceManager.GetAnticipatedConstructionPickups counts it, plus the fuel in crew hands.
            held += job.IsManual ? job.ResourcesRequested.Confirmed : job.AnticipatedPickUp + job.ResourcesInHand;
        }
        return held;
    }

    /// <summary>The fuel of a station in stasis, or null if it never loaded and remembers no stock.</summary>
    static int? RememberedFuel(MetaShip station)
    {
        if (station.Spawner is not StasisShipSpawner { ResourcesMemory: { } memory }) return null;
        var stock = new Dictionary<ID<ResourceRules>, int>();
        memory.GetTotalResources(stock, new Dictionary<ID<ResourceRules>, int>());
        return stock.GetValueOrDefault(Fuel);
    }

    static int? HeldFuel(MetaShip station) => station.Ship is { } ship ? HeldFuel(ship) : RememberedFuel(station);

    /// <summary>
    /// Stations that load before the run exists (the start) or while the next system builds get the floor here: a new run and each
    /// system switch top up the stations that are live.
    /// </summary>
    internal static void OnSystemEntered(Run run, bool firstVisit) => OnNewRun(run);

    internal static void OnNewRun(Run run) => TopUp(run, includeRemembered: false);

    /// <summary>
    /// Spec 852: a higher floor tops up the live stations at once, also the stations in stasis that remember a stock.
    /// A system entry does not top up remembered stocks, so fuel that the players bought does not come back on a return visit.
    /// </summary>
    internal static void OnTunablesApplied(Run run) => TopUp(run, includeRemembered: true);

    static void TopUp(Run run, bool includeRemembered) => Errors.Guard("station fuel floor", () =>
    {
        var sim = run.Game.Sim;
        if (Floor(sim) is not { } floor) return;
        foreach (var station in SpaceStation.GetStations(sim, onlyOperating: true).OrderBy(s => s.StationID))
        {
            if (station.PhysicalShip is { } ship)
            {
                int have = HeldFuel(ship);
                if (have < floor) ship.Resources.AddResources(Fuel, floor - have, excludeConstructionSinks: true);
            }
            else if (includeRemembered && RememberedFuel(station.MetaShip) is { } have && have < floor)
                station.MetaShip.AddResources(Fuel, floor - have);
        }
    });

    static readonly ID<SimObjectSpawner> TradeStationTag = new("trade_station");

    /// <summary>Test only: the floor of the current system, and the least fuel of a trade station that has its stock (loaded or remembered).
    /// Military stations get the floor too, but most have almost no storage (measured: 0 to 6 untyped tiles), so they sell little.</summary>
    internal static int CurrentFloor(SimRoot sim) => Floor(sim) ?? throw new InvalidOperationException("no active run");

    internal static int LeastStationFuel(Run run)
    {
        var least = int.MaxValue;
        foreach (var station in SpaceStation.GetStations(run.Game.Sim, onlyOperating: true))
        {
            var tags = station.PhysicalShip?.Tags ?? (station.Spawner is { } sp ? SimQueries.StasisTags(sp) : null);
            if (tags?.Contains(TradeStationTag) != true || HeldFuel(station.MetaShip) is not { } have) continue;
            least = Math.Min(least, have);
        }
        return least == int.MaxValue ? throw new InvalidOperationException("no trade station with a stock in this system") : least;
    }

    /// <summary>
    /// Test only: the crew of the first loaded trade station start to move all fuel of one storage tile to an empty tile, as vanilla
    /// crew do on their own after a station loads. Fails unless the vanilla trade stock dips and the held fuel stays the same.
    /// </summary>
    internal static void MoveStationFuel(Run run)
    {
        var ship = SpaceStation.GetStations(run.Game.Sim, onlyOperating: true).OrderBy(s => s.StationID)
            .Select(s => s.PhysicalShip).FirstOrDefault(s => s?.Tags.Contains(TradeStationTag) == true)
            ?? throw new InvalidOperationException("no loaded trade station; run tostation first");
        var tiles = FlexResourceGrid.GetAllOnShip(ship).SelectMany(g => g).ToList();
        var from = tiles.FirstOrDefault(t => t.ResourceType == Fuel && t.Resources > 0) ?? throw new InvalidOperationException("no fuel tile on the station");
        var to = tiles.FirstOrDefault(t => !t.HasResources && ((IResourceSink)t).GetRemainingCapacity(Fuel, allowPreferredTypeMismatch: false) > 0)
            ?? throw new InvalidOperationException("no empty fuel tile on the station");
        int before = HeldFuel(ship);
        ship.Resources.ManualResourceTransfer(from, to, from.Resources, sticky: false, TradeType.None, 0, autoCancel: false, confirmed: true, ship.Jobs.AllocGlobalJobID());
        int forSale = ship.Resources.GetResourceTotal(Fuel, includeStored: true, includeCarried: false, includeAnticipatedPickups: true);
        if (forSale >= before || HeldFuel(ship) != before)
            throw new InvalidOperationException($"internal fuel move: {before} held before, {HeldFuel(ship)} after, {forSale} for sale");
    }

    /// <summary>
    /// After the first spawn of a station: ShipSpawner.SimulateTrading sets the stock, then CareerSimModeManager.OnShipSpawned scales
    /// every NPC fuel storage by NpcSpawnedResourceFractions (25 % to 75 %), so the floor goes after both.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.OnShipSpawned))]
    static class OnLoad
    {
        static void Postfix(CareerSimModeManager __instance, Ship ship, bool isInitSpawn) => Errors.Guard("station fuel floor", () =>
        {
            if (!isInitSpawn || ship.IsHumanPlayerShip || !ship.TryGetSpecialComponent<SpaceStation>(out _) || Floor(__instance.Sim) is not { } floor) return;
            int have = HeldFuel(ship);
            if (have < floor) ship.Resources.AddResources(Fuel, floor - have, excludeConstructionSinks: true);
        });
    }

    /// <summary>
    /// Spec 304: a station that goes into stasis keeps the fuel in its crew's hands. Vanilla remembers only the fuel on storage tiles
    /// (StoredResourcesMemory), so a station whose crew moved fuel between tiles when the players left came back below the floor.
    /// </summary>
    [HarmonyPatch(typeof(StoredResourcesMemory), MethodType.Constructor, [typeof(Ship), typeof(bool)])]
    static class OnStasis
    {
        static void Postfix(StoredResourcesMemory __instance, Ship ship) => Errors.Guard("station fuel floor", () =>
        {
            // The ship already left the sim (ship.Sim is null), so the floor check uses the run's sim.
            if (ship.IsHumanPlayerShip || !ship.TryGetSpecialComponent<SpaceStation>(out _) || Run.Current?.Game.Sim is not { } sim || Floor(sim) is null) return;
            int carried = ship.Resources.GetResourceTotal(Fuel, includeStored: false, includeCarried: true);
            if (carried == 0) return;
            int added = __instance.AddResources(Fuel, carried);
            Log.Info($"station fuel floor: {ship.Metadata.PlainShipName} goes into stasis with {carried} fuel in crew hands; {added} kept");
        });
    }

    [HarmonyPatch(typeof(TradeRoutesManager), nameof(TradeRoutesManager.GetDesiredTradeDeltas))]
    static class OnTrade
    {
        static void Postfix(TradeRoutesManager __instance, MetaShip sinkShip, Dictionary<ID<ResourceRules>, int> tradeDeltas) => Errors.Guard("station fuel floor", () =>
        {
            if (!tradeDeltas.TryGetValue(Fuel, out int delta) || delta >= 0 || Floor(__instance.Sim) is not { } floor) return;
            // Vanilla limits a purchase to the station stock, so a station with no stock gets no negative delta.
            int have = HeldFuel(sinkShip) ?? throw new InvalidOperationException("a trade ship buys fuel from a station with no stock");
            int clamped = BalanceModel.ClampStationFuelDelta(delta, have, floor);
            if (clamped == 0) tradeDeltas.Remove(Fuel);
            else tradeDeltas[Fuel] = clamped;
        });
    }
}

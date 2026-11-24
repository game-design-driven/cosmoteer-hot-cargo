using Cosmoteer;
using Cosmoteer.Factions;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Modes.Career.TechTree;
using Cosmoteer.Ships.Special;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Balance;

/// <summary>Entry point of the Balance area: the economy patches (BALANCE.md) and the balance census for tests.</summary>
public static class BalanceArea
{
    public static void Register()
    {
        FuelRules.Register();
        Run.Hooks.NewRunStarted += StationFuelFloor.OnNewRun;
        Run.Hooks.SystemEntered += StationFuelFloor.OnSystemEntered;
        // A new fuel floor tops up the live stations at once (TUNABLES.md). A lower floor takes no fuel away.
        Run.Hooks.TunablesApplied += StationFuelFloor.OnTunablesApplied;
        RegisterContractTests();
        Autotest.Commands["balance_census"] = arg => BalanceCensus.Log(arg.Length > 0 ? arg : "census");
        // econ_census: logs every built-in ship (faction, tier, tags, value, units and value of each resource in its parts) and every
        // part (cost and resources), from the loaded rules. For price studies (docs/steel-coil-price-study.md). Test only.
        Autotest.Commands["econ_census"] = _ =>
        {
            static string Units(IEnumerable<(string Id, int Units)> rs) =>
                string.Join(",", rs.GroupBy(r => r.Id).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}:{g.Sum(r => r.Units)}"));
            foreach (var part in Autotest.RequireRun().Game.Sim.Ships.LocalPlayerShips.First().Rules.Parts.OrderBy(p => p.ID.ToString(), StringComparer.Ordinal))
                Log.Info($"ECON part {part.ID} cost={part.Cost} res={Units(part.Resources.Select(r => (r.Type.ToString(), (int)r.Quantity)))}");
            foreach (var b in GameApp.Rules.BuiltinShips.OrderBy(b => b.ID.ToString(), StringComparer.Ordinal))
            {
                int tier = b.Tier ?? 0;
                var parts = b.File.LoadShipBlueprints().BlueprintParts.Select(bp => bp.Rules).ToList();
                Log.Info($"ECON ship {b.ID} faction={b.Faction?.ToString() ?? "-"} tier={tier} tags={string.Join("|", (b.Tags ?? []).Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal))} " +
                         $"value={parts.Sum(p => p.Cost)} res={Units(parts.SelectMany(p => p.Resources.Select(r => (r.Type.ToString(), (int)r.Quantity))))}");
            }
            foreach (var r in GameApp.Rules.Resources.OrderBy(r => r.ID.ToString(), StringComparer.Ordinal))
                Log.Info($"ECON resource {r.ID} price={r.BuyPrice} stack={r.MaxStackSize}");
            foreach (var (kit, n, parts) in Thieves.ThiefShip.CensusLayouts())
                Log.Info($"ECON thief kit={kit} segments={n} parts={string.Join(",", parts.GroupBy(p => p).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}:{g.Count()}"))}");
            for (int t = 1; t <= 20; t++) Log.Info($"ECON tiermax {t} {GameData.TierValueMaximum(t)} thiefParts={Thieves.ThiefShip.Design(t).Count}");
        };
        Autotest.Commands["balance_visit"] = arg => BalanceCensus.Visit(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
        // balance_probe <tier> <count>: spawns hunters of this tier and logs their mass, value, fuel and kill fame.
        Autotest.Commands["balance_probe"] = arg =>
        {
            var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            BalanceCensus.Probe(a[0], a.Length > 1 ? a[1] : 1);
        };
        // balance_salvage <label>: destroys every hunter part and logs the value of the nuggets in the system after the last mark.
        Autotest.Commands["balance_salvage"] = arg => BalanceCensus.Salvage(arg);
        // Single player only: jumps the human ships to the unvisited neighbor with the highest low tier that is not a faction home system (the main route), without fuel.
        Autotest.Commands["balance_route"] = _ =>
        {
            var run = Autotest.RequireRun();
            int maxTier = Cosmoteer.GameApp.Rules.CareerMode.MapSizes[Journey.JourneyArea.RunMapSizeIndex()].MapGenerator.Spawners
                .OfType<Journey.TierCapSpawner>().Single().MaxChangeableLowTier;
            var next = run.Game.CurrentMapNode.Connections
                .Where(n => !run.State.VisitedNodes.Contains(n.UniqueID) && (run.Mode.GetMapNodeData(n).TierRange?.Min ?? 0) <= maxTier)
                .OrderByDescending(n => run.Mode.GetMapNodeData(n).TierRange?.Min ?? 0).ThenBy(n => n.UniqueID)
                .FirstOrDefault()
                ?? run.Game.CurrentMapNode.Connections.Where(n => !run.State.VisitedNodes.Contains(n.UniqueID)).OrderBy(n => n.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no unvisited neighbor system");
            Autotest.JumpWhenAllowed(run, next, "balance_route");
        };
    }

    static float _fameHeatExpected;

    /// <summary>Fields and commands of tests/autotest/balance-rules.txt: the contracts of spec 300 to 304.</summary>
    static void RegisterContractTests()
    {
        // Paid blueprint price ÷ (rules cost × the station faction's reputation factor). Expect Journey.TechCostScale. Needs tostation first.
        Autotest.Fields["balance_techcost_scale"] = () =>
        {
            var run = Autotest.RequireRun();
            var station = SpaceStation.GetStations(run.Game.Sim, onlyOperating: true).Select(s => s.PhysicalShip).OfType<Cosmoteer.Ships.Ship>().FirstOrDefault()
                ?? throw new InvalidOperationException("no loaded station; run tostation first");
            var tech = GameApp.Rules.CareerMode.TechsByID[new Cosmoteer.Data.ID<TechRules>("cosmoteer.cannon_med")];
            float rep = FactionRules.GetFaction(station.Metadata.PlayerIndex) is { } f ? run.Mode.GetCurrentReputationTitle(f.ID).TechBuyCostFactor : 1f;
            return CommTechsTab.GetTechCost(run.Game, tech, station) / (tech.Cost * (double)rep);
        };
        // balance_fame_over_cap <fame>: gives a mission fame reward while the players are above its fame cap (spec 302), and remembers
        // the heat that spec 25 predicts for the uncapped fame. balance_fame_heat_gap = heat gained − that prediction.
        Autotest.Commands["balance_fame_over_cap"] = arg =>
        {
            var run = Autotest.RequireRun();
            run.Mode.PlayerFame = Math.Max(run.Mode.PlayerFame, 100);
            var reward = new FameReward(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), maxFame: 10);
            int uncapped = (int)(reward.Fame * Journey.RewardScalePatch.FameFactor(run.Mode.EconDifficulty));
            float mult = run.HeatMultiplier(run.State.FleetTech);
            _fameHeatExpected = run.State.Heat + HeatModel.FameHeat(run.T.Heat.HeatPerStandardMission, mult, uncapped, (int)Math.Round(run.StandardMissionFamePaid(run.CurrentTier)));
            reward.GiveReward(run.Game);
        };
        Autotest.Fields["balance_fame_heat_gap"] = () => Autotest.RequireRun().State.Heat - _fameHeatExpected;
        Autotest.Fields["balance_fuel_stack"] = () => GameApp.Rules.FtlFuelResource.GetResourceRules().MaxStackSize;
        Autotest.Fields["balance_fuel_price"] = () => Autotest.RequireRun().Game.Sim.Mode.GetResourceBuyCost(GameApp.Rules.FtlFuelResource, null);
        Autotest.Fields["balance_fuel_station_typed_max"] = () =>
            Autotest.RequireRun().Mode.CurrentMapNodeData.SectorType.TradeRoutes.StationResourceTradeDeltas
                .Single(d => d.ResourceType == GameApp.Rules.FtlFuelResource).PercentOfTypedTiles.Max;
        Autotest.Fields["balance_jump_cost_per_mass_ly"] = () =>
            (SimQueries.FirstHumanShip(Autotest.RequireRun().Game.Sim) ?? throw new InvalidOperationException("no human ship")).Rules.MapJumpFuelCostPerMassDistance;
        // balance_set <BalanceTunables field> <value>: changes a live tunable for this run. Test only.
        Autotest.Commands["balance_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Balance, arg);
        Autotest.Fields["balance_station_fuel_floor"] = () => StationFuelFloor.CurrentFloor(Autotest.RequireRun().Game.Sim);
        // The least fuel of a station with a stock minus the floor. Expect 0 or more (spec 304).
        Autotest.Fields["balance_station_fuel_gap"] = () => StationFuelFloor.LeastStationFuel(Autotest.RequireRun()) - StationFuelFloor.CurrentFloor(Autotest.RequireRun().Game.Sim);
        // balance_station_fuel_move: the crew of a loaded trade station start an internal fuel move (StationFuelFloor.MoveStationFuel).
        Autotest.Commands["balance_station_fuel_move"] = _ => StationFuelFloor.MoveStationFuel(Autotest.RequireRun());
        // Fuel in the hands of the crews of the loaded stations: a station that goes into stasis now must keep it (spec 304).
        Autotest.Fields["balance_station_fuel_carried"] = () => SpaceStation.GetStations(Autotest.RequireRun().Game.Sim, onlyOperating: true)
            .Select(st => st.PhysicalShip).OfType<Cosmoteer.Ships.Ship>()
            .Sum(ship => ship.Resources.GetResourceTotal(GameApp.Rules.FtlFuelResource, includeStored: false, includeCarried: true));
        // Spec 305: the tier that hunter stage responses start from, and how far it is above the system tier.
        Autotest.Fields["balance_hunter_base_tier"] = () => Hunters.HunterSpawner.BaseTier(Autotest.RequireRun());
        Autotest.Fields["balance_hunter_floor_gap"] = () => Hunters.HunterSpawner.BaseTier(Autotest.RequireRun()) - Autotest.RequireRun().CurrentTier;
        // balance_hunters_set <HunterTunables field> <value>: changes a live hunter tunable for this run. Test only.
        Autotest.Commands["balance_hunters_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Hunters, arg);
        Autotest.Fields["balance_peak_tier_cap"] = () =>
        {
            var run = Autotest.RequireRun();
            return Journey.TierCapSpawner.PeakTierCap(run.Mode, run.State.SectorIndex) ?? -1;
        };
    }
}

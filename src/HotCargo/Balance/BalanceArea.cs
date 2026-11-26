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
    static Dictionary<string, long> _lootMark = [], _lootCargo = [];

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
        // part (cost and resources), from the loaded rules. For price studies (docs/loot-volume-study.md). Test only.
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
        // econ_loot mark | break <weapon|salvage> | report <label>: loot volume of a fight (docs/loot-volume-study.md). mark: the
        // loose units and the cargo of the ships of the last aispawn. break: every part of those ships breaks, by weapon damage or
        // salvage damage. report: per resource, the loose units since mark, the cargo at mark, and the free storage of the player's
        // first ship. Test only.
        Autotest.Commands["econ_loot"] = arg =>
        {
            var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var run = Autotest.RequireRun();
            var sim = run.Game.Sim;
            var enemies = Ai.PersonalityAutotest.LastSpawned.Select(id => sim.Ships[id]).OfType<Cosmoteer.Ships.Ship>().Where(s => !s.IsJunk).ToList();
            Dictionary<string, long> Loose() => Enumerable.Range(0, sim.Nuggets.Count).Select(i => sim.Nuggets[i])
                .GroupBy(n => n.ResourceType.ToString()).ToDictionary(g => g.Key, g => g.Sum(n => (long)n.Quantity));
            switch (a[0])
            {
                case "mark":
                    _lootMark = Loose();
                    _lootCargo = enemies.SelectMany(e => Tech.Vaults.StorageTiles(e)).Where(t => t.ResourceType is not null)
                        .GroupBy(t => t.ResourceType!.Value.ToString()).ToDictionary(g => g.Key, g => g.Sum(t => (long)t.Resources));
                    Log.Info($"ECON loot mark: {enemies.Count} ships [{string.Join(", ", enemies.Select(e => $"{e.Metadata.PlainShipName} value={e.BaseBlueprintCost} parts={e.Parts.Count} bayTiles={Tech.Vaults.StorageTiles(e).Count()}"))}] econ={run.Mode.EconDifficulty.ID} enemyDrop={run.Mode.EconDifficulty.EnemyResourceDropRateFactor}");
                    break;
                case "break":
                    bool salvage = a[1] == "salvage";
                    foreach (var part in enemies.SelectMany(e => e.Parts).ToList())
                        if (salvage) part.OnHit(part.Rules.MaxHealth * 10, Cosmoteer.DamageType.Salvage);
                        else part.Health = 0;
                    break;
                case "report":
                    var now = Loose();
                    var me = sim.Ships.LocalPlayerShips.First();
                    var tiles = Tech.Vaults.StorageTiles(me).ToList();
                    foreach (string r in now.Keys.Union(_lootCargo.Keys).OrderBy(k => k, StringComparer.Ordinal))
                    {
                        long gain = now.GetValueOrDefault(r) - _lootMark.GetValueOrDefault(r);
                        if (gain == 0 && _lootCargo.GetValueOrDefault(r) == 0) continue;
                        Log.Info($"ECON loot {a[1]} {r} loose={gain} cargoAtMark={_lootCargo.GetValueOrDefault(r)}");
                    }
                    int empty = tiles.Count(t => t.Resources == 0);
                    Log.Info($"ECON loot {a[1]} player {me.Metadata.PlainShipName} value={me.BaseBlueprintCost} bayTiles={tiles.Count} emptyTiles={empty} " +
                             $"held=[{string.Join(",", tiles.Where(t => t.Resources > 0).GroupBy(t => t.ResourceType!.Value.ToString()).Select(g => $"{g.Key}:{g.Sum(t => t.Resources)}/{g.Count()}"))}]");
                    break;
                default: throw new InvalidOperationException("econ_loot needs: mark | break weapon|salvage | report <label>");
            }
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
        // Single player only: jumps the human ships one band ahead on the run map (the unvisited neighbor of the next band), without fuel.
        Autotest.Commands["balance_route"] = _ =>
        {
            var run = Autotest.RequireRun();
            int band = Journey.RunMap.Band(run.Game.CurrentMapNode);
            var next = run.Game.CurrentMapNode.Connections
                .Where(n => !run.State.VisitedNodes.Contains(n.UniqueID) && Journey.RunMap.Band(n) == band + 1)
                .OrderBy(n => n.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no unvisited neighbor system in the next band");
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
        // balance_crew_overdraw: a crew member asked for more than it carries gives what it carries, without a crash (#307).
        Autotest.Commands["balance_crew_overdraw"] = _ => CrewCarryOverdraw.Check(Autotest.RequireRun());
        // Fuel in the hands of the crews of the loaded stations: a station that goes into stasis now must keep it (spec 304).
        Autotest.Fields["balance_station_fuel_carried"] = () => SpaceStation.GetStations(Autotest.RequireRun().Game.Sim, onlyOperating: true)
            .Select(st => st.PhysicalShip).OfType<Cosmoteer.Ships.Ship>()
            .Sum(ship => ship.Resources.GetResourceTotal(GameApp.Rules.FtlFuelResource, includeStored: false, includeCarried: true));
        // Spec 305: the tier that hunter stage responses start from, and how far it is above the system tier.
        // balance_peaktier <tier>: test only. The run's peak tier (spec 19), as after a visit to a system of that tier.
        Autotest.Commands["balance_peaktier"] = arg => Autotest.RequireRun().State.PeakTier = int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
        Autotest.Fields["balance_hunter_base_tier"] = () => Hunters.HunterSpawner.BaseTier(Autotest.RequireRun());
        Autotest.Fields["balance_hunter_floor_gap"] = () => Hunters.HunterSpawner.BaseTier(Autotest.RequireRun()) - Autotest.RequireRun().CurrentTier;
        // balance_hunters_set <HunterTunables field> <value>: changes a live hunter tunable for this run. Test only.
        Autotest.Commands["balance_hunters_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Hunters, arg);
    }
}

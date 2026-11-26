using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Crew.Jobs;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Journey;

/// <summary>Entry point of the Journey area. Mod.Boot calls Register once at startup, before the game starts.</summary>
public static class JourneyArea
{
    /// <summary>NameKey of the Hot Cargo run map size in mod/journey/galaxy.rules.</summary>
    const string RunMapNameKey = "HotCargoJourney/MapSize";

    public static void Register()
    {
        Run.Hooks.SystemEntered += Buyer.OnSystemEntered;
        Run.Hooks.NewRunStarted += Buyer.OnNewRun;
        Lockdown.Register();
        LeftPayments.Register();
        AddAutotest();
    }

    /// <summary>Index of the Hot Cargo run map in the career map size list. Throws if the mod rules did not add it.</summary>
    public static int RunMapSizeIndex()
    {
        int i = Array.FindIndex(GameApp.Rules.CareerMode.MapSizes, m => m.NameKey.Key == RunMapNameKey);
        return i >= 0 ? i : throw new InvalidOperationException($"map size {RunMapNameKey} not found; is the Hot Cargo rules mod on?");
    }

    static int LowTier(Run run, MapNode n) =>
        run.Mode.GetMapNodeData(n).TierRange?.Min ?? throw new InvalidOperationException($"node {n.Name} has no tier");

    static void AddAutotest()
    {
        Autotest.Commands["buyer"] = arg =>
        {
            var run = Autotest.RequireRun();
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts)
            {
                case ["spawn"]:
                    Buyer.Spawn(run);
                    break;
                // Calls the win path that the trade delivery patch calls. The real sale needs crew to walk to the station.
                case ["deliver", var units]:
                    Buyer.OnDelivered(run, int.Parse(units, CultureInfo.InvariantCulture));
                    break;
                // Test-only: puts the acting player's first ship next to the Buyer. Run it twice: the first call loads the Buyer
                // out of stasis, the second puts the ship within trade distance.
                case ["goto"]:
                {
                    var sim = run.Game.Sim;
                    var ship = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no ship of the acting player");
                    var buyers = SimQueries.TaggedShips(sim, Buyer.Tag);
                    if (buyers.Count == 0) throw new InvalidOperationException("no Buyer in this system");
                    var buyer = buyers[0];
                    float shipRadius = ship.DetWorldBoundingCircle.Radius;
                    float gap = buyer.Live is { } loaded ? loaded.DetWorldBoundingCircle.Radius + shipRadius + 100f : 1200f;
                    // Each player gets its own distance, so two ships do not land on each other.
                    gap += Autotest.ActingPlayerIndex * (2f * shipRadius + 50f);
                    Autotest.Teleport(ship, Autotest.ClearSpotNear(sim, buyer.Location, gap, shipRadius));
                    Log.Info($"AUTOTEST goto: ship {ship.Name} to {ship.WorldCenter}, Buyer at {buyer.Location}, loaded={buyer.Live is not null}");
                    break;
                }
                // Test-only: a real sale of tech from the acting player's first ship through the trade input.
                case ["sell", var count]:
                {
                    var sim = run.Game.Sim;
                    var ship = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no ship of the acting player");
                    var buyer = sim.Ships.FirstOrDefault(Buyer.IsBuyer) ?? throw new InvalidOperationException("no loaded Buyer ship");
                    var res = Tech.Vaults.TechId;
                    int units = int.Parse(count, CultureInfo.InvariantCulture);
                    var source = ship.Resources.GetBestSendingSource(res, MPValueType.Confirmed, out int have)
                        ?? throw new InvalidOperationException($"no {res} on the ship");
                    var sink = buyer.Resources.GetBestReceivingSink(res, units, MPValueType.Confirmed, out int capacity,
                        includeAnticipatedDeliveries: false, externalDelivery: true)
                        ?? throw new InvalidOperationException($"the Buyer has no room for {res}");
                    int amount = Math.Min(units, Math.Min(have, capacity));
                    int price = sim.Mode.GetResourceSellRefund(res, buyer.Metadata.PlayerIndex);
                    Log.Info($"AUTOTEST sell {amount} {res} to the Buyer at {price} each");
                    run.Game.NetManager.EnqueueInput(new MPMoveResourcesInput(source, sink, amount, ship.Jobs.AllocLocalJobID(),
                        tradeType: TradeType.Sell, pricePerResource: price));
                    break;
                }
                default:
                    throw new InvalidOperationException("buyer needs: spawn | deliver <units> | goto | sell <units>");
            }
        };
        // Single player only: jumps the human ships to an unvisited neighbor system, without the FTL animation or fuel.
        // "jump back": to a visited neighbor instead (the system the ships came from).
        Autotest.Commands["jump"] = arg =>
        {
            var run = Autotest.RequireRun();
            bool back = arg.Trim() == "back";
            var next = run.Game.CurrentMapNode.Connections.FirstOrDefault(n => run.State.VisitedNodes.Contains(n.UniqueID) == back)
                ?? throw new InvalidOperationException(back ? "no visited neighbor system" : "no unvisited neighbor system");
            Autotest.JumpWhenAllowed(run, next, "jump");
        };
        Autotest.Commands["maptiers"] = _ =>
        {
            var run = Autotest.RequireRun();
            foreach (var band in run.Game.Map.Nodes.GroupBy(RunMap.Band).OrderBy(g => g.Key))
            {
                var tiers = band.Select(n => LowTier(run, n)).Order().ToList();
                Log.Info($"AUTOTEST MAP band={band.Key} nodes={tiers.Count} lowTiers=[{string.Join(",", tiers)}] " +
                         $"links=[{string.Join(" ", band.Select(n => string.Join(",", n.Connections.Select(RunMap.Band))))}]");
            }
        };
        Autotest.Commands["rewardcheck"] = _ =>
        {
            var run = Autotest.RequireRun();
            var game = run.Game;
            float scale = run.T.Journey.RewardScale;
            var econ = run.Mode.EconDifficulty;
            var players = new List<PlayerState>();
            game.GetHumanPlayerStates(players, deterministic: true);
            int moneyBefore = players.Sum(p => p.Money);
            var mission = new Mission(game, Buyer.MissionType);
            new MoneyReward(mission, 1000).GiveReward(game);
            int moneyGained = players.Sum(p => p.Money) - moneyBefore;
            float missionScale = run.T.Journey.MissionMoneyScale;
            int expectedMoney = (int)(1000f * (econ.MoneyRewardFactor * scale * missionScale)) / players.Count * players.Count;
            Autotest.Check(moneyGained == expectedMoney, $"money reward 1000 x econ {econ.MoneyRewardFactor} x scale {scale} x mission money {missionScale} = {expectedMoney} (actual {moneyGained})");
            int fameBefore = run.Mode.PlayerFame;
            new FameReward(10).GiveReward(game);
            int fameGained = run.Mode.PlayerFame - fameBefore;
            int expectedFame = (int)(10f * (econ.FameRewardFactor * scale));
            Autotest.Check(fameGained == expectedFame, $"fame reward 10 x econ {econ.FameRewardFactor} x scale {scale} = {expectedFame} (actual {fameGained})");
        };

        Autotest.Fields["buyers"] = () => Buyer.Count(Autotest.RequireRun().Game.Sim);
        Autotest.Fields["buyermission"] = () =>
            Autotest.RequireRun().Mode.Missions.Count(m => m.MetatypeID == Buyer.MissionType);
        // 1 if the loaded Buyer trades with the first human ship in the system.
        Autotest.Fields["buyertrades"] = () =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            var buyer = sim.Ships.FirstOrDefault(Buyer.IsBuyer) ?? throw new InvalidOperationException("no loaded Buyer ship");
            var human = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no ship of the acting player");
            return buyer.AI?.WillTradeWith(human) == true ? 1 : 0;
        };
        Autotest.Fields["nodes"] = () => Autotest.RequireRun().Game.Map.Nodes.NodeCount;
        // Spec 2414: sector types on the run map that are not basic (the starting system is always basic).
        Autotest.Fields["mapnonbasic"] = () =>
        {
            var run = Autotest.RequireRun();
            return run.Game.Map.Nodes.Count(n => run.Mode.GetMapNodeData(n).SectorType.ID.ToString() != "basic");
        };
        Autotest.Fields["runmap"] = () => Autotest.RequireRun().Mode.Init is Cosmoteer.Modes.Career.CareerInit init && init.MapSizeIndex == RunMapSizeIndex() ? 1 : 0;
        // Spec 2552: the low tier of the Buyer's system, the last band.
        Autotest.Fields["finaltier"] = () =>
        {
            var run = Autotest.RequireRun();
            return LowTier(run, RunMap.BuyerNode(run) ?? throw new InvalidOperationException("not the run map"));
        };
        Autotest.Fields["mapsteps"] = () => RunMap.Steps(Autotest.RequireRun());
        // Spec 2550: systems on the map minus the lens widths of its steps. Expect 0.
        Autotest.Fields["mapsizegap"] = () =>
        {
            var run = Autotest.RequireRun();
            int steps = RunMap.Steps(run);
            return run.Game.Map.Nodes.NodeCount - Enumerable.Range(0, steps).Sum(b => RunMapModel.Width(b, steps));
        };
        // Spec 2553: the band of the starting system. Expect 0.
        Autotest.Fields["startband"] = () =>
        {
            var run = Autotest.RequireRun();
            return RunMap.Band(run.Game.Map.Nodes.Single(n => run.Mode.GetMapNodeData(n).IsInitNode));
        };
        // Sell price to the Buyer divided by the sell price with no destination. Expect BuyerPriceFactor.
        Autotest.Fields["buyerprice"] = () =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            double toBuyer = sim.Mode.GetResourceSellRefund(Tech.Vaults.TechId, PlayerIndexes.Neutral);
            double plain = sim.Mode.GetResourceSellRefund(Tech.Vaults.TechId, null);
            return plain > 0 ? toBuyer / plain : throw new InvalidOperationException("tech has no sell price");
        };
        Autotest.Fields["delivered"] = () => Autotest.RequireRun().State.DeliveredUnits;
        Autotest.Fields["score"] = () => Autotest.RequireRun().State.Score;
    }
}

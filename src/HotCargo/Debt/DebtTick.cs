using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.Random;
using HotCargo.Core;

namespace HotCargo.Debt;

/// <summary>Sim queries about lenders and their ships. Sim state only, so every machine gives the same answer.</summary>
static class Lenders
{
    static readonly ID<SimObjectSpawner> TradeStationTag = new("trade_station");
    static readonly ID<SimObjectSpawner> BuyerTag = new("hc_buyer");
    const ulong OfferSalt = 0x4445_4254_4C4FUL;

    public static FactionRules? FactionOf(Ship ship) => FactionRules.GetFaction(ship.Metadata.PlayerIndex);

    public static string Name(string faction) => GameApp.Rules.GetFactionRules(new ID<FactionRules>(faction)).NameKey.Text;

    static bool IsStation(Ship ship) => SpaceStation.GetStation(ship, onlyOperating: true) is not null;

    /// <summary>Spec 1402: a faction trade station that offers this player a loan now.</summary>
    public static bool OffersLoan(Run run, Ship station, int player) =>
        run.State.Result == RunResult.Running && run.T.Debt.Enabled && station.HasTag(TradeStationTag) && !station.HasTag(BuyerTag)
        && FactionOf(station) is { } f && run.State.Debt.Of(f.ID.ToString(), player) is null && IsStation(station)
        && DebtModel.Offers(new FastRand((uint)station.UniqueID, OfferSalt).Single(), run.T.Debt.LoanStationChance);

    const ulong RefinanceSalt = 0x4445_4254_5245_46UL;

    /// <summary>Spec 1421: this player's debts that a trade station of another faction offers to buy now, in list order.</summary>
    public static List<Debt> Refinances(Run run, Ship station, int player)
    {
        if (run.State.Result != RunResult.Running || !run.T.Debt.Enabled || run.State.Debt.Debts.Count == 0 || !station.HasTag(TradeStationTag)
            || station.HasTag(BuyerTag) || FactionOf(station) is not { } f || !IsStation(station)) return [];
        string id = f.ID.ToString();
        if (run.State.Debt.Of(id, player) is not null || Factions.FactionsArea.Roles(run).Ally.ID.ToString() == id
            || new FastRand((uint)station.UniqueID, RefinanceSalt).Single() >= run.T.Debt.RefinanceStationChance) return [];
        return run.State.Debt.OfPlayer(player).Where(d => d.Faction != id).ToList();
    }

    /// <summary>
    /// Spec 1405 and 1413: the debt that a collector asks for, or this player's debt that a station of the lender takes payments for, or null.
    /// </summary>
    public static Debt? DebtAt(Run run, Ship ship, int player)
    {
        if (run.State.Result != RunResult.Running) return null;
        var s = run.State.Debt;
        if (s.Collectors.FirstOrDefault(c => c.Ship == ship.UniqueID) is { } c) return s.Of(c.Faction, c.Player);
        return FactionOf(ship) is { } f && s.Of(f.ID.ToString(), player) is { } d && IsStation(ship) ? d : null;
    }

    /// <summary>
    /// Spec 1417: who lends when money goes below 0. The system's controlling faction, else the faction of the operating station
    /// nearest to the player's ships, else the corporation's rival.
    /// </summary>
    public static string OverdraftLender(Run run, SimRoot sim, int player)
    {
        if (sim.MapNode?.ControllingFaction is { } owner) return owner.ToString();
        var ships = sim.Ships.HumanPlayerShips.Where(h => h.Metadata.PlayerIndex == player).Select(h => h.DetWorldCenter).ToList();
        var station = ships.Count == 0 ? null : SpaceStation.GetStations(sim, onlyOperating: true).Select(st => st.PhysicalShip).OfType<Ship>()
            .Where(st => FactionOf(st) is not null)
            .OrderBy(st => ships.Min(h => h.DistanceSquaredTo(st.DetWorldCenter))).ThenBy(st => st.UniqueID).FirstOrDefault();
        return (station is not null ? FactionOf(station)! : Factions.FactionsArea.Roles(run).Rival).ID.ToString();
    }

    public static Collector? CollectorOf(Run run, Ship ship) => run.State.Debt.Collectors.FirstOrDefault(c => c.Ship == ship.UniqueID);

    public static float Ratio(Debt d) => DebtModel.Ratio(d.Balance, d.Principal);

    public static DebtStage StageOf(Run run, Debt d) => DebtModel.Stage(Ratio(d), run.T.Debt.FirmRatio, run.T.Debt.HostileRatio);

    static WantedManager Wanted(SimRoot sim) => ((CareerSimModeManager)sim.Mode).Wanted;

    public static bool Wants(SimRoot sim, string faction) => Wanted(sim)._wantedShips.ContainsKey(new ID<FactionRules>(faction));

    /// <summary>
    /// Spec 1409: the lender wants the players in this system. Every human ship gets a vanilla wanted entry, last seen now, so the
    /// search runs for the vanilla time and the police come to the last known spot. The faction's ships that see the players keep it on.
    /// </summary>
    public static void Want(SimRoot sim, string faction)
    {
        var wanted = Wanted(sim);
        var id = new ID<FactionRules>(faction);
        if (!wanted._wantedShips.TryGetValue(id, out var list)) wanted._wantedShips.Add(id, list = []);
        foreach (var ship in sim.Ships.HumanPlayerShips)
            if (!list.Any(i => i.Ship == ship))
                list.Add(new WantedManager.WantedInfo(ship) { LastKnownLocation = ship.DetWorldCenter, LastSeenTime = sim.LogicalTime });
    }
}

/// <summary>Spec 1404 to 1410: interest, stages, collectors and the wanted state, in the sim tick.</summary>
static class DebtTick
{
    static readonly ID<BuiltinShipTag> Combat = new("combat");

    public static void OnTick(Run run, SimRoot sim, double dt)
    {
        if (run.State.Result != RunResult.Running) return;
        var s = run.State.Debt;
        var t = run.T.Debt;
        double now = run.State.GameSeconds;
        // Spec 1417: a stable order, so every machine opens the same debts.
        foreach (int player in run.Game.HumanPlayersIndexes.Order())
            if (run.Game.GetPlayerState(player, deterministic: true).Money < 0) Overdraft(run, sim, player);
        foreach (var d in s.Debts)
        {
            while (now >= d.NextInterestAt)
            {
                d.Balance = DebtModel.Interest(d.Balance, t.InterestRate * d.RateFactor);
                d.NextInterestAt += t.InterestSeconds;
            }
            var stage = Lenders.StageOf(run, d);
            if (stage > d.Stage) StageRose(run, sim, d, stage);
            d.Stage = stage;
            bool wanted = Lenders.Wants(sim, d.Faction);
            if (d.WasWanted && !wanted) d.NextCollectorAt = Math.Min(d.NextCollectorAt, now + t.ReturnSeconds);
            d.WasWanted = wanted;
            bool collectorHere = s.Collectors.Any(c => c.Faction == d.Faction && c.Player == d.Player);
            if (!wanted && now >= d.NextCollectorAt && !collectorHere && CollectorAnchor(sim, d.Player) is { } anchor)
            {
                SpawnCollector(run, sim, d, anchor.DetWorldCenter, overdraft: false);
                d.NextCollectorAt = now + t.CollectorSeconds;
            }
            // Spec 1418: one hail for each overdraft debt, when it comes due.
            if (d.HailAt is { } hailAt && now >= hailAt)
            {
                d.HailAt = null;
                if (d.Balance > t.OverdraftHailMin && !wanted && !collectorHere && CollectorAnchor(sim, d.Player) is { } owner)
                {
                    SpawnCollector(run, sim, d, owner.DetWorldCenter, overdraft: true);
                    Telemetry.Tele.Emit(Telemetry.Cat.Economy, "debt.hail", d, static (w, d) =>
                    {
                        w.WriteNumber("player", d.Player);
                        w.WriteString("lender", d.Faction);
                        w.WriteNumber("balance", d.Balance);
                        w.WriteString("cause", "overdraft");
                    });
                }
                else Log.Info($"debt: no overdraft hail for player {d.Player} and {d.Faction}, balance {d.Balance}");
            }
        }
        TickCollectors(run, sim, now);
        DebtMissions.Tick(run, sim);
        DebtHelp.Tick(run, sim, now);
    }

    /// <summary>Spec 1323 and 1401: the collector comes to a ship of the player who owes the debt. Null while that player has no ship here.</summary>
    static Ship? CollectorAnchor(SimRoot sim, int player) =>
        SimQueries.AnchorShip(sim, s => s.Metadata.PlayerIndex == player ? 1f : 0f) is { } ship && ship.Metadata.PlayerIndex == player ? ship : null;

    /// <summary>
    /// Spec 1417: the player's money below 0 goes back to 0, and the shortfall goes on the player's debt with the lender. A new debt
    /// comes due for the lender's hail (spec 1418).
    /// </summary>
    static void Overdraft(Run run, SimRoot sim, int player)
    {
        var state = run.Game.GetPlayerState(player, deterministic: true);
        int shortfall = -state.Money;
        string lender = Lenders.OverdraftLender(run, sim, player);
        bool isNew = run.State.Debt.Of(lender, player) is null;
        state.AddMoney(shortfall);
        var d = DebtDeals.Owe(run, lender, player, shortfall);
        if (isNew) d.HailAt = run.State.GameSeconds + run.T.Debt.OverdraftHailSeconds;
        DebtArea.Chat(run, DebtArea.Format("OverdraftFmt", run.IsMultiplayer ? DebtArea.PlayerName(run, player) : DebtArea.Text("You"), Cosmoteer.Extensions.ToXmlCostString(shortfall), Lenders.Name(lender)));
        Telemetry.Tele.Emit(Telemetry.Cat.Economy, "debt.overdraft", (d, shortfall, isNew), static (w, x) =>
        {
            w.WriteNumber("player", x.d.Player);
            w.WriteString("lender", x.d.Faction);
            w.WriteNumber("amount", x.shortfall);
            w.WriteNumber("balance", x.d.Balance);
            w.WriteBoolean("new", x.isNew);
        });
        Log.Info($"debt: player {player} overdraft {shortfall} on {lender}, balance {d.Balance}, new {isNew}");
    }

    /// <summary>Collectors belong to the system they came to.</summary>
    public static void OnSystemEntered(Run run, bool firstVisit)
    {
        run.State.Debt.Collectors.Clear();
        // Spec 1433: helpers stay behind and ask nothing.
        run.State.Debt.Help = null;
    }

    static void StageRose(Run run, SimRoot sim, Debt d, DebtStage stage)
    {
        string name = Lenders.Name(d.Faction);
        string ratio = Lenders.Ratio(d).ToString("0.0");
        if (stage == DebtStage.Hostile)
        {
            Lenders.Want(sim, d.Faction);
            DebtArea.Center(run, DebtArea.Format("HostileCenterFmt", name));
        }
        DebtArea.Chat(run, DebtArea.Format(stage == DebtStage.Hostile ? "HostileChatFmt" : "FirmChatFmt", name, ratio));
        Onboarding.RunLog.Add(run, Onboarding.RunLogKind.Debt, DebtArea.Format(stage == DebtStage.Hostile ? "LogHostileFmt" : "LogFirmFmt", name, ratio));
        Log.Info($"debt: {d.Faction} stage {stage} at ratio {ratio}");
    }

    static void TickCollectors(Run run, SimRoot sim, double now)
    {
        var list = run.State.Debt.Collectors;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (sim.Ships[c.Ship] is not { } ship || !ship.ExistsInSim(sim) || ship.IsJunk)
            {
                list.RemoveAt(i--);
                continue;
            }
            // Paid off at a station: nothing left to collect.
            if (run.State.Debt.Of(c.Faction, c.Player) is null) { Leave(run, sim, c, ship); i--; continue; }
            // Spec 1407: it flies to the debtor's nearest ship until it is in comms range (a small ship's comms reach about 400).
            if (!c.Hailed && !(c.Hailed = HailDebtor(sim, ship, c.Player)) && NearestShipOf(sim, c.Player, ship.DetWorldCenter) is { } target)
                Hunters.HunterOrders.MoveTo(ship, target.DetWorldCenter, 150f);
            if (now >= c.LeaveAt) { Leave(run, sim, c, ship); i--; }
        }
    }

    static Ship? NearestShipOf(SimRoot sim, int player, Vector2 at) =>
        sim.Ships.HumanPlayerSignificantShips.Where(h => h.Metadata.PlayerIndex == player)
            .OrderBy(h => h.DetWorldCenter.DistanceSquaredTo(at)).ThenBy(h => h.UniqueID).FirstOrDefault();

    /// <summary>
    /// Spec 1407: the collector hails the debtor's nearest ship that can call it back, like an event carrier (EventHail.TryHail, which
    /// picks any player's ship). False while no such ship is in range.
    /// </summary>
    static bool HailDebtor(SimRoot sim, Ship collector, int player)
    {
        var mode = (CareerSimModeManager)sim.Mode;
        var target = sim.Ships.HumanPlayerSignificantShips
            .Where(h => h.Metadata.PlayerIndex == player && mode.CanShipCallShip(h, collector) && mode.WillShipAnswerCall(h, collector))
            .OrderBy(h => h.DetWorldCenter.DistanceSquaredTo(collector.DetWorldCenter)).ThenBy(h => h.UniqueID).FirstOrDefault();
        if (target is null) return false;
        mode.AIHailPlayer(collector, target);
        return true;
    }

    /// <summary>
    /// Spec 1407 and 1410: the collector goes. At the hostile stage, a collector that leaves without a payment below the hostile ratio
    /// makes the lender want the players, and it stays to fight. Otherwise it jumps out. Removes it from the list.
    /// </summary>
    public static void Leave(Run run, SimRoot sim, Collector c, Ship ship)
    {
        run.State.Debt.Collectors.Remove(c);
        string name = Lenders.Name(c.Faction);
        if (run.State.Debt.Of(c.Faction, c.Player) is { } d && Lenders.StageOf(run, d) == DebtStage.Hostile)
        {
            Lenders.Want(sim, c.Faction);
            d.WasWanted = true;
            DebtArea.Center(run, DebtArea.Format("HostileCenterFmt", name));
            DebtArea.Chat(run, DebtArea.Format("CollectorTurnsFmt", ship.Metadata.PlainShipName, name));
            Log.Info($"debt: collector {ship.Metadata.PlainShipName} of {c.Faction} turns hostile");
            return;
        }
        Pursuit.Removals.JumpOut(run, ship);
        DebtArea.Chat(run, DebtArea.Format("CollectorLeavesFmt", ship.Metadata.PlainShipName));
    }

    /// <summary>Spec 1407 and 1418: one combat ship of the lender near the debtor's ship.</summary>
    static void SpawnCollector(Run run, SimRoot sim, Debt d, Vector2 center, bool overdraft)
    {
        var t = run.T.Debt;
        var ship = SpawnFactionShip(run, sim, d.FactionId, center, t.CollectorDistance);
        run.State.Debt.Collectors.Add(new Collector
        {
            Ship = ship.UniqueID, Faction = d.Faction, Player = d.Player, Overdraft = overdraft, LeaveAt = run.State.GameSeconds + t.CollectorWaitSeconds,
        });
        DebtArea.Chat(run, DebtArea.Format("CollectorArrivesFmt", ship.Metadata.PlainShipName, Lenders.Name(d.Faction)));
        Log.Info($"debt: collector {ship.Metadata.PlainShipName} of {d.Faction} arrived, balance {d.Balance}, stage {Lenders.StageOf(run, d)}");
    }

    /// <summary>
    /// Spec 1407 and 1431: a combat ship of a faction (its military player index, the sector tier or the nearest lower tier), with the
    /// FTL jump-in effect, at this distance from the center.
    /// </summary>
    public static Ship SpawnFactionShip(Run run, SimRoot sim, ID<FactionRules> factionId, Vector2 center, float distance)
    {
        var faction = GameApp.Rules.GetFactionRules(factionId);
        BuiltinShipRules? built = null;
        for (int tier = Math.Max(1, run.CurrentTier); tier >= 1 && built is null; tier--)
            built = GameApp.Rules.BuiltinShips.GetRandomMatchingShip(sim.Rand, [faction.ID], new Range<int>(tier, tier), null, [Combat]);
        if (built is null) throw new InvalidOperationException($"no built-in combat ship of {faction.ID} at tier {run.CurrentTier} or below");
        var wanted = GameApp.Rules.CareerMode.Wanted;
        Vector2 loc = center;
        // Same retry rule as police spawns (WantedManager.GetPoliceSpawnLoc): avoid suns, give up after 100 tries.
        for (int i = 0; i < 100; i++)
        {
            loc = center + sim.Rand.Normal() * distance;
            if (!sim.ShouldAIAvoidLocation(loc, wanted.PoliceSpawnAvoidableDoodadTags, wanted.PoliceSpawnAvoidableDoodadBuffer)) break;
        }
        var spawner = new StasisShipSpawner(sim, built.ID, loc, SpawnedShipFeatures.None, sim.Rand.UInt64())
        {
            AI = new ID<ShipAIRules>("Normal"),
            Direction = loc.DirectionTo(in center),
            ObjectAllegiance = faction.MilitaryPlayerIndex,
        };
        spawner.Spawn(sim);
        var ship = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{built.ID} of {faction.ID} did not spawn");
        _ = ship.Renderer.AnimateFtlJumpIn(ship.WorldFlightDirection);
        return ship;
    }
}

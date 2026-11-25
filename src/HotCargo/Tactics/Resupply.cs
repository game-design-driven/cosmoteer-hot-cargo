using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Repairs;
using Halfling.Geometry;
using HotCargo.Autonomy;
using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

/// <summary>
/// Spec 1305-1308. What a damaged AI ship lacks for its repairs, and the ships that can give it: faction mates and friendly stations.
/// Reads and changes sim state only. Called from the AI tick, so it runs the same on every client.
/// </summary>
internal static class Resupply
{
    /// <summary>Every repair of the ship as one vanilla update: damaged parts, destroyed parts and destroyed doors.</summary>
    static IEnumerable<object> RepairObjects(Ship ship)
    {
        foreach (var p in ship.DestroyedParts) yield return p;
        foreach (var d in ship.DestroyedDoors) yield return d;
        foreach (var p in ship.Parts.RepairableParts.DamagedRepairableParts) yield return p;
    }

    /// <summary>Adds one repair to the update. False when the game refuses it (a rebuild that something blocks).</summary>
    static bool AddRepair(ShipUpdateInfo info, object repair) => repair switch
    {
        DestroyedPart d => info.TryRebuildPart(d, rebuildDoors: false, hireCrew: false, out _),
        DestroyedDoor d => info.TryRebuildDoor(d, out _),
        Part p => Repair(info, p),
        _ => throw new InvalidOperationException($"unexpected repair object {repair.GetType()}"),
    };

    static bool Repair(ShipUpdateInfo info, Part part)
    {
        info.RepairPart(part);
        return true;
    }

    /// <summary>
    /// Spec 1305: the resources that the ship lacks for all its repairs, by resource ID. ownOnly: count only the ship's own storage,
    /// not the loose nuggets and own-player ships nearby that the vanilla repair counts too. Crews reach those only through airlocks.
    /// </summary>
    public static Dictionary<string, int> Lacking(Ship ship, bool ownOnly = false)
    {
        var cost = new Dictionary<string, int>();
        using (var info = ShipUpdateInfo.Alloc(ship))
        {
            foreach (var repair in RepairObjects(ship).ToList()) AddRepair(info, repair);
            // returnFinalCost: false. The final cost turns resources that a trading station in range sells into credits.
            using var total = info.GetTotalPhysicalCost(MPValueType.Confirmed, returnFinalCost: false, isBuild: false, refundStoredResources: false, playerIndex: ship.Metadata.PlayerIndex);
            foreach (var (type, n) in total.Resources)
                if (n > 0) cost[type.ToString()] = n;
        }
        return RepairModel.Lacking(cost, type => ownOnly ? Stock(ship, new ID<ResourceRules>(type))
            : ship.Resources.GetUsableConstructionResourceTotal(new ID<ResourceRules>(type), includeAnticipated: true));
    }

    public static bool ShortOfCrew(Ship ship, TacticsTunables t) =>
        RepairModel.ShortOfCrew(ship.Crew.Assigned.Count, ship.Crew.TotalCrewCapacity, t.FallBackCrewFraction);

    /// <summary>Units of a resource in the ship's storage. Carried units and construction stock do not count.</summary>
    static int Stock(Ship ship, ID<ResourceRules> type) => ship.Resources.GetResourceTotal(type, includeStored: true, includeCarried: false);

    /// <summary>
    /// Spec 1307: the nearest faction mate in range (ResourcePool.Pools) that holds a resource that the ship lacks.
    /// Ties go to the lower ID.
    /// </summary>
    public static Ship? FindPoolMate(Ship ship, IReadOnlyDictionary<string, int> lacking, TacticsTunables t)
    {
        if (lacking.Count == 0 || !t.FactionPoolEnabled) return null;
        var types = lacking.Keys.Select(k => new ID<ResourceRules>(k)).ToList();
        Ship? best = null;
        float bestDistance = float.MaxValue;
        foreach (var other in HunterOrders.Sim(ship).Ships)
        {
            if (other == ship || !other.IsSignificant || !ResourcePool.Pools(ship, other)) continue;
            float d = HunterOrders.EdgeDistance(ship, other);
            if (d > t.FallBackPoolRange || d > bestDistance || (d == bestDistance && best is not null && other.UniqueID.CompareTo(best.UniqueID) > 0)) continue;
            if (!types.Any(type => Stock(other, type) > 0)) continue;
            (best, bestDistance) = (other, d);
        }
        return best;
    }

    /// <summary>True when the two ships are within the vanilla pool and transfer distance (crew rules, 400 edge to edge).</summary>
    public static bool InPoolRange(Ship a, Ship b) =>
        a.DetWorldBoundingCircle.DistanceTo(b.DetWorldBoundingCircle) <= GameApp.Rules.Crew.LoseHomeShipWarningDistance;

    /// <summary>
    /// Spec 1308: the stations that trade with the ship now: live stations of requirement 1069's list. A station in stasis never trades,
    /// and an AI ship that comes near does not wake it as a player ship does (#343: a fleeing ship waited next to one and never docked).
    /// </summary>
    static IEnumerable<StationRef> Trading(Ship ship) => AutonomyWorld.Stations(ship).Where(s => s.Live is { } live && Situation.IsStation(live));

    /// <summary>Spec 1308: the nearest station that trades with the ship now. Ties go to the lower ID.</summary>
    public static StationRef? FindStation(Ship ship) =>
        Trading(ship).OrderBy(s => Math.Max(0f, AutonomyWorld.EdgeDistance(ship, s.Center) - s.Radius)).ThenBy(s => s.Id).Cast<StationRef?>().FirstOrDefault();

    /// <summary>The station with this ID, while it still trades with the ship: friendly and live.</summary>
    public static StationRef? Station(Ship ship, ObjectID id) => Trading(ship).Where(s => s.Id == id).Cast<StationRef?>().FirstOrDefault();

    /// <summary>True when the live station allows trade with the ship (vanilla AllowResourceTrade: friendly and within 400).</summary>
    public static bool AtStation(Ship ship, StationRef station) =>
        station.Live is { } live && HunterOrders.Sim(ship).Mode.AllowResourceTrade(ship, live);

    /// <summary>
    /// Spec 1308: the station supplies the ship for free: each resource that its own storage lacks, as far as its storage has room,
    /// and crew for the free crew places. Returns the units and crew given.
    /// </summary>
    public static (int Units, int Crew) Supply(Ship ship)
    {
        int units = 0;
        foreach (var (id, need) in Lacking(ship, ownOnly: true).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            units += ship.Resources.AddResources(new ID<ResourceRules>(id), need, excludeConstructionSinks: true);
        int crew = ship.Crew.CreateCrewsAtRandomParts(ship.Crew.RemainingCrewCapacity);
        return (units, crew);
    }

    /// <summary>
    /// Spec 1308: the station repairs up to count of the ship's repairs at once, for free. Returns the repairs done.
    /// The vanilla crew repair cannot do this job for many AI ships: combat ships often have no storage bays, crews reach the
    /// station only through airlocks, and next to a trading station the vanilla repair module asks for credits, which an AI player
    /// does not have (AISelfRepairModule.TryRepairObject with the final cost of ShipUpdateInfo.GetTotalPhysicalCost).
    /// </summary>
    public static int StationRepair(Ship ship, int count)
    {
        int done = 0;
        foreach (var repair in RepairObjects(ship).ToList())
        {
            if (done >= count) break;
            using var info = ShipUpdateInfo.Alloc(ship);
            if (!AddRepair(info, repair) || !info.CanApplyPhysical()) continue;
            info.ApplyPhysical(destroyBlocking: false, builtByPlayer: false, updateDestroyed: true);
            done++;
        }
        return done;
    }

    /// <summary>
    /// The players' combat block rule (CareerSimModeManager.GetConstructionLegality, used there only for the instant build modes):
    /// an enemy ship within EnemyCombatNoBuildRadius fired in the last EnemyCombatNoBuildDuration (combat difficulty rules).
    /// Reads sim state only.
    /// </summary>
    public static bool InCombat(Ship ship) => CombatEnemy(ship) is not null;

    /// <summary>The first enemy that puts the ship in combat (InCombat), or null.</summary>
    static Ship? CombatEnemy(Ship ship)
    {
        var sim = HunterOrders.Sim(ship);
        if (sim.Mode is not Cosmoteer.Modes.Career.CareerSimModeManager career) return null;
        var rules = career.CombatDifficulty;
        if (rules.EnemyCombatNoBuildRadius <= 0) return null;
        var found = new List<Ship>();
        sim.Ships.DetFindIntersectingWith(new Circle(ship.DetWorldCenter, rules.EnemyCombatNoBuildRadius), found, approximate: true, null,
            s => s.IsEnemiesWith(ship) && s.Weapons.LastWeaponFireTime + rules.EnemyCombatNoBuildDuration >= sim.LogicalTime);
        return found.OrderBy(s => s.UniqueID).FirstOrDefault();
    }

    /// <summary>Tactics log event of repairs that a ship in combat cancels (autotest field tactic_repair_hold).</summary>
    public const string RepairHoldEvent = "repair_hold";

    /// <summary>
    /// Spec 1317: an AI ship in combat (InCombat) does not repair, unless it repairs in cover behind its rock (spec 1306), next to its
    /// faction mate (spec 1307) or at its station (spec 1308, 1315).
    /// </summary>
    public static bool RepairBlocked(Ship ship, TacticsTunables t) => t.RepairCombatHoldEnabled && !RepairsAnyway(ship) && InCombat(ship);

    static bool RepairsAnyway(Ship ship) =>
        ship.AI?.GetStrategyModule<FallBackStrategy>() is { } fallBack && (fallBack.InCover(ship) || fallBack.AtStation(ship) || fallBack.AtPoolMate(ship))
        || ship.AI?.GetStrategyModule<Ai.FleeStrategy>()?.AtStation(ship) == true;

    /// <summary>Spec 1317: a ship in combat cancels its queued repairs (ConstructionManager.CancelAll), so its crew stop work and fight.</summary>
    public static void HoldRepairsInCombat(Ship ship, TacticsTunables t)
    {
        if (ship.OptionalConstructionManager is not { HasAnyQueued: true } queue || !RepairBlocked(ship, t)) return;
        int count = QueuedRepairs(ship);
        queue.CancelAll();
        TacticsLog.Event(ship, RepairHoldEvent, $"cancels {count} repairs in combat with {CombatEnemy(ship)?.Metadata.PlainShipName}");
    }

    /// <summary>Queued repairs and rebuilds of the ship.</summary>
    public static int QueuedRepairs(Ship ship) => ship.OptionalConstructionManager is { } cm
        ? cm.QueuedPartRepairs.Count + cm.QueuedPartConstructions.Count(c => c.IsRepair) + cm.QueuedDoorConstructions.Count(c => c.IsRepair)
        : 0;

    /// <summary>Tactics log event of a station supply (autotest field tactic_fallback_supply).</summary>
    public const string SupplyEvent = "fallback_supply";

    /// <summary>
    /// Spec 1308, 1315: one AI tick of a station visit. The ship flies next to the station; there the station supplies it once
    /// (supplied turns true) and repairs it, and the ship holds still. threat: the ship keeps its strong side to it on the way.
    /// </summary>
    public static void VisitStation(Ship ship, StationRef station, ref bool supplied, Ship? threat, TacticsTunables t)
    {
        if (!AtStation(ship, station))
        {
            Ai.PersonalityStrategy.MoveGuarded(ship, Beside(ship, station.Center, station.Radius, t.FallBackDockGap), t.FallBackDockGap * 0.5f, threat, t);
            return;
        }
        if (!supplied)
        {
            var (units, crew) = Supply(ship);
            supplied = true;
            TacticsLog.Event(ship, SupplyEvent, $"station {station.Live?.Metadata.PlainShipName} supplies {units} units and {crew} crew");
        }
        StationRepair(ship, t.FallBackStationRepairsPerTick);
        HoldStill(ship);
    }

    /// <summary>Stops the ship where it is: no move or attack command.</summary>
    public static void HoldStill(Ship ship)
    {
        if (ship.Commands.CurConfirmedCommand is not null) ship.Commands.Clear();
    }

    /// <summary>A spot gap from the target's hull on the ship's side, so the ship stops next to it and not inside it.</summary>
    public static Vector2 Beside(Ship ship, Vector2 center, float radius, float gap)
    {
        var dir = center.DistanceTo(ship.DetWorldCenter) < 1f ? new Vector2(1f, 0f) : Vector2.FromAngle(center.DirectionTo(ship.DetWorldCenter));
        return center + dir * (radius + ship.WorldBoundingRadius + gap);
    }
}

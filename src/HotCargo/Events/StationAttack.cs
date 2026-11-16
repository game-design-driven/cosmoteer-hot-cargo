using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.StrategyModules;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Repairs;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Cosmoteer.Localization;
using Halfling;
using Halfling.Geometry;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Debris;
using HotCargo.Diplomacy;
using HotCargo.Hunters;
using HotCargo.Pursuit;

namespace HotCargo.Events;

/// <summary>
/// Spec 90 to 98: a strong raider group attacks a trade station in the players' system. The raiders go for the station and shoot a
/// player ship only when it is in their way. The station hails and shows a tag, and the chat says what goes on. After the attack the
/// station pulls in the debris near it and rebuilds its destroyed parts from it. All sim logic runs in the sim tick, the AI tick and the damage call,
/// and reads and writes the run state (EventsState.Attack), so every co-op machine does the same.
/// </summary>
static class StationAttack
{
    internal static readonly ID<SimObjectSpawner> RaiderTag = new("hc_ev_station_raider");
    static readonly ID<SimObjectSpawner> StationTag = new("trade_station");
    static readonly ID<SimObjectSpawner> DefenseTag = new("station_defense");
    /// <summary>Defense platforms within this distance of the station's hull belong to it (vanilla places them close).</summary>
    const float DefenseRadius = 1000f;
    const string Spawn = "StationRaiders", HailEvent = "station_attack";
    /// <summary>Game seconds between two checks of a running attack.</summary>
    const double CheckSeconds = 1;
    /// <summary>The raiders spawn this far outside the station's hull, on the far side from the players.</summary>
    const float SpawnGap = 300f;
    /// <summary>Game seconds before the raiders that are left jump out, once the attack ends.</summary>
    const double LeaveDelaySeconds = 5;

    static string Text(string key, params object?[] args) => Strings.FormatText("HotCargoStationAttack/" + key, args);

    /// <summary>UI only: every machine shows the same line in the center and in the chat.</summary>
    static void Say(GameRoot game, string xml)
    {
        Ux.UxKit.CenterText(game, xml);
        Ux.UxKit.ChatLine(game, xml);
    }

    /// <summary>Spec 90: the roll is due a while after the first arrival in a system. A jump leaves a running attack behind.</summary>
    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = run.State.Events;
        s.Attack = null;
        s.AttackDueSeconds = firstVisit ? run.State.GameSeconds + run.T.Events.StationAttackDelaySeconds : -1;
    }

    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("station attack", () =>
    {
        if (sim != run.Game.Sim) return;
        var s = run.State.Events;
        double now = run.State.GameSeconds;
        if (Math.Floor(now / CheckSeconds) != Math.Floor((now - dt) / CheckSeconds) && s.AttackDueSeconds >= 0 && now >= s.AttackDueSeconds
            && Approached(run, sim) is { } approached)
        {
            s.AttackDueSeconds = -1;
            TryStart(run, sim, approached);
        }
        if (s.Attack is not { } a) return;
        double step = a.Phase == AttackPhase.Attack ? CheckSeconds : Math.Max(run.T.Events.StationRebuildStepSeconds, dt);
        if (Math.Floor(now / step) == Math.Floor((now - dt) / step)) return;
        if (a.Phase == AttackPhase.Attack) Watch(run, sim, a);
        else Rebuild(run, sim, a);
    });

    /// <summary>
    /// A live trade station that can be attacked: not the Buyer's, and not one that refuses calls (it could not carry the hail, spec 657).
    /// A station in stasis cannot be attacked or rebuilt.
    /// </summary>
    static bool Attackable(Run run, Ship ship) =>
        ship.HasTag(StationTag) && !ship.HasTag(Journey.Buyer.Tag) && !ship.IsJunk && !Factions.FactionsArea.Refuses(run, null, ship);

    /// <summary>Spec 90: the first attackable station within StationAttackTriggerDistance of a player ship's hull, or null.</summary>
    static Ship? Approached(Run run, SimRoot sim)
    {
        float r = run.T.Events.StationAttackTriggerDistance;
        var players = sim.Ships.HumanPlayerSignificantShips.ToList();
        return sim.Ships.Where(x => Attackable(run, x) && players.Any(p => HunterOrders.EdgeDistance(p, x) <= r)).OrderBy(x => x.UniqueID).FirstOrDefault();
    }

    /// <summary>The attackable station nearest to the players, or null. For the test command sa_start.</summary>
    internal static Ship? NearestStation(Run run, SimRoot sim)
    {
        if (SimQueries.AnchorShip(sim) is not { } anchor) return null;
        return sim.Ships.Where(x => Attackable(run, x)).OrderBy(x => HunterOrders.EdgeDistance(x, anchor)).ThenBy(x => x.UniqueID).FirstOrDefault();
    }

    /// <summary>Spec 90: the one roll of the visit, at the first station that the players come near. Events on, from StationAttackMinSector, the pacer allows one.</summary>
    static void TryStart(Run run, SimRoot sim, Ship station)
    {
        var t = run.T.Events;
        if (!t.Enabled || EventLibrary.LoadError is not null || run.State.SectorIndex < t.StationAttackMinSector) return;
        if (run.State.Events.Attack is not null || !Pacer.MayStart(run, PacedKinds.StationAttack)) return;
        float roll = sim.Rand.Single();
        Log.Info($"station attack: roll {roll:0.000} vs {t.StationAttackChance:0.000} at {station.Metadata.PlainShipName}");
        if (roll < t.StationAttackChance) Start(run, sim, station);
    }

    /// <summary>
    /// Spec 91, 92: spawns the raiders beside the station, on the far side from the nearest player ship, and warns the players. A raider that
    /// spawns out of the live range waits in stasis and is still in the attack. With no live raider there is no attack, and the stasis ships
    /// are removed so they never load as strays.
    /// </summary>
    internal static bool Start(Run run, SimRoot sim, Ship station)
    {
        var witness = sim.Ships.HumanPlayerSignificantShips.OrderBy(p => HunterOrders.EdgeDistance(p, station)).ThenBy(p => p.UniqueID).FirstOrDefault()
            ?? throw new InvalidOperationException("a station attack with no player ship");
        Vector2 away = witness.DetWorldCenter.DistanceSquaredTo(station.DetWorldCenter) > 1f
            ? Vector2.FromAngle(witness.DetWorldCenter.DirectionTo(station.DetWorldCenter)) : new Vector2(1, 0);
        Vector2 at = station.DetWorldCenter + away * (station.WorldBoundingRadius + SpawnGap);
        var before = sim.Ships.Where(x => x.HasTag(RaiderTag)).Select(x => x.UniqueID).ToHashSet();
        var stasisBefore = StasisRaiders(sim).Select(x => x.ReservedObjectID).ToHashSet();
        EventLibrary.Spawn(Spawn).Generate(sim, new FastRand(sim.Rand.UInt64()), loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
        var live = sim.Ships.Where(x => x.HasTag(RaiderTag) && !before.Contains(x.UniqueID)).OrderBy(x => x.UniqueID).ToList();
        var frozen = StasisRaiders(sim).Where(x => !stasisBefore.Contains(x.ReservedObjectID)).ToList();
        if (live.Count == 0)
        {
            foreach (var x in frozen) sim.Stasis.RemoveStasisSpawner(x);
            Log.Info($"station attack: the spawn at {at} made no live ships ({frozen.Count} went into stasis and were removed); no attack");
            return false;
        }
        int down = GunsDown(sim, station, at, run.T.Events.StationAttackGunsDown);
        var s = run.State.Events;
        var raiders = live.Select(x => x.UniqueID).Concat(frozen.Select(x => x.ReservedObjectID)).ToList();
        s.Attack = new StationAttackState { Station = station.UniqueID, Phase = AttackPhase.Attack, Raiders = raiders, Since = run.State.GameSeconds };
        Pacer.Started(run, PacedKinds.StationAttack);
        // Only one event page waits at a time (requirement 70): with another page open, the chat line alone carries the warning.
        if (s.Pending is null) EventRunner.Open(run, sim, EventLibrary.Get(HailEvent), lead: "", carrier: station.UniqueID, carrierHails: true);
        Say(run.Game, Text("StartFmt", station.Metadata.XmlShipName, raiders.Count));
        Log.Info($"station attack: {raiders.Count} raiders ({frozen.Count} in stasis) attack {station.Metadata.PlainShipName}, {down} gun parts down: "
            + string.Join(", ", live.Select(r => $"{r.Metadata.PlainShipName} ({r.Parts.Count} parts)")));
        return true;
    }

    /// <summary>
    /// Spec 91: the raiders' first strike. The given share of the weapon parts of the station and of its defense platforms (vanilla tag
    /// station_defense, within DefenseRadius of its hull) is destroyed, nearest to the raiders first. The station's parts come back with the
    /// rebuild (spec 96); the platforms repair as in vanilla. Returns the parts destroyed.
    /// </summary>
    static int GunsDown(SimRoot sim, Ship station, Vector2 raiders, float share)
    {
        var ships = sim.Ships.Where(x => x == station || x.HasTag(DefenseTag) && !x.IsJunk && HunterOrders.EdgeDistance(x, station) <= DefenseRadius);
        var guns = ships.SelectMany(x => x.Weapons.Select(w => w.Part)).Distinct().ToList();
        int count = (int)MathF.Ceiling(guns.Count * Math.Clamp(share, 0f, 1f));
        foreach (var part in guns.OrderBy(p => p.DetWorldCenter.DistanceSquaredTo(raiders)).ThenBy(p => p.Ship.UniqueID).ThenBy(p => p.Location.X).ThenBy(p => p.Location.Y)
            .Take(count).ToList())
            part.Health = 0;
        return count;
    }

    static List<StasisShipSpawner> StasisRaiders(SimRoot sim) =>
        sim.Stasis.OfType<StasisShipSpawner>().Where(x => x.Tags?.Contains(RaiderTag) == true).OrderBy(x => x.ReservedObjectID).ToList();

    /// <summary>Spec 94: drops the raiders that are gone and ends the attack.</summary>
    static void Watch(Run run, SimRoot sim, StationAttackState a)
    {
        var t = run.T.Events;
        var station = sim.Ships[a.Station];
        // Spec 94: the players are so far away that the game froze the station. The attack waits for them, and so does its time.
        if (station is null && InStasis(sim, a.Station))
        {
            a.Since += CheckSeconds;
            return;
        }
        // A raider in stasis (out of the live range) is still in the attack.
        foreach (var id in a.Raiders.Where(id => Out(sim, id)))
            Log.Info($"station attack: raider {id} is out ({(sim.Ships[id] is null ? "gone" : "a wreck")})");
        a.Raiders.RemoveAll(id => Out(sim, id));
        var end = StationAttackModel.End(a.Raiders.Count, station is not null, station is null ? 0f : HunterOrders.HealthFraction(station),
            t.StationAttackFallHealth, run.State.GameSeconds - a.Since, t.StationAttackMaxSeconds);
        if (end != AttackEnd.None) Finish(run, sim, a, end, station);
    }

    static bool InStasis(SimRoot sim, ObjectID id) => sim.Stasis.OfType<StasisShipSpawner>().Any(x => x.ReservedObjectID == id);

    static bool Out(SimRoot sim, ObjectID id) => sim.Ships[id] is { } ship ? ship.IsJunk : !InStasis(sim, id);

    /// <summary>Spec 94, 95: the end words and the reward; the raiders that are left jump out. Then the rebuild starts (spec 96).</summary>
    static void Finish(Run run, SimRoot sim, StationAttackState a, AttackEnd end, Ship? station)
    {
        var t = run.T.Events;
        string name = station?.Metadata.XmlShipName ?? Text("TheStation");
        Log.Info($"station attack: ends {end} after {run.State.GameSeconds - a.Since:0} s, station health {(station is null ? 0f : HunterOrders.HealthFraction(station)):0.00}, helped {a.Helped}");
        switch (end)
        {
            case AttackEnd.Repelled when a.Helped && station is not null:
                var lines = new List<string>();
                EventRunner.ChangeMoney(run, run.Game.HumanPlayersIndexes.Min(), EventRunner.Credits(run, t.StationAttackReward, 0f, run.CurrentTier, 0), lines);
                if (FactionRules.GetShipFaction(station) is { } faction)
                {
                    int amount = (int)MathF.Round(t.StationAttackReputation * GameData.StandardMissionReputation(run.CurrentTier));
                    if (amount != 0) { run.Mode.AddFactionReputation(faction.ID, amount); lines.Add(EventText.Reputation(faction.ID, amount)); }
                }
                Say(run.Game, Text("RepelledHelpedFmt", name));
                foreach (var line in lines) Ux.UxKit.ChatLine(run.Game, line);
                break;
            case AttackEnd.Repelled:
                Say(run.Game, Text("RepelledFmt", name));
                break;
            default:
                double due = run.State.GameSeconds + LeaveDelaySeconds;
                foreach (var id in a.Raiders)
                    if (sim.Ships[id] is not null && !run.State.Diplomacy.Leaving.Any(l => l.Ship == id)) run.State.Diplomacy.Leaving.Add(new Leaving { Ship = id, Due = due });
                // Raiders in stasis leave unseen, so they never load later as strays.
                foreach (var x in StasisRaiders(sim).Where(x => a.Raiders.Contains(x.ReservedObjectID)).ToList()) sim.Stasis.RemoveStasisSpawner(x);
                Say(run.Game, Text(end == AttackEnd.Fell ? "FellFmt" : "HeldFmt", name));
                break;
        }
        a.Raiders.Clear();
        if (station is null || station.DestroyedParts.Count == 0)
        {
            run.State.Events.Attack = null;
            return;
        }
        a.Phase = AttackPhase.Rebuild;
        a.Since = a.DebrisSeen = run.State.GameSeconds;
        Ux.UxKit.ChatLine(run.Game, Text("RebuildStartFmt", name));
    }

    /// <summary>
    /// Spec 96, 97: one rebuild step. The station pulls in the nearest unclaimed debris piece; once the piece is in reach it takes in its
    /// nearest parts and rebuilds destroyed parts for them. With no debris in reach for StationRebuildWaitSeconds the station rebuilds from
    /// its own stock (vanilla).
    /// </summary>
    static void Rebuild(Run run, SimRoot sim, StationAttackState a)
    {
        var t = run.T.Events;
        double now = run.State.GameSeconds;
        if (sim.Ships[a.Station] is not { } station)
        {
            // In stasis the rebuild waits for the players, and so does its wait for debris. A station that is gone ends it.
            if (InStasis(sim, a.Station)) a.DebrisSeen = now;
            else run.State.Events.Attack = null;
            return;
        }
        string name = station.Metadata.XmlShipName;
        int destroyed = station.DestroyedParts.Count;
        if (destroyed == 0)
        {
            Log.Info($"station attack: {station.Metadata.PlainShipName} is rebuilt: {a.Rebuilt} parts from {a.DebrisTaken} debris parts");
            Ux.UxKit.ChatLine(run.Game, Text("RebuildDoneFmt", name));
            run.State.Events.Attack = null;
            return;
        }
        if (NearestDebris(run, sim, station, t.StationRebuildRadius) is not { } piece)
        {
            if (now - a.DebrisSeen < t.StationRebuildWaitSeconds) return;
            Log.Info($"station attack: no debris near {station.Metadata.PlainShipName}; {destroyed} parts left to its own stock");
            Ux.UxKit.ChatLine(run.Game, Text("RebuildStockFmt", name));
            run.State.Events.Attack = null;
            return;
        }
        a.DebrisSeen = now;
        float gap = HunterOrders.EdgeDistance(piece, station);
        if (gap > t.StationRebuildReach)
        {
            piece.Physics.Body.LinearVelocity = Vector2.FromAngle(piece.DetWorldCenter.DirectionTo(station.DetWorldCenter)) * t.StationRebuildPullSpeed;
            return;
        }
        piece.Physics.Body.LinearVelocity = Vector2.Zero;
        int taken = TakeIn(sim, piece, station, t.StationRebuildPartsPerStep);
        int rebuilt = RebuildParts(station, StationAttackModel.RebuildCount(taken, t.StationRebuildPerDebrisPart, destroyed));
        a.DebrisTaken += taken;
        a.Rebuilt += rebuilt;
        // No part can go back (none touches the hull, or debris blocks it): vanilla rebuilds the rest, as it does without the event.
        if (taken > 0 && rebuilt == 0)
        {
            Log.Info($"station attack: no destroyed part of {station.Metadata.PlainShipName} can be rebuilt now; {destroyed} parts left to its own stock");
            run.State.Events.Attack = null;
        }
    }

    /// <summary>The unclaimed debris piece (spec 1241 without the size limit) nearest to the station's hull, within the radius.</summary>
    internal static Ship? NearestDebris(Run run, SimRoot sim, Ship station, float radius)
    {
        var reach = DebrisArea.TractorReach(sim, run.T.Debris.TractorMargin);
        return sim.Ships.Where(x => DebrisArea.Unclaimed(run, x, reach) && HunterOrders.EdgeDistance(x, station) <= radius)
            .OrderBy(x => HunterOrders.EdgeDistance(x, station)).ThenBy(x => x.UniqueID).FirstOrDefault();
    }

    /// <summary>Removes up to <paramref name="count"/> parts of the piece nearest to the station; an empty piece leaves the sim. Returns the parts taken.</summary>
    static int TakeIn(SimRoot sim, Ship piece, Ship station, int count)
    {
        var parts = piece.Parts.OrderBy(p => p.DetWorldCenter.DistanceSquaredTo(station.DetWorldCenter)).ThenBy(p => p.Location.X).ThenBy(p => p.Location.Y)
            .Take(count).ToList();
        using (var info = ShipUpdateInfo.Alloc(piece))
        {
            foreach (var part in parts) info.PartsToRemove.Add(new PartInfo(part));
            info.ApplyPhysical();
        }
        // No lost-ship handling: it reads the local player and writes a local file, so it is not the same on every machine.
        if (piece.Parts.Count == 0 && sim.Ships.Contains(piece)) sim.Ships.Remove(piece, dispose: true, handleLostShip: false);
        return parts.Count;
    }

    /// <summary>
    /// Rebuilds up to <paramref name="count"/> destroyed parts, in cell order, the way the vanilla self-repair does (a part must touch the
    /// hull and nothing may block it), but at no cost. Returns the parts rebuilt.
    /// </summary>
    static int RebuildParts(Ship station, int count)
    {
        int done = 0;
        foreach (var part in station.DestroyedParts.OrderBy(p => p.Location.Y).ThenBy(p => p.Location.X).ToList())
        {
            if (done >= count) break;
            using var info = ShipUpdateInfo.Alloc(station);
            if (!info.TryRebuildPart(part, rebuildDoors: true, hireCrew: false, out _)) continue;
            info.ApplyPhysical(destroyBlocking: false, builtByPlayer: false, updateDestroyed: true);
            done++;
        }
        return done;
    }

    /// <summary>Spec 93: the target of a raider: the nearest player ship in its way, else the station.</summary>
    internal static Ship Target(Run run, SimRoot sim, Ship raider, Ship station)
    {
        float range = run.T.Events.StationAttackInTheWayRange;
        System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);
        return sim.Ships.HumanPlayerSignificantShips
            .Where(p => p.CanBeAttackedBy(raider) && StationAttackModel.InTheWay(N(raider.DetWorldCenter), raider.WorldBoundingRadius, N(station.DetWorldCenter),
                N(p.DetWorldCenter), p.WorldBoundingRadius, range))
            .OrderBy(p => HunterOrders.EdgeDistance(raider, p)).ThenBy(p => p.UniqueID).FirstOrDefault() ?? station;
    }

    /// <summary>
    /// Spec 93, a hunter override (HunterOrders.Overrides): a raider of a running attack goes for the station, or for a player ship in its
    /// way. Other ships, and raiders once the attack ends, keep the hunter AI. The attack lives in the run state. AI tick: sim state only.
    /// </summary>
    internal static bool Override(Ship ship)
    {
        if (Run.Current is not { } run || ship.Sim is not { } sim || run.Game.Sim != sim) return false;
        if (run.State.Events.Attack is not { Phase: AttackPhase.Attack } a || !a.Raiders.Contains(ship.UniqueID)) return false;
        if (sim.Ships[a.Station] is not { } station) return false;
        HunterOrders.Attack(ship, Target(run, sim, ship, station));
        return true;
    }

    /// <summary>
    /// Spec 95: the players help when a human player's shot damages a raider. The game names the shot's player while the damage applies
    /// (Sim.CurrentActionSourcePlayer, which vanilla reads for its war check in the same call). Sim state only.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.OnPartDamaged))]
    static class HelpPatch
    {
        static void Postfix(CareerSimModeManager __instance, Ship ship, int damage)
        {
            if (damage <= 0 || Run.Current?.State.Events.Attack is not { Phase: AttackPhase.Attack, Helped: false } a || !a.Raiders.Contains(ship.UniqueID)) return;
            if (__instance.Sim.CurrentActionSourcePlayer is not { } source || !__instance.GameMode.IsHumanPlayerIndex(source.PlayerId)) return;
            a.Helped = true;
            Log.Info($"station attack: player {source.PlayerId} hits raider {ship.Metadata.PlainShipName}: the players help");
        }
    }

    /// <summary>Spec 96: the vanilla self-repair of the attacked station rebuilds no destroyed part; the attack and the rebuild do. Damaged parts it still repairs.</summary>
    [HarmonyPatch(typeof(AISelfRepairModule), "GetAllRepairableObjects")]
    static class HoldRebuild
    {
        static void Postfix(Ship ship, List<object> objs)
        {
            if (Run.Current?.State.Events.Attack is { } a && a.Station == ship.UniqueID) objs.RemoveAll(o => o is DestroyedPart or DestroyedDoor);
        }
    }

    /// <summary>Spec 92: the blip tag of a ship in the attack, or null.</summary>
    internal static string? Tag(StationAttackState a, ObjectID id)
    {
        string? tag = id == a.Station ? Text(a.Phase == AttackPhase.Attack ? "TagUnderAttack" : "TagRebuilding")
            : a.Raiders.Contains(id) ? Text("TagRaider") : null;
        if (tag is null) return null;
        return a.Phase == AttackPhase.Attack ? $"<bad><b>{tag}</b></bad>" : $"<good><b>{tag}</b></good>";
    }

    /// <summary>Spec 92: a tag on the station's blip (UNDER ATTACK, then REBUILDING) and on each raider's blip (RAIDER). UI only.</summary>
    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class Label
    {
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (Run.Current?.State.Events.Attack is not { } a || Tag(a, __instance.Ship.UniqueID) is not { } xml) return;
            __result = __result is null ? xml : xml + "\n" + __result;
        }
    }
}

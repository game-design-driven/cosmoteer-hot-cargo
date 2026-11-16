using Cosmoteer.Crew;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Debris;
using HotCargo.Dev;

namespace HotCargo.Events;

/// <summary>Test commands of the station attack (spec 98).</summary>
static class StationAttackAutotest
{
    static Run R() => Autotest.RequireRun();
    static SimRoot Sim() => R().Game.Sim;
    static StationAttackState A() => R().State.Events.Attack ?? throw new InvalidOperationException("no station attack runs");
    /// <summary>The station of the running attack, or of the last one (sa_mark notes it), so the checks work after the end.</summary>
    static Ship Station()
    {
        if (R().State.Events.Attack is { } a) _station = a.Station;
        return Sim().Ships[_station ?? throw new InvalidOperationException("no station attack ran")] ?? throw new InvalidOperationException("the attacked station is gone");
    }
    static long Money() => R().Game.GetPlayerState(R().Game.LocalPlayerIndex, deterministic: true).Money;
    static List<Ship> Raiders() => A().Raiders.Select(id => Sim().Ships[id]).OfType<Ship>().ToList();
    static Ship? AttackTarget(Ship s) => (s.Commands.CurConfirmedCommand as AttackCommand)?.Target;
    static long PlayerHealth() => Sim().Ships.HumanPlayerSignificantShips.Sum(p => p.Parts.Sum(x => (long)x.Health));

    /// <summary>Parts of the unclaimed debris within the rebuild radius of the station (spec 96).</summary>
    static List<Ship> DebrisPieces(Ship station)
    {
        var run = R();
        var reach = DebrisArea.TractorReach(Sim(), run.T.Debris.TractorMargin);
        return Sim().Ships.Where(x => DebrisArea.Unclaimed(run, x, reach) && Hunters.HunterOrders.EdgeDistance(x, station) <= run.T.Events.StationRebuildRadius).ToList();
    }

    static int DebrisParts(Ship station) => DebrisPieces(station).Sum(x => x.Parts.Count);

    /// <summary>Parts of the ships that sa_mark saw near the station, as they are now (a piece that is gone counts 0). Pieces that drift in later do not count.</summary>
    static int MarkedPieceParts() => _pieces.Sum(id => Sim().Ships[id]?.Parts.Count ?? 0);

    static Cosmoteer.Game.ObjectID? _station;
    static long _health, _money;
    static int _debris, _destroyed;
    static List<Cosmoteer.Game.ObjectID> _pieces = [];
    static int _pieceParts;

    internal static void Register()
    {
        // sa_start: starts an attack on the nearest live trade station now, with no roll and no pacer check.
        Autotest.Commands["sa_start"] = _ =>
        {
            var station = StationAttack.NearestStation(R(), Sim()) ?? throw new InvalidOperationException("no live trade station");
            Autotest.Check(StationAttack.Start(R(), Sim(), station), "the station attack starts");
        };
        // sa_roll: the attack roll is due now, as after an arrival: it runs when a player ship is near a station (spec 90).
        Autotest.Commands["sa_roll"] = _ => R().State.Events.AttackDueSeconds = R().State.GameSeconds;
        // sa_place far|way: far = the player ships on the far side of the station from the raiders, out of their way;
        // way = the first player ship beside the first raider, 60 off its hull (in the way by range), clear of the station.
        Autotest.Commands["sa_place"] = arg =>
        {
            var station = Station();
            var raider = Raiders().FirstOrDefault() ?? throw new InvalidOperationException("no raider left");
            var ships = Sim().Ships.HumanPlayerSignificantShips.ToList();
            Vector2 toStation = Vector2.FromAngle(raider.DetWorldCenter.DirectionTo(station.DetWorldCenter));
            Vector2 side = new(-toStation.Y, toStation.X);
            Vector2 at = arg.Trim() switch
            {
                "far" => station.DetWorldCenter + toStation * (station.WorldBoundingRadius + 1500f),
                "way" => raider.DetWorldCenter + side * (raider.WorldBoundingRadius + ships[0].WorldBoundingRadius + 60f),
                _ => throw new InvalidOperationException("sa_place needs far or way"),
            };
            float offset = 0f;
            foreach (var s in ships) { Autotest.Teleport(s, at + side * offset); offset += 2 * s.WorldBoundingRadius + 100f; }
        };
        // sa_mark: notes the player health, money, debris parts and destroyed station parts, for the _lost, _gain, _drop and _hits fields.
        Autotest.Commands["sa_mark"] = _ =>
        {
            _health = PlayerHealth();
            _money = Money();
            // Every other ship near the station, also a wreck that still has crew: it can become unclaimed debris later.
            var station = Station();
            var pieces = Sim().Ships.Where(x => x != station && !x.IsHumanPlayerShip && Hunters.HunterOrders.EdgeDistance(x, station) <= R().T.Events.StationRebuildRadius).ToList();
            _pieces = pieces.Select(x => x.UniqueID).ToList();
            _debris = DebrisParts(station);
            _pieceParts = pieces.Sum(x => x.Parts.Count);
            _destroyed = Station().DestroyedParts.Count;
            Log.Info($"AUTOTEST station attack mark: debris {_debris}, destroyed {_destroyed}, money {_money}");
        };
        // sa_break <n>: destroys the n station parts farthest from its center (its outer hull).
        Autotest.Commands["sa_break"] = arg =>
        {
            var station = Station();
            foreach (var p in station.Parts.OrderByDescending(p => p.DetWorldCenter.DistanceSquaredTo(station.DetWorldCenter)).Take(int.Parse(arg)).ToList()) p.Health = 0;
        };
        // sa_wreck: every raider becomes a wreck where it is: its crew die and it turns to junk. Its storage parts go, as if the players
        // salvaged the cargo: a wreck that holds resources is loot, and the station leaves it alone (spec 96).
        Autotest.Commands["sa_wreck"] = _ =>
        {
            foreach (var r in Raiders())
            {
                foreach (var c in r.Crew.Assigned.ToList()) c.Kill(DeathCause.LostInSpace);
                r.Metadata.PlayerIndex = -3;
                using var info = ShipUpdateInfo.Alloc(r);
                foreach (var part in r.Parts.Where(p => p.Components.Any(c => c is Cosmoteer.Ships.Parts.Resources.BaseResourceStorage)).ToList())
                    info.PartsToRemove.Add(new Cosmoteer.Ships.Parts.PartInfo(part));
                info.ApplyPhysical();
            }
            Autotest.Wait(1);
        };
        // sa_clear_guards: removes every other AI ship within 3,000 of the station (its guards and patrols), so only the raiders fight it.
        Autotest.Commands["sa_clear_guards"] = _ =>
        {
            var station = Station();
            var raiders = A().Raiders.ToHashSet();
            var others = Sim().Ships.Where(x => x != station && !x.IsHumanPlayerShip && !x.IsJunk && !x.Rules.IsAsteroid && !raiders.Contains(x.UniqueID)
                && x.SpecialComponents.Count == 0 && Hunters.HunterOrders.EdgeDistance(x, station) <= 3000f).ToList();
            foreach (var x in others) Sim().Ships.Remove(x, dispose: true, handleLostShip: false);
            Log.Info($"AUTOTEST removed {others.Count} AI ships near the station");
        };
        // sa_camera <size>: the camera on the station, this many world units across (screenshots).
        Autotest.Commands["sa_camera"] = arg =>
        {
            var at = Station().WorldCenter;
            Autotest.Commands["camera"](string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{at.X} {at.Y} {arg.Trim()}"));
        };
        // sa_kill_raiders: every raider part is destroyed (no wreck is left).
        Autotest.Commands["sa_kill_raiders"] = _ => { foreach (var r in Raiders()) foreach (var p in r.Parts.ToList()) p.Health = 0; Autotest.Wait(1); };
        Autotest.Fields["sa_phase"] = () => R().State.Events.Attack is { } a ? (int)a.Phase : -1;
        Autotest.Fields["sa_raiders"] = () => R().State.Events.Attack?.Raiders.Count ?? 0;
        Autotest.Fields["sa_off_station"] = () => Raiders().Count(r => AttackTarget(r) != Station());
        Autotest.Fields["sa_on_player"] = () => Raiders().Count(r => AttackTarget(r) is { IsHumanPlayerShip: true });
        Autotest.Fields["sa_destroyed"] = () => Station().DestroyedParts.Count;
        Autotest.Fields["sa_station_hits"] = () => Station().DestroyedParts.Count - _destroyed;
        Autotest.Fields["sa_money_gain"] = () => Money() - _money;
        Autotest.Fields["sa_leaving"] = () => R().State.Diplomacy.Leaving.Count(l => Sim().Ships[l.Ship]?.HasTag(StationAttack.RaiderTag) == true);
        Autotest.Fields["sa_helped"] = () => A().Helped ? 1 : 0;
        Autotest.Fields["sa_taken"] = () => A().DebrisTaken;
        Autotest.Fields["sa_rebuilt"] = () => A().Rebuilt;
        Autotest.Fields["sa_debris"] = () => DebrisParts(Station());
        // The parts that the marked debris pieces lost: other wreck pieces can drift into the rebuild radius meanwhile.
        // Destroyed station parts that came back since sa_mark.
        Autotest.Fields["sa_destroyed_drop"] = () => _destroyed - Station().DestroyedParts.Count;
        Autotest.Fields["sa_debris_drop"] = () => _pieceParts - MarkedPieceParts();
        Autotest.Fields["sa_player_lost"] = () => _health - PlayerHealth();
        Autotest.Fields["sa_station_tag"] = () => StationAttack.Tag(A(), A().Station) is null ? 0 : 1;
        Autotest.Fields["sa_pacer"] = () => Pursuit.Pacer.Count(R(), Pursuit.PacedKinds.StationAttack);
    }
}

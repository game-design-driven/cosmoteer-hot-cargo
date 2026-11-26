using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Missions;

/// <summary>
/// Test commands for mission ships that must keep their course near enemies (#372: the vanilla aggro module turned the escort
/// freighter's hull to every enemy on radar, and it crawled). A test tracks one ship by its spawn tag and puts an enemy beside it.
/// </summary>
internal static class AggroAutotest
{
    static string? _tag;
    static Vector2 _markedAt;
    static float _markedGap;

    public static void Register()
    {
        // hc_track <spawn tag>: the ship that the hc_track_* fields read, the live ship with the tag and the lowest ID.
        Autotest.Commands["hc_track"] = arg =>
        {
            _tag = arg.Trim();
            var ship = Tracked();
            Log.Info($"AUTOTEST track {_tag}: {ship.Metadata.PlainShipName} id={ship.UniqueID} ai={ship.AI?.Rules.ID}");
        };
        // Remembers the tracked ship's place and its gap to the nearest player ship, for hc_track_moved and hc_track_closed.
        Autotest.Commands["hc_track_mark"] = _ =>
        {
            var ship = Tracked();
            (_markedAt, _markedGap) = (ship.DetWorldCenter, Gap(ship));
        };
        // 1 while its order turns its hull away from its course or makes it fight: a move with forced rotation
        // (AIAggroEnemiesModule.TacticalRetreat) or an attack order.
        Autotest.Fields["hc_track_turned"] = () =>
            Tracked().Commands.CurConfirmedCommand is MoveCommand { ForceDestinationRotation: true } or AttackCommand ? 1 : 0;
        Autotest.Fields["hc_track_speed"] = () => Tracked().Physics.Body?.LinearVelocity.Length() ?? 0f;
        Autotest.Fields["hc_track_moved"] = () => Tracked().DetWorldCenter.DistanceTo(_markedAt);
        Autotest.Fields["hc_track_closed"] = () => _markedGap - Gap(Tracked());
        // hc_track_leave <distance>: the player ships go about <distance> from the tracked ship, on clear spots (MissionArea.TeleportPlayers).
        Autotest.Commands["hc_track_leave"] = arg =>
            MissionArea.TeleportPlayers(Tracked().DetWorldCenter + new Vector2(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) - 400f, 0f), clear: true);
        // hc_watcher <distance>: a hostile Small Point Defense Platform with no AI, <distance> from the tracked ship, square to the
        // line from it to the nearest player ship. It stays where it spawns, and its point defense does not reach far.
        Autotest.Commands["hc_watcher"] = arg => SpawnWatcher(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
    }

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    static Ship Tracked()
    {
        string tag = _tag ?? throw new InvalidOperationException("run hc_track <spawn tag> first");
        return SimQueries.TaggedShips(Sim(), new ID<SimObjectSpawner>(tag)).Select(t => t.Live).OfType<Ship>().MinBy(s => s.UniqueID)
            ?? throw new InvalidOperationException($"no live ship tagged {tag}");
    }

    static Ship Nearest(Ship ship) => SimQueries.NearestHumanShip(ship.Sim, ship.DetWorldCenter) ?? throw new InvalidOperationException("no player ship");

    static float Gap(Ship ship) => ship.DetWorldCenter.DistanceTo(Nearest(ship).DetWorldCenter);

    static void SpawnWatcher(float distance)
    {
        var ship = Tracked();
        var sim = Sim();
        const string hull = "Small Point Defense Platform";
        var pick = Cosmoteer.GameApp.Rules.BuiltinShips.FirstOrDefault(b => b.File.ShipName == hull) ?? throw new InvalidOperationException($"no built-in ship '{hull}'");
        Vector2 toPlayers = (Nearest(ship).DetWorldCenter - ship.DetWorldCenter).Normalize();
        Vector2 at = Autotest.ClearSpotNear(sim, ship.DetWorldCenter + new Vector2(-toPlayers.Y, toPlayers.X) * distance, 0f, 150f);
        int hostile = ((CareerSimModeManager)sim.Mode).AllocUniquePlayerIndex();
        if (!sim.Mode.AreEnemies(hostile, ship.Metadata.PlayerIndex)) throw new InvalidOperationException($"player {hostile} is no enemy of {ship.Metadata.PlainShipName}");
        var spawner = new StasisShipSpawner(sim, pick.ID, at, SpawnedShipFeatures.None, sim.Rand.UInt64())
        {
            Direction = at.DirectionTo(ship.DetWorldCenter),
            ObjectAllegiance = hostile,
        };
        spawner.Spawn(sim);
        var watcher = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{pick.ID} did not spawn");
        Log.Info($"AUTOTEST watcher {watcher.Metadata.PlainShipName} id={watcher.UniqueID} at {at}, {at.DistanceTo(ship.DetWorldCenter):0} from {ship.Metadata.PlainShipName} " +
                 $"(sight {ship.Sensors.DetWorldSightCircle.Radius:0}, radar {ship.Sensors.DetWorldRadarCircle.Radius:0})");
    }
}

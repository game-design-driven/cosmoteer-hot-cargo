using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Missions.Mechanics;

namespace HotCargo.Missions;

/// <summary>
/// Spec 1877: the one arrival rule of every mission that delivers a ship (the Tow Job, Salvage Rights, Repossession, the escorts, the
/// Mining Escort miner): the ship has arrived when its hull is within Mechanics.DeliveryRadius of the target's hull. A point target (an
/// escort's jump point) has no hull. Arrival completes the step at once, with no comms step.
/// </summary>
internal static class ShipDelivery
{
    internal static float Gap(GameRoot game) => MechanicsArea.TunablesFor(game).DeliveryRadius;

    internal static bool Arrived(GameRoot game, Circle ship, Circle target) =>
        MechanicsModel.Gap(ship.Center.DistanceTo(target.Center), ship.Radius, target.Radius) <= Gap(game);

    /// <summary>The map ring of a target: the area that the ship's hull must reach.</summary>
    internal static Circle Area(GameRoot game, Circle target) => new(target.Center, target.Radius + Gap(game));

    /// <summary>The live station whose hull covers the point (a station never moves), or null: in stasis, gone, or none there.</summary>
    internal static Ship? StationAt(SimRoot sim, Vector2 point) =>
        SpaceStation.GetStations(sim, onlyOperating: false).Select(s => s.PhysicalShip).OfType<Ship>()
            .Where(s => s.DetWorldBoundingCircle.Contains(point)).OrderBy(s => s.DetWorldCenter.DistanceTo(point)).ThenBy(s => s.UniqueID).FirstOrDefault();

    /// <summary>The ship has arrived at the live station at the point. False while that station is not live.</summary>
    internal static bool ArrivedAtStation(GameRoot game, SimRoot sim, Ship ship, Vector2 station) =>
        StationAt(sim, station) is { } st && Arrived(game, ship.DetWorldBoundingCircle, st.DetWorldBoundingCircle);
}

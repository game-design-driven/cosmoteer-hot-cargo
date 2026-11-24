using Cosmoteer;
using Cosmoteer.Factions;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Missions.Mechanics;
using PartAt = HotCargo.Core.ShipBuilder.PartAt;

namespace HotCargo.Missions;

/// <summary>
/// Spec 1891: the Blind Courier's ship, a small unarmed courier built from vanilla parts (<see cref="ShipBuilder"/>), like the Ore Tender.
/// No weapons, shields or armor, so a raid kills it fast. Rotation 0 faces the nose (up, -y).
/// </summary>
internal static class CourierShip
{
    /// <summary>
    /// A one-wide corridor down the center line, with an airlock at the nose and a fire extinguisher at its end; the control room and a cargo bay beside its front, the reactor and crew quarters beside
    /// its back; a braking thruster on each front flank, a side thruster on each back flank, and five medium thrusters at the tail.
    /// Each part touches one placed before it.
    /// </summary>
    internal static readonly PartAt[] Layout =
    [
        new("cosmoteer.corridor", 0, -2, 0),
        new("cosmoteer.corridor", 0, -1, 0),
        new("cosmoteer.corridor", 0, 0, 0),
        // The ship editor's vital parts (Core/ShipCheck): a fire extinguisher at the spine's end, its door up into the spine, and an
        // airlock at the nose, its door down into the spine.
        new("cosmoteer.fire_extinguisher", 0, 1, 2),
        new("cosmoteer.airlock", 0, -3, 0),
        new("cosmoteer.control_room_small", -2, -2, 0),
        new("cosmoteer.storage_2x2", 1, -2, 0),
        new("cosmoteer.reactor_small", -2, 0, 0),
        // Its only doors are on top: the crew leave through the cargo bay, like on the Ore Tender.
        new("cosmoteer.crew_quarters_med", 1, 0, 0),
        new("cosmoteer.thruster_small", -3, -2, 2),
        new("cosmoteer.thruster_small", 3, -2, 2),
        new("cosmoteer.thruster_small", -4, 0, 1),
        new("cosmoteer.thruster_small", 3, 0, 3),
        new("cosmoteer.thruster_med", -2, 2, 0),
        new("cosmoteer.thruster_med", -1, 2, 0),
        new("cosmoteer.thruster_med", 0, 2, 0),
        new("cosmoteer.thruster_med", 1, 2, 0),
        new("cosmoteer.thruster_med", 2, 2, 0),
    ];

    static byte[]? _design;

    static byte[] Design() => _design ??= ShipBuilder.DesignBytes(Layout, "Courier", "courier");

    /// <summary>
    /// Spec 1891: a full courier of the faction's civilians at the spot, with the module-less AI of the Ore Tender (MinerShip.AiId), so it
    /// follows only the objective's move orders. Far from the players the game puts it into stasis like any other ship.
    /// </summary>
    internal static Ship Spawn(SimRoot sim, Rand rand, FactionRules faction, Vector2 at, Direction facing)
    {
        var ship = ShipBuilder.Load(sim, rand, Design(), Strings.GetText("HotCargoMissions/CourierName"), faction.CivilianPlayerIndex, at, facing);
        ship.AI = new ShipAI(GameApp.Rules.ShipAIs[MinerShip.AiId], new AIParameters { ChanceToConsiderSurrenderingToPlayer = 0f });
        sim.Ships.Add(ship);
        Log.Info($"blind courier: {ship.Metadata.PlainShipName} id={ship.UniqueID} player {faction.CivilianPlayerIndex} at {at} parts={ship.Parts.Count} crew={ship.Crew.Assigned.Count}/{ship.Crew.TotalCrewCapacity}");
        return ship;
    }
}

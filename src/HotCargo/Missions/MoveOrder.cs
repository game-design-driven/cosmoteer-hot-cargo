using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Halfling.Geometry;

namespace HotCargo.Missions;

/// <summary>Move orders for mission ships whose AI has no strategy modules (the escort freighter, the blind courier).</summary>
internal static class MoveOrder
{
    /// <summary>A move order to the point, unless the current order already goes within <paramref name="slack"/> of it.</summary>
    public static void To(Ship ship, Vector2 to, float slack)
    {
        if (ship.Commands.CurConfirmedCommand is MoveCommand { Location: { } at } && at.DistanceTo(to) <= slack) return;
        ship.Commands.EnqueueCommand(new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), to, null, CollisionAvoidanceFlags.All, isTentative: false),
            append: false, confirmed: true);
    }
}

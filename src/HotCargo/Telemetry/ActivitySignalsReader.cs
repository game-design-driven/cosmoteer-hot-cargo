using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Simulation;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 1761: the activity signals of the players' fleet, read from state that the game already keeps. Read only.
/// Limits: the build tool is the local GUI, so in co-op each machine sees only its own player's editor; the other signals are sim
/// state and the same on every machine. A placed part or ordered repair that never gets its resources counts until it is done or removed.
/// Revisit if the files of host and client must agree on build time, or if unpaid parts inflate build or repair time.
/// </summary>
static class ActivitySignalsReader
{
    internal static ActivitySignals Read(Session s, SimRoot sim)
    {
        bool build = s.Game.Gui?.ShipGui?.BuildButton?.IsSelected == true, trade = false, travel = sim.Ships.IsDoingLocalJump, repair = false;
        var comms = (sim.Mode as CareerSimModeManager)?.OpenComms;
        float far = s.Run.T.Telemetry.TravelMinDistance;
        foreach (Ship ship in sim.Ships.HumanPlayerSignificantShips)
        {
            if (!trade && comms is not null && comms.Values.Any(callers => callers.Contains(ship))) trade = true;
            if (!travel) travel = ship.Commands.CurConfirmedCommand switch
            {
                FtlGateJumpCommand => true,
                MoveCommand m => (m.Location ?? (m.TargetObjectID is { } id ? sim.Ships[id]?.DetWorldCenter : null)) is { } to && to.DistanceTo(ship.DetWorldCenter) >= far,
                _ => false,
            };
            if (ship.OptionalConstructionManager is { } cm)
            {
                // New parts placed, and repairs ordered: queued and not blocked by something in the way, also while the crew fetch the resources.
                var open = cm.QueuedPartConstructions.Where(q => !cm.BlockedPartConstructions.Contains(q)).ToList();
                if (open.Any(q => !q.IsRepair) || cm.QueuedDoorConstructions.Any(q => !q.IsRepair) || cm.QueuedPartDeconstructions.Count > 0
                    || cm.QueuedDoorDeconstructions.Count > 0) build = true;
                if (open.Any(q => q.IsRepair) || cm.QueuedPartRepairs.Count > 0 || cm.QueuedDoorConstructions.Any(q => q.IsRepair)) repair = true;
            }
        }
        // A station trade: the station's crew often carry it, so every crew member of the system counts whose trade job has a human ship at one end.
        if (!trade) trade = sim.Crew._allSouls.Any(c => c.Job is ResourceTransferJob { TradeType: not TradeType.None } j
            && (j.Source?.Ship?.IsHumanPlayerShip == true || j.Sink.Ship?.IsHumanPlayerShip == true));
        return new ActivitySignals(s.InFight, build, trade, travel, repair);
    }
}

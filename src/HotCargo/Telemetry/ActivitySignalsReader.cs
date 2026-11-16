using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Simulation;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 1761: the activity signals of the players' fleet, read from state that the game already keeps. Read only.
/// Limit: the build tool is the local GUI, so in co-op each machine sees only its own player's editor; the other signals are sim
/// state and the same on every machine. Revisit if the files of host and client must agree on build time.
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
            if (ship.OptionalConstructionManager is { } cm && (cm.QueuedPartDeconstructions.Count > 0 || cm.QueuedDoorDeconstructions.Count > 0)) build = true;
            if (build && trade && repair) continue;
            var rebuilds = ship.OptionalConstructionManager?.QueuedPartConstructions.Where(q => q.IsRepair).Select(q => q.ConstructionJob).ToHashSet();
            foreach (var crew in ship.Crew.Assigned)
            {
                switch (crew.Job)
                {
                    case RepairPartJob: repair = true; break;
                    case ConstructPartJob job when rebuilds?.Contains(job) == true: repair = true; break;
                    case ConstructPartJob: build = true; break;
                    case ResourceTransferJob { TradeType: not TradeType.None }: trade = true; break;
                }
            }
        }
        return new ActivitySignals(s.InFight, build, trade, travel, repair);
    }
}

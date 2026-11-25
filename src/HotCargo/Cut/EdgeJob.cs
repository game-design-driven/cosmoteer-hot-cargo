using Cosmoteer;
using Cosmoteer.Crew;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Roles;
using Halfling.Geometry;
using Halfling.Timing;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>
/// Spec 2464, 2486: the crew job of one cut or weld edge. One crew member stands in the work tile next to the edge and adds work until
/// the edge has <paramref name="seconds"/> of work. Built like the game's door deconstruction and repair jobs (DeconstructDoorJob, RepairPartJob).
/// </summary>
internal sealed class EdgeJob(CutEdge edge, Func<Run, float> seconds, string nameKey) : Job
{
    public CutEdge Edge { get; } = edge;

    public override Part? Part => null;

    public override int DesiredCrew
    {
        get
        {
            if (Edge.Done || Ship is not { } ship) return 0;
            return Sim is { } sim && sim.Doodads.ShouldAvoidLocation(ship.DetWorldBoundingCircle, GameApp.Rules.Crew.AvoidableDoodadTags) ? 0 : 1;
        }
    }

    public override NonContiguousCrewSearchMode AllowNonContiguousCrewSearch => NonContiguousCrewSearchMode.EnabledWithoutAirlock;

    Vector2 WorkPoint => new(Edge.WorkX + 0.5f, Edge.WorkY + 0.5f);

    Part? WorkPart => Ship?.Parts[new IntVector2(Edge.WorkX, Edge.WorkY), PartRectType.Physical];

    /// <summary>The same crew rules as DeconstructDoorJob.IsValidFor.</summary>
    public override bool IsValidFor(CrewSoul crew)
    {
        if (Ship is not { } ship) return false;
        if (crew.HomeShip != ship && (crew.HomeShip == null || !ship.Sim.Mode.CanShipHelpWithConstruction(crew.HomeShip, ship))) return false;
        if (crew.CarriedResource.Quantity > 0) return false;
        if (crew.HomeShip is { } home)
        {
            if (home.Ftl.IsPreparingToJump) return false;
            if (ship.DetTransformPointToWorld(WorkPoint).DistanceTo(home.DetWorldBoundingCircle) > GameApp.Rules.Crew.LoseHomeShipWarningDistance) return false;
        }
        return crew.ShipCrew is not { } shipCrew
            || !ship.Sim.Doodads.ShouldAvoidLocation(shipCrew.Ship.DetWorldBoundingCircle, GameApp.Rules.Crew.AvoidableDoodadTags);
    }

    /// <summary>
    /// A walkable work tile (a corridor, a room) is a part destination: crew walk there inside the ship and arrive when they enter it.
    /// Any other tile (armor, a hull part) is a point near the hull that crew reach from outside, as vanilla construction does
    /// (ConstructPartJob): a crew member inside the ship never arrives at such a point (ShipCrew arrival needs Destination.Part).
    /// </summary>
    public override Destination GetDestination(CrewSoul crew) => WalkablePart is { } part
        ? new Destination(part, WorkPoint)
        : new Destination(Ship!, WorkPoint, null, crew.Rules.ConstructDistance);

    Part? WalkablePart => WorkPart is { } part && !part.IsDestroyed && part.Rules.CrewSpeedFactor.IsNonZero ? part : null;

    /// <summary>
    /// The crew search starts inside the ship only from a walkable work tile. The crew path grid covers only the walkable tiles and a
    /// margin (PathManager.EnlargeToFit), so a search from armor far from them reads outside the grid and crashes the game. Any other
    /// work tile is a point near the hull, as RepairPartJob.GetSearchOrigin does.
    /// </summary>
    public override bool GetSearchOrigin(out Destination? origin, out float distanceToOrigin, List<Ship> additionalSearchShips)
    {
        origin = WalkablePart is { } part ? new Destination(part, WorkPoint) : new Destination(Ship!, WorkPoint);
        distanceToOrigin = 0f;
        GetNearbyConstructionHelperShips(Ship!, additionalSearchShips);
        return true;
    }

    public override void OnCrewArrived(CrewSoul crew, Time dt, out Destination newDest)
    {
        newDest = default;
        if (Edge.Done || Run.Current is not { } run) return;
        Edge.Work += (float)(dt.Seconds * crew.Rules.WorkPerSecond);
        if (WorkPart is { } part) crew.SpaceCrew?.RenewConstructionBeams(part);
        if (Edge.Work >= seconds(run)) Edge.Done = true;
        else newDest = GetDestination(crew);
    }

    /// <summary>The priority of deconstruction work, as DeconstructDoorJob.GetBaseRolePriority.</summary>
    public override int? GetBaseRolePriority(Role? role)
    {
        if (Ship is not { } ship) return null;
        int value = ship.Rules.DefaultDeconstructPriority;
        if (ship.Rules.AssociateDeconstructJobWithPart is { } assoc && role is not null
            && role.Priorities.TryGetValue(new PriorityKey(assoc, SalvageJob.FakeDeconstructComponentID), out int p))
            value = p;
        return value > 0 ? value : null;
    }

    public override string GetJobName(CrewSoul c) => Strings.GetText(nameKey);

    public override int GetTypeIntegrityHash() => 0x4843_4354; // "HCCT"

    public override bool OverrideExclusiveAssignment() => true;
}

using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Cut;

namespace HotCargo.Weld;

/// <summary>The weld rules (<see cref="WeldModel"/>) applied to game ships: targets, places, the pull and the merge.</summary>
internal static class WeldShip
{
    /// <summary>
    /// Spec 2481: a target for the ship of <paramref name="playerIndex"/>: within range, an own ship or a derelict, only parts of the
    /// ship's part set, and named by no contract, the relic or an abandoned-ship event.
    /// </summary>
    internal static bool IsTarget(Run run, Ship ship, Ship target, int playerIndex)
    {
        if (target == ship || target.IsDisposed || target.Sim != ship.Sim || target.Parts.Count == 0 || target.Rules.IsAsteroid) return false;
        if (ship.DetWorldBoundingCircle.DistanceTo(target.DetWorldBoundingCircle) > run.T.Weld.Range) return false;
        bool own = target.IsHumanPlayerShip && target.Commands.IsCommandableBy(playerIndex);
        if (!own && !target.IsJunk) return false;
        if (target.Parts.Any(p => !ship.Rules.PartsByID.ContainsKey(p.Rules.ID))) return false;
        if (target.HasTag(Events.Abandoned.RelicTag) || target.HasLabel(Events.Abandoned.Label)) return false;
        return !run.Mode.Missions.Any(m => m.IsShipAssociated(target));
    }

    /// <summary>Spec 2482: the targets of the ship, nearest first, then by ID.</summary>
    internal static List<Ship> Targets(Run run, Ship ship, int playerIndex) =>
        ship.Sim.Ships.Where(t => IsTarget(run, ship, t, playerIndex))
            .OrderBy(t => t.DetWorldBoundingCircle.DistanceTo(ship.DetWorldBoundingCircle)).ThenBy(t => t.UniqueID).ToList();

    /// <summary>Spec 2482: the place nearest to where the target is now.</summary>
    internal static Placement Snap(Ship ship, Ship target)
    {
        var origin = ship.DetTransformPointFromWorld(target.DetLocation);
        return WeldModel.Snap((float)target.DetRotation - (float)ship.DetRotation, origin.X, origin.Y);
    }

    /// <summary>The ship's parts and the target's parts at a place, in one grid of the ship. Target parts come after the ship's.</summary>
    internal sealed record Layout(CutGrid Both, HashSet<Cell> ShipTiles, List<Cell> TargetTiles, int ShipParts);

    internal static Layout Place(Ship ship, Ship target, Placement p)
    {
        var parts = new List<(PartRules Rules, int Rot, bool Flip)>();
        var cells = new Dictionary<Cell, int>();
        var shipTiles = new HashSet<Cell>();
        foreach (var part in ship.Parts.OrderBy(q => q.Location.Y).ThenBy(q => q.Location.X))
        {
            foreach (IntVector2 c in part.PhysicalRect)
            {
                cells[new Cell(c.X, c.Y)] = parts.Count;
                shipTiles.Add(new Cell(c.X, c.Y));
            }
            parts.Add((part.Rules, part.Rotation, part.FlipX));
        }
        int shipParts = parts.Count;
        foreach (var part in ship.BlueprintParts)
            foreach (IntVector2 c in part.PhysicalRect) shipTiles.Add(new Cell(c.X, c.Y));
        var targetTiles = new List<Cell>();
        foreach (var part in target.Parts.OrderBy(q => q.Location.Y).ThenBy(q => q.Location.X))
        {
            foreach (IntVector2 c in part.PhysicalRect)
            {
                var placed = WeldModel.Place(new Cell(c.X, c.Y), p);
                cells[placed] = parts.Count;
                targetTiles.Add(placed);
            }
            parts.Add((part.Rules, WeldModel.PartRotation(part.Rotation, p), part.FlipX));
        }
        var grid = new CutGrid(cells, e =>
        {
            var (a, b) = (parts[cells[e.A]], parts[cells[e.B]]);
            return CutShip.InContact(a.Rules, a.Rot, a.Flip, b.Rules, b.Rot, b.Flip, e);
        });
        return new(grid, shipTiles, targetTiles, shipParts);
    }

    /// <summary>Spec 2483: the check of a place.</summary>
    internal static WeldCheck Check(Ship ship, Ship target, Placement p)
    {
        var layout = Place(ship, target, p);
        return WeldModel.Check(layout.ShipTiles, layout.TargetTiles, layout.Both, i => i >= layout.ShipParts);
    }

    /// <summary>Spec 2485: the world pose of the target at its place.</summary>
    internal static (Vector2 Location, float Rotation) Pose(Ship ship, Placement p) =>
        (ship.DetTransformPointToWorld(new Vector2(p.X, p.Y)), (float)ship.DetRotation + p.Turns * MathF.PI / 2f);

    /// <summary>
    /// Spec 2485: one step of the pull. Sets the target's motion to the ship's motion plus a move and a turn toward its place. Returns
    /// true when it arrived; then the target holds there exactly.
    /// </summary>
    internal static bool Pull(Ship ship, Ship target, Placement p, WeldTunables t, float dt)
    {
        var (loc, rot) = Pose(ship, p);
        var d = loc - target.DetLocation;
        float dAngle = rot - (float)target.DetRotation;
        if (WeldModel.Arrived(d.X, d.Y, dAngle))
        {
            Hold(ship, target, p);
            return true;
        }
        var body = target.Physics.Body;
        var (vx, vy) = WeldModel.PullVelocity(d.X, d.Y, t.PullSpeed, dt);
        body.LinearVelocity = ship.Physics.Body.GetLinearVelocityFromWorldPoint(body.WorldCenter) + new Vector2(vx, vy);
        body.AngularVelocity = ship.Physics.Body.AngularVelocity + WeldModel.TurnRate(dAngle, t.TurnSpeed * MathF.PI / 180f, dt);
        return false;
    }

    /// <summary>Spec 2485: the target sits exactly at its place and moves with the ship.</summary>
    internal static void Hold(Ship ship, Ship target, Placement p)
    {
        var (loc, rot) = Pose(ship, p);
        var wrapper = target.Physics.PhysicsWrapper;
        wrapper.Location = loc;
        wrapper.Rotation = new Direction(rot);
        var body = target.Physics.Body;
        body.LinearVelocity = ship.Physics.Body.GetLinearVelocityFromWorldPoint(body.WorldCenter);
        body.AngularVelocity = ship.Physics.Body.AngularVelocity;
        ship.Sim.Ships.RefreshShipPosition(target);
    }

    /// <summary>
    /// Spec 2487: the target becomes part of the ship. Each target part is built at once at its place with its health; stored
    /// resources move by type (the rest drops next to the ship); the crew move over; the target leaves the game without the
    /// lost-ship handling (spec 2488).
    /// </summary>
    internal static void Merge(Ship ship, Ship target, Placement p)
    {
        var sim = ship.Sim;
        var parts = target.Parts.OrderBy(q => q.Location.Y).ThenBy(q => q.Location.X).ToList();
        var placed = new List<(Part From, IntVector2 Loc, int Rot)>();
        using (var sui = ShipUpdateInfo.Alloc(ship))
        {
            foreach (var part in parts)
            {
                var r = part.Rect;
                var (x, y, _, _) = WeldModel.Place((r.X, r.Y, r.Width, r.Height), p);
                var loc = new IntVector2(x, y);
                int rot = WeldModel.PartRotation(part.Rotation, p);
                var rules = ship.Rules.PartsByID[part.Rules.ID];
                if (!sui.TryAddBlueprintPart(rules, loc, rot, part.FlipX, mirrorPhysical: true, allowOverwrite: false, addDoors: true, hireCrew: false, out var reasons, force: true))
                    throw new InvalidOperationException($"weld: {ship.Metadata.PlainShipName} cannot take {rules.ID} at {loc} rot {rot}: {reasons}");
                placed.Add((part, loc, rot));
            }
            sui.Apply();
        }
        // Storage of the part itself (a reactor's battery, a vault, a tank) moves into the same component of the rebuilt part; the game
        // builds a part's components in rules order, so the same index is the same component.
        var moved = new Dictionary<ID<ResourceRules>, int>();
        foreach (var (from, loc, rot) in placed)
        {
            var built = ship.Parts[loc, PartRectType.Normal]
                ?? throw new InvalidOperationException($"weld: {from.Rules.ID} was not built at {loc}");
            if (built.Rules.ID != from.Rules.ID || built.Rotation != rot) throw new InvalidOperationException($"weld: {built.Rules.ID} rot {built.Rotation} at {loc}, expected {from.Rules.ID} rot {rot}");
            built.Health = from.Health;
            for (int i = 0; i < from.Components.Count; i++)
            {
                if (from.Components[i] is not ISimpleResourceStorage a || built.Components[i] is not ISimpleResourceStorage b || a.ResourceType != b.ResourceType) continue;
                int n = Math.Min(a.Resources, b.MaxResources);
                b.Resources = n;
                moved[a.ResourceType] = moved.GetValueOrDefault(a.ResourceType) + n;
            }
        }

        // The rest (cargo in flexible storage) goes into the ship's storage by type; what does not fit drops next to the ship.
        foreach (var res in GameApp.Rules.Resources.OrderBy(r => r.ID.ToString(), StringComparer.Ordinal))
        {
            int n = target.Resources.GetResourceTotal(res.ID, includeStored: true, includeCarried: false) - moved.GetValueOrDefault(res.ID);
            if (n <= 0) continue;
            int left = n - ship.Resources.AddResources(res.ID, n, excludeConstructionSinks: true);
            if (left > 0) SimQueries.DropLoose(sim, ship.DetWorldCenter, ship.WorldBoundingRadius, res.ID, left);
            Log.Info($"weld: {n} {res.ID} from {target.Metadata.PlainShipName} into the ship's storage, {left} dropped");
        }

        foreach (var soul in target.Crew.Assigned.OrderBy(c => c.UniqueID).ToList())
        {
            target.Crew.UnassignCrew(soul, killed: false);
            ship.Crew.AssignCrew(soul);
        }
        foreach (var crew in target.Crew.OnShip.OrderBy(c => c.Soul.UniqueID).ToList())
        {
            var world = target.DetTransformPointToWorld(crew.Location);
            target.Crew.RemoveCrew(crew, unregisterWithSim: false);
            // Add first: a crew member's location update needs its ship (ShipCrew.SetLocation).
            ship.Crew.AddCrew(crew, registerWithSim: false);
            crew.SetLocation(ship.DetTransformPointFromWorld(world), lerpFromPrevious: false, queueGeometryRefresh: true, clearPath: true);
        }
        sim.Ships.Remove(target, dispose: true, handleLostShip: false);
    }
}

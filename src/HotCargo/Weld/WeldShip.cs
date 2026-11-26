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
    /// Spec 2481, 2482: whether <paramref name="target"/> is a target for the ship of <paramref name="playerIndex"/>, or why not: within
    /// range, an own ship or a derelict, only parts of the ship's part set, and named by no contract, the relic or an abandoned-ship
    /// event. Null for objects that are no candidates at all (the ship itself, a ship in another system, a ship without parts).
    /// </summary>
    internal static WeldTargetVerdict? TargetVerdict(Run run, Ship ship, Ship target, int playerIndex)
    {
        if (target == ship || target.IsDisposed || target.Sim != ship.Sim || target.Parts.Count == 0) return null;
        if (target.Rules.IsAsteroid || target.Parts.Any(p => !ship.Rules.PartsByID.ContainsKey(p.Rules.ID))) return WeldTargetVerdict.OtherParts;
        bool own = target.IsHumanPlayerShip && target.Commands.IsCommandableBy(playerIndex);
        if (!own && !target.IsJunk) return WeldTargetVerdict.NotYours;
        if (target.HasTag(Events.Abandoned.RelicTag) || target.HasLabel(Events.Abandoned.Label) || run.Mode.Missions.Any(m => m.IsShipAssociated(target)))
            return WeldTargetVerdict.Mission;
        if (ship.DetWorldBoundingCircle.DistanceTo(target.DetWorldBoundingCircle) > run.T.Weld.Range) return WeldTargetVerdict.TooFar;
        return WeldTargetVerdict.Ok;
    }

    internal static bool IsTarget(Run run, Ship ship, Ship target, int playerIndex) => TargetVerdict(run, ship, target, playerIndex) == WeldTargetVerdict.Ok;

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

    /// <summary>A target part and its part in the ship at a place, with the rotation and flip that the game gives it there.</summary>
    internal readonly record struct PlacedPart(Part From, PartInfo Info);

    /// <summary>The target's parts at a place in the ship's grid, in a stable order (top to bottom, then left to right).</summary>
    internal static List<PlacedPart> PlaceParts(Ship ship, Ship target, Placement p) =>
        target.Parts.OrderBy(q => q.Location.Y).ThenBy(q => q.Location.X).Select(part =>
        {
            var r = part.Rect;
            var (x, y, _, _) = WeldModel.Place((r.X, r.Y, r.Width, r.Height), p);
            var rules = ship.Rules.PartsByID[part.Rules.ID];
            return new PlacedPart(part, new PartInfo(rules, new IntVector2(x, y), WeldModel.PartRotation(part.Rotation, p, rules.IsRotateable), rules.IsFlippable && part.FlipX));
        }).ToList();

    /// <summary>
    /// A part that cannot turn keeps its footprint, so a turn changes its shape when it is not square. No vanilla or Hot Cargo part is
    /// like that (all are square), but a part of another mod can be.
    /// </summary>
    static bool KeepsShape(PlacedPart placed, Placement p)
    {
        var r = placed.From.Rect;
        var (_, _, w, h) = WeldModel.Place((r.X, r.Y, r.Width, r.Height), p);
        return placed.Info.Rect.Size == new IntVector2(w, h);
    }

    /// <summary>
    /// The ship's parts and the target's parts at a place, in one grid of the ship (physical tiles). Target parts come after the ship's.
    /// <see cref="ShipCover"/> and <see cref="TargetCover"/>: the whole part rects (<c>PartRectType.Normal</c>), which the design keeps
    /// one part per tile (CommonBasePartsManager.AddPart).
    /// </summary>
    internal sealed record Layout(CutGrid Both, HashSet<Cell> ShipCover, List<Cell> TargetCover, List<Cell> TargetTiles, int ShipParts, bool KeepsShape);

    internal static Layout Place(Ship ship, Ship target, Placement p)
    {
        var parts = new List<(PartRules Rules, int Rot, bool Flip)>();
        var cells = new Dictionary<Cell, int>();
        var shipCover = new HashSet<Cell>();
        foreach (var part in ship.Parts.OrderBy(q => q.Location.Y).ThenBy(q => q.Location.X))
        {
            foreach (IntVector2 c in part.PhysicalRect) cells[new Cell(c.X, c.Y)] = parts.Count;
            foreach (IntVector2 c in part.Rect) shipCover.Add(new Cell(c.X, c.Y));
            parts.Add((part.Rules, part.Rotation, part.FlipX));
        }
        int shipParts = parts.Count;
        foreach (var part in ship.BlueprintParts)
            foreach (IntVector2 c in part.Rect) shipCover.Add(new Cell(c.X, c.Y));
        var targetCover = new List<Cell>();
        var targetTiles = new List<Cell>();
        var placed = PlaceParts(ship, target, p);
        foreach (var (_, info) in placed)
        {
            foreach (IntVector2 c in info.PhysicalRect)
            {
                cells[new Cell(c.X, c.Y)] = parts.Count;
                targetTiles.Add(new Cell(c.X, c.Y));
            }
            foreach (IntVector2 c in info.Rect) targetCover.Add(new Cell(c.X, c.Y));
            parts.Add((info.Rules, info.Rotation, info.FlipX));
        }
        var grid = new CutGrid(cells, e =>
        {
            var (a, b) = (parts[cells[e.A]], parts[cells[e.B]]);
            return CutShip.InContact(a.Rules, a.Rot, a.Flip, b.Rules, b.Rot, b.Flip, e);
        });
        return new(grid, shipCover, targetCover, targetTiles, shipParts, placed.All(x => KeepsShape(x, p)));
    }

    /// <summary>Spec 2483: the check of a place.</summary>
    internal static WeldCheck Check(Ship ship, Ship target, Placement p)
    {
        var layout = Place(ship, target, p);
        if (!layout.KeepsShape) return new(WeldVerdict.CannotTurn, []);
        return WeldModel.Check(layout.ShipCover, layout.TargetCover, layout.TargetTiles, layout.Both, i => i >= layout.ShipParts);
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

    /// <summary>
    /// Spec 2485: while a weld runs, the target and the ship do not collide. So the piece can turn through the hull on its way to its
    /// place, and a held piece does not push the ship. The fixtures of both bodies share a negative collision group: such fixtures
    /// never collide (Fixture.CollisionGroup, ContactManager.ShouldCollide). The game uses no collision groups. The ship's fixtures
    /// change with its parts, so the tick sets the group again each step. Two welds whose ship IDs share the group also pass through
    /// each other; revisit if that shows in play.
    /// </summary>
    internal static void PassThrough(Ship ship, Ship? target, bool on)
    {
        short group = on ? (short)-(1 + (int)(ship.UniqueID._id % 32000)) : (short)0;
        foreach (var body in target is null ? [ship.Physics.Body] : (Halfling.Physics2D.Dynamics.Body[])[ship.Physics.Body, target.Physics.Body])
            foreach (var fixture in body.FixtureList)
                if (fixture.CollisionGroup != group) fixture.CollisionGroup = group;
    }

    /// <summary>
    /// Spec 2484: a piece that a weld lets go keeps the ship's motion at its point. So it floats where it is and does not drift on at
    /// the pull speed into the ship or away.
    /// </summary>
    internal static void Release(Ship ship, Ship target)
    {
        var body = target.Physics.Body;
        body.LinearVelocity = ship.Physics.Body.GetLinearVelocityFromWorldPoint(body.WorldCenter);
        body.AngularVelocity = ship.Physics.Body.AngularVelocity;
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
        // Checked before any change: a place that is not valid leaves both ships as they were.
        if (Check(ship, target, p).Verdict is var verdict && verdict != WeldVerdict.Ok)
            throw new InvalidOperationException($"weld: {target.Metadata.PlainShipName} cannot join {ship.Metadata.PlainShipName} at {p}: {verdict}");
        var placed = PlaceParts(ship, target, p);
        using (var sui = ShipUpdateInfo.Alloc(ship))
        {
            foreach (var (_, info) in placed)
                if (!sui.TryAddBlueprintPart(info.Rules, info.Location, info.Rotation, info.FlipX, mirrorPhysical: true, allowOverwrite: false, addDoors: true, hireCrew: false, out var reasons, force: true))
                    throw new InvalidOperationException($"weld: {ship.Metadata.PlainShipName} cannot take {info.Rules.ID} at {info.Location} rot {info.Rotation}: {reasons}");
            sui.Apply();
        }
        var built = placed.Select(x => (x.From, Built: x.Info.GetPart(ship))).ToList();
        if (built.FirstOrDefault(b => b.Built is null) is { From: { } missing })
        {
            Undo(ship, placed);
            throw new InvalidOperationException($"weld: {missing.Rules.ID} was not built as planned on {ship.Metadata.PlainShipName}; the weld was undone");
        }
        // Storage of the part itself (a reactor's battery, a vault, a tank) moves into the same component of the rebuilt part; the game
        // builds a part's components in rules order, so the same index is the same component.
        var moved = new Dictionary<ID<ResourceRules>, int>();
        foreach (var (from, part) in built)
        {
            part!.Health = from.Health;
            for (int i = 0; i < from.Components.Count; i++)
            {
                if (from.Components[i] is not ISimpleResourceStorage a || part.Components[i] is not ISimpleResourceStorage b || a.ResourceType != b.ResourceType) continue;
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

    /// <summary>Removes the parts that a failed merge added, with their design, so the ship is as it was before the merge.</summary>
    static void Undo(Ship ship, List<PlacedPart> placed)
    {
        var design = placed.SelectMany(x => x.Info.Rect.Select(c => ship.BlueprintParts[c])).OfType<Cosmoteer.Ships.Blueprints.BlueprintPart>().Distinct().ToList();
        using var sui = ShipUpdateInfo.Alloc(ship);
        sui.TryRemoveBlueprintParts(design, mirrorPhysical: true, out _, force: true);
        sui.Apply();
    }
}

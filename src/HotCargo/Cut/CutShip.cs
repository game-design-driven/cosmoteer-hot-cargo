using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;

namespace HotCargo.Cut;

/// <summary>The cut rules (<see cref="CutModel"/>) applied to a game ship: its parts, its design and the game's split.</summary>
internal static class CutShip
{
    /// <summary>A ship's parts or design parts as a cut grid, by physical tiles, with the parts in tile order for stable numbers.</summary>
    internal sealed record Layout<T>(CutGrid Grid, List<T> Parts) where T : CommonBasePart;

    internal static Layout<T> Grid<T>(IEnumerable<T> source) where T : CommonBasePart
    {
        var parts = source.OrderBy(p => p.Location.Y).ThenBy(p => p.Location.X).ToList();
        var cells = new Dictionary<Cell, int>();
        for (int i = 0; i < parts.Count; i++)
            foreach (IntVector2 c in parts[i].PhysicalRect) cells[new Cell(c.X, c.Y)] = i;
        return new(new CutGrid(cells, e => InContact(parts[cells[e.A]], parts[cells[e.B]], e)), parts);
    }

    /// <summary>
    /// Two parts touch across an edge and both allow contact on that side, the game's rule
    /// (CommonBaseContiguityManager.ProcessPartsNeedingSets: the second part's contiguity is turned by 180 degrees).
    /// </summary>
    static bool InContact(CommonBasePart a, CommonBasePart b, Edge e) => InContact(a.Rules, a.Rotation, a.FlipX, b.Rules, b.Rotation, b.FlipX, e);

    /// <summary>The contact rule for parts by rules, rotation and flip: <paramref name="ra"/> holds tile A of the edge.</summary>
    internal static bool InContact(PartRules ra, int rotA, bool flipA, PartRules rb, int rotB, bool flipB, Edge e)
    {
        var side = e.Down ? AdjacencyFlags.Bottom : AdjacencyFlags.Right;
        return (ra.GetAllowedContiguity(rotA, flipA) & side) != 0 && (rb.GetAllowedContiguity(Mathx.Wrap(rotB + 2, 4), flipB) & side) != 0;
    }

    static Part? PartAt(Ship ship, Cell c) => ship.Parts[new IntVector2(c.X, c.Y), PartRectType.Physical];

    /// <summary>Spec 2467: the edge still needs work: two different parts in contact across it. Reads only the two tiles.</summary>
    internal static bool NeedsWork(Ship ship, Edge e) =>
        PartAt(ship, e.A) is { } a && PartAt(ship, e.B) is { } b && a != b && InContact(a, b, e);

    internal static int Area(CommonBasePart p) => p.PhysicalRect.Width * p.PhysicalRect.Height;

    /// <summary>
    /// Spec 2465: splits the ship with the cut edges as gaps, through the game's own split (StructureContiguityManager.SplitIntoNewShips).
    /// The cut's pieces replace the game's contiguity sets, one set for each piece, and the piece that stays gets the origin number.
    /// Returns false when the ship stays one piece.
    /// </summary>
    internal static bool Split(Ship ship, IReadOnlySet<Edge> gaps)
    {
        var layout = Grid(ship.Parts);
        var pieces = layout.Grid.Pieces(gaps);
        if (pieces.Values.Distinct().Count() < 2) return false;
        int stay = CutModel.StayPiece(pieces, i => layout.Parts[i].Rules.ContiguityOriginPriority, i => Area(layout.Parts[i]));

        var cm = ship.StructureContiguity;
        cm._contiguitySets.Clear();
        cm._setsByPart.Clear();
        cm._partsNeedingSets.Clear();
        var sets = new Dictionary<int, ContiguousPartsSet<Part>>();
        for (int i = 0; i < layout.Parts.Count; i++)
        {
            var part = layout.Parts[i];
            int piece = pieces[i];
            if (sets.TryGetValue(piece, out var set)) ((CommonBaseContiguityManager<Part>.IContiguousPartsSetMembers)set).AddPart(part);
            else
            {
                sets[piece] = set = new ContiguousPartsSet<Part>(piece, part);
                cm._contiguitySets.Add(set);
            }
            cm._setsByPart.Add(part, set);
        }
        cm.OriginContiguityNumber = stay;
        foreach (var set in cm._contiguitySets)
            ((CommonBaseContiguityManager<Part>.IContiguousPartsSetMembers)set).SetIsContiguous(set.ContiguityNumber == stay, ship.Parts);
        cm.SplitIntoNewShips();
        return true;
    }

    /// <summary>
    /// Spec 2465: the piece drifts away from the ship at <paramref name="speed"/> relative to it, from the ship's center toward the
    /// piece's center. The momentum of the two stays the same.
    /// </summary>
    internal static void PushApart(Ship ship, Ship piece, float speed)
    {
        Body a = ship.Physics.Body, b = piece.Physics.Body;
        var dir = piece.DetWorldCenter - ship.DetWorldCenter;
        if (speed <= 0f || dir.Length < 1e-4f || a.Mass + b.Mass <= 0f) return;
        dir = dir.Normalize();
        float total = a.Mass + b.Mass;
        b.LinearVelocity += dir * (speed * a.Mass / total);
        a.LinearVelocity -= dir * (speed * b.Mass / total);
    }

    /// <summary>Spec 2466: removes the design parts on the far side of the cut, through the game's design-only update.</summary>
    internal static int TrimDesign(Ship ship, IReadOnlySet<Edge> gaps)
    {
        var design = Grid(ship.BlueprintParts);
        var keep = new HashSet<Cell>();
        foreach (var part in ship.Parts)
            foreach (IntVector2 c in part.PhysicalRect) keep.Add(new Cell(c.X, c.Y));
        var trim = CutModel.Trim(design.Grid, gaps, keep).Order().Select(i => design.Parts[i]).ToList();
        if (trim.Count == 0) return 0;
        using var sui = ShipUpdateInfo.Alloc(ship);
        if (!sui.TryRemoveBlueprintParts(trim, mirrorPhysical: false, out var reasons, force: true))
            throw new InvalidOperationException($"cut: the design of {ship.Metadata.PlainShipName} refused to lose {trim.Count} parts: {reasons}");
        sui.ApplyBlueprints(updateDestroyed: true);
        ship.UpdateBlueprintValidity();
        return trim.Count;
    }
}

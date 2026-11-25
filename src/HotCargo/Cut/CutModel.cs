namespace HotCargo.Cut;

/// <summary>A tile of a ship grid.</summary>
public readonly record struct Cell(int X, int Y);

/// <summary>A grid corner: the top left corner of the tile with the same coordinates.</summary>
public readonly record struct Corner(int X, int Y);

/// <summary>The edge between a tile and its right neighbor (<see cref="Down"/> false) or its lower neighbor (<see cref="Down"/> true).</summary>
public readonly record struct Edge(int X, int Y, bool Down)
{
    public Cell A => new(X, Y);
    public Cell B => Down ? new(X, Y + 1) : new(X + 1, Y);

    /// <summary>The edge between two side neighbors.</summary>
    public static Edge Between(Cell a, Cell b) => (b.X - a.X, b.Y - a.Y) switch
    {
        (1, 0) => new(a.X, a.Y, false),
        (-1, 0) => new(b.X, b.Y, false),
        (0, 1) => new(a.X, a.Y, true),
        (0, -1) => new(b.X, b.Y, true),
        var d => throw new ArgumentException($"the cells {a} and {b} are not side neighbors ({d})"),
    };

    /// <summary>The edge that lies between two neighboring corners.</summary>
    public static Edge Along(Corner a, Corner b) => (b.X - a.X, b.Y - a.Y) switch
    {
        (1 or -1, 0) => new(Math.Min(a.X, b.X), a.Y - 1, true),
        (0, 1 or -1) => new(a.X - 1, Math.Min(a.Y, b.Y), false),
        var d => throw new ArgumentException($"the corners {a} and {b} are not neighbors ({d})"),
    };
}

/// <summary>Spec 2462: why a line is not a clean cut.</summary>
public enum CutVerdict { Ok, NotOnShip, NotAcross, TooManyPieces, LooseEnd }

/// <summary>
/// A ship grid as the cut rules see it: the part that holds each tile, and whether two different parts are in contact across an edge
/// (both part rules allow contact on that side). Parts are numbered by the caller.
/// </summary>
public sealed class CutGrid(IReadOnlyDictionary<Cell, int> parts, Func<Edge, bool> inContact)
{
    public IReadOnlyDictionary<Cell, int> Parts { get; } = parts;

    public int? PartAt(Cell c) => Parts.TryGetValue(c, out int p) ? p : null;

    /// <summary>Spec 2461: an edge inside one part. The line cannot go through it.</summary>
    public bool Blocked(Edge e) => PartAt(e.A) is { } a && PartAt(e.B) == a;

    /// <summary>Spec 2462: two different parts in contact across the edge.</summary>
    public bool NeedsWork(Edge e) => PartAt(e.A) is { } a && PartAt(e.B) is { } b && a != b && inContact(e);

    /// <summary>
    /// The piece of each part when the cut edges are gaps. Pieces are numbered from 0 in the order of their first tile (by Y, then X),
    /// so every machine numbers them the same.
    /// </summary>
    public Dictionary<int, int> Pieces(IReadOnlySet<Edge> cut)
    {
        var root = new Dictionary<int, int>();
        int Find(int p)
        {
            while (root[p] != p) p = root[p] = root[root[p]];
            return p;
        }
        foreach (int p in Parts.Values) root[p] = p;
        foreach (var (cell, part) in Parts)
        {
            foreach (var next in (Cell[])[new(cell.X + 1, cell.Y), new(cell.X, cell.Y + 1)])
            {
                var e = Edge.Between(cell, next);
                if (cut.Contains(e) || !NeedsWork(e)) continue;
                root[Find(part)] = Find(Parts[next]);
            }
        }
        var number = new Dictionary<int, int>();
        var pieces = new Dictionary<int, int>();
        foreach (var (cell, part) in Parts.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
        {
            int r = Find(part);
            if (!number.TryGetValue(r, out int n)) number[r] = n = number.Count;
            pieces[part] = n;
        }
        return pieces;
    }
}

/// <summary>The result of the check of a line (spec 2462). <see cref="Work"/> holds the edges that need work, in line order.</summary>
public sealed record CutCheck(CutVerdict Verdict, IReadOnlyList<Edge> Work, IReadOnlyDictionary<int, int> Pieces);

/// <summary>Pure rules of the cut (spec 2460 to 2472). No game types, so the unit tests cover them.</summary>
public static class CutModel
{
    /// <summary>
    /// Spec 2461: moves the end of a line one corner at a time toward <paramref name="to"/>, the longer axis first. A step back over the
    /// last edge removes that edge. The line stops at a blocked edge and returns it; otherwise it returns null.
    /// </summary>
    public static Edge? Extend(List<Corner> line, Corner to, Func<Edge, bool> blocked)
    {
        if (line.Count == 0) throw new ArgumentException("a line starts with one corner", nameof(line));
        while (line[^1] != to)
        {
            var end = line[^1];
            int dx = to.X - end.X, dy = to.Y - end.Y;
            var next = Math.Abs(dx) >= Math.Abs(dy) ? new Corner(end.X + Math.Sign(dx), end.Y) : new Corner(end.X, end.Y + Math.Sign(dy));
            if (line.Count >= 2 && line[^2] == next)
            {
                line.RemoveAt(line.Count - 1);
                continue;
            }
            var edge = Edge.Along(end, next);
            if (blocked(edge)) return edge;
            line.Add(next);
        }
        return null;
    }

    /// <summary>The edges of a line, in line order, each once.</summary>
    public static List<Edge> Edges(IReadOnlyList<Corner> line) =>
        line.Zip(line.Skip(1), Edge.Along).Distinct().ToList();

    /// <summary>
    /// Spec 2462: a clean cut has at least one edge that needs work, makes exactly two pieces, and every edge that needs work has its
    /// two sides in different pieces. The ship must be one piece before the cut.
    /// </summary>
    public static CutCheck Check(CutGrid grid, IReadOnlyList<Edge> edges)
    {
        var work = edges.Distinct().Where(grid.NeedsWork).ToList();
        var pieces = grid.Pieces(edges.ToHashSet());
        CutVerdict verdict;
        if (work.Count == 0) verdict = CutVerdict.NotOnShip;
        else
        {
            int count = pieces.Values.Distinct().Count();
            verdict = count switch
            {
                1 => CutVerdict.NotAcross,
                > 2 => CutVerdict.TooManyPieces,
                _ => work.Any(e => pieces[grid.Parts[e.A]] == pieces[grid.Parts[e.B]]) ? CutVerdict.LooseEnd : CutVerdict.Ok,
            };
        }
        return new(verdict, work, pieces);
    }

    /// <summary>
    /// Spec 2465: the piece that stays the ship, by the game's rule (CommonBaseContiguityManager.UpdateContiguity): the strongest
    /// control room priority of its parts, then the larger area, then the lower piece number.
    /// </summary>
    public static int StayPiece(IReadOnlyDictionary<int, int> pieces, Func<int, int> priority, Func<int, int> area) =>
        pieces.GroupBy(kv => kv.Value)
            .Select(g => (Piece: g.Key, Priority: g.Max(kv => priority(kv.Key)), Area: g.Sum(kv => area(kv.Key))))
            .OrderByDescending(p => p.Priority).ThenByDescending(p => p.Area).ThenBy(p => p.Piece)
            .First().Piece;

    /// <summary>Spec 2464: the tile next to a work edge where the crew member stands, on the side of the piece that stays.</summary>
    public static Cell WorkCell(CutGrid grid, IReadOnlyDictionary<int, int> pieces, int stay, Edge e) =>
        grid.PartAt(e.A) is { } a && pieces[a] == stay ? e.A : e.B;

    /// <summary>
    /// Spec 2466: the parts of a design to remove after the split: every part whose piece (with the cut edges as gaps) holds no tile of
    /// <paramref name="keep"/>, the tiles of the ship that stays.
    /// </summary>
    public static HashSet<int> Trim(CutGrid design, IReadOnlySet<Edge> cut, IReadOnlySet<Cell> keep)
    {
        var pieces = design.Pieces(cut);
        var kept = design.Parts.Where(kv => keep.Contains(kv.Key)).Select(kv => pieces[kv.Value]).ToHashSet();
        return pieces.Where(kv => !kept.Contains(kv.Value)).Select(kv => kv.Key).ToHashSet();
    }
}

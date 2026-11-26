using HotCargo.Cut;

namespace HotCargo.Weld;

/// <summary>
/// Spec 2482: where the target goes in the ship's grid: <see cref="Turns"/> quarter turns (the game's part rotation sense:
/// (x, y) to (-y, x)), then the target's grid origin at (<see cref="X"/>, <see cref="Y"/>) of the ship's grid.
/// </summary>
public readonly record struct Placement(int Turns, int X, int Y)
{
    public Placement Turned() => this with { Turns = (Turns + 1) & 3 };
}

/// <summary>Spec 2481, 2482: whether an object is a weld target, or why not.</summary>
public enum WeldTargetVerdict { Ok, TooFar, NotYours, OtherParts, Mission }

/// <summary>Spec 2483: why a place is not valid.</summary>
public enum WeldVerdict { Ok, Covered, NoContact, CannotTurn }

/// <summary>The check of a place: the verdict and the joint edges, in the ship's grid.</summary>
public sealed record WeldCheck(WeldVerdict Verdict, IReadOnlyList<Edge> Joints);

/// <summary>Pure rules of the weld (spec 2480 to 2494). No game types, so the unit tests cover them.</summary>
public static class WeldModel
{
    /// <summary>A grid point turned by quarter turns around the origin.</summary>
    public static (int X, int Y) Turn(int x, int y, int turns) => (turns & 3) switch
    {
        0 => (x, y),
        1 => (-y, x),
        2 => (-x, -y),
        _ => (y, -x),
    };

    /// <summary>A target tile in the ship's grid.</summary>
    public static Cell Place(Cell c, Placement p)
    {
        // A tile is the unit square from its corner; turn both corners and take the smaller one.
        var (ax, ay) = Turn(c.X, c.Y, p.Turns);
        var (bx, by) = Turn(c.X + 1, c.Y + 1, p.Turns);
        return new(Math.Min(ax, bx) + p.X, Math.Min(ay, by) + p.Y);
    }

    /// <summary>A target rectangle (corner and size) in the ship's grid.</summary>
    public static (int X, int Y, int W, int H) Place((int X, int Y, int W, int H) r, Placement p)
    {
        var (ax, ay) = Turn(r.X, r.Y, p.Turns);
        var (bx, by) = Turn(r.X + r.W, r.Y + r.H, p.Turns);
        return (Math.Min(ax, bx) + p.X, Math.Min(ay, by) + p.Y, Math.Abs(bx - ax), Math.Abs(by - ay));
    }

    /// <summary>
    /// A target part's rotation in the ship. A flip stays, because the game flips a part before it turns it. A part that cannot turn
    /// (structure, armor, corridors) always has rotation 0, as the game builds it (the CommonBasePart constructor).
    /// </summary>
    public static int PartRotation(int rotation, Placement p, bool rotateable) => rotateable ? (rotation + p.Turns) & 3 : 0;

    /// <summary>An angle in radians, wrapped to more than minus pi and at most pi.</summary>
    public static float Wrap(float angle)
    {
        float a = MathF.IEEERemainder(angle, 2f * MathF.PI);
        return a <= -MathF.PI ? a + 2f * MathF.PI : a;
    }

    /// <summary>
    /// Spec 2482: the place nearest to where the target is now. <paramref name="angle"/>: the target's rotation minus the ship's, in
    /// radians. (<paramref name="x"/>, <paramref name="y"/>): the target's grid origin in the ship's grid.
    /// </summary>
    public static Placement Snap(float angle, float x, float y)
    {
        int turns = (int)MathF.Round(Wrap(angle) / (MathF.PI / 2f));
        return new(((turns % 4) + 4) % 4, (int)MathF.Round(x), (int)MathF.Round(y));
    }

    /// <summary>
    /// Spec 2483: a place is valid when no target tile covers a ship tile (<paramref name="shipTiles"/>: its parts and its design) and
    /// at least one target part touches a ship part with contact (<paramref name="both"/>: the ship's parts and the placed target's
    /// parts in one grid; <paramref name="isTarget"/> tells the target's parts).
    /// </summary>
    public static WeldCheck Check(IReadOnlySet<Cell> shipTiles, IEnumerable<Cell> targetTiles, CutGrid both, Func<int, bool> isTarget)
    {
        var tiles = targetTiles.ToList();
        if (tiles.Any(shipTiles.Contains)) return new(WeldVerdict.Covered, []);
        var joints = new List<Edge>();
        foreach (var c in tiles.OrderBy(c => c.Y).ThenBy(c => c.X))
            foreach (var n in (Cell[])[new(c.X + 1, c.Y), new(c.X - 1, c.Y), new(c.X, c.Y + 1), new(c.X, c.Y - 1)])
            {
                var e = Edge.Between(c, n);
                if (both.PartAt(n) is { } other && !isTarget(other) && both.NeedsWork(e) && !joints.Contains(e)) joints.Add(e);
            }
        return new(joints.Count > 0 ? WeldVerdict.Ok : WeldVerdict.NoContact, joints);
    }

    /// <summary>
    /// Spec 2485: the velocity, relative to the ship, that moves the target toward its place this step: at most
    /// <paramref name="maxSpeed"/>, and no farther than the place within <paramref name="dt"/>.
    /// </summary>
    public static (float X, float Y) PullVelocity(float dx, float dy, float maxSpeed, float dt)
    {
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d < 1e-6f) return (0f, 0f);
        float speed = MathF.Min(maxSpeed, d / dt);
        return (dx / d * speed, dy / d * speed);
    }

    /// <summary>Spec 2485: the turn rate, relative to the ship, toward the place's angle, at most <paramref name="maxTurn"/>.</summary>
    public static float TurnRate(float dAngle, float maxTurn, float dt)
    {
        float a = Wrap(dAngle);
        return MathF.Sign(a) * MathF.Min(maxTurn, MathF.Abs(a) / dt);
    }

    /// <summary>Spec 2485: close enough to snap to the place: 0.05 tiles and half a degree.</summary>
    public static bool Arrived(float dx, float dy, float dAngle) =>
        dx * dx + dy * dy < 0.05f * 0.05f && MathF.Abs(Wrap(dAngle)) < MathF.PI / 360f;
}

namespace HotCargo.Breach;

/// <summary>Pure rules of the crew breach (spec 1610 to 1617). No game types, so the unit tests cover them.</summary>
public static class BreachModel
{
    /// <summary>
    /// Spec 1612: the door between the breach cell and the walkable cell next to it, in the game's door form: a horizontal door lies on the
    /// top edge of its cell, a vertical door on the left edge. The cells must be side neighbors.
    /// </summary>
    public static ((int X, int Y) Cell, bool Horizontal) Door((int X, int Y) breach, (int X, int Y) walk) => (walk.X - breach.X, walk.Y - breach.Y) switch
    {
        (0, -1) => (breach, true),
        (0, 1) => (walk, true),
        (-1, 0) => (breach, false),
        (1, 0) => (walk, false),
        var d => throw new ArgumentException($"the cells {breach} and {walk} are not side neighbors ({d})"),
    };
}

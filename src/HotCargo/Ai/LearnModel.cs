namespace HotCargo.Ai;

/// <summary>Spec 1920: the part kinds whose loss can show a player's focus. The index is the place in the loss sums.</summary>
public enum FocusKind { Weapons, Thrusters, Core }

/// <summary>Spec 1920-1922: the habits that a ship saw, or that a report holds. Focus null: no part focus.</summary>
public readonly record struct Habits(bool Kite, bool Cloud, FocusKind? Focus)
{
    public bool Any => Kite || Cloud || Focus is not null;

    /// <summary>Spec 1922: a new report adds its habits. A new part focus replaces the old one.</summary>
    public Habits Merge(Habits newer) => new(Kite || newer.Kite, Cloud || newer.Cloud, newer.Focus ?? Focus);
}

/// <summary>Enemies learn tactics on plain numbers (spec 1920 to 1939). No game types, so the unit tests cover it.</summary>
public static class LearnModel
{
    /// <summary>
    /// Spec 1920: the part focus from the part health lost by kind (index = FocusKind). There is one when the ship lost at least
    /// minLoss of its full part health in all, and one kind holds share or more of the loss.
    /// </summary>
    public static FocusKind? Focus(IReadOnlyList<float> losses, float fullHealth, float share, float minLoss)
    {
        if (losses.Count != Enum.GetValues<FocusKind>().Length) throw new ArgumentException($"one loss per focus kind, got {losses.Count}", nameof(losses));
        float total = losses.Sum();
        if (fullHealth <= 0f || total < minLoss * fullHealth) return null;
        for (int i = 0; i < losses.Count; i++)
            if (losses[i] >= share * total) return (FocusKind)i;
        return null;
    }

    /// <summary>Spec 1923: a report fades when it is this many jumps old.</summary>
    public static bool Faded(int age, int jumps) => age >= jumps;

    /// <summary>
    /// Spec 1926: the distance from the target, toward the ship, of the first sample point outside the nebula: steps of step, up to
    /// max. Null when the whole line is inside.
    /// </summary>
    public static float? EdgeDistance(Func<float, bool> insideAt, float max, float step)
    {
        if (step <= 0f) throw new ArgumentOutOfRangeException(nameof(step), step, "a step is above 0");
        for (float d = step; d <= max; d += step)
            if (!insideAt(d)) return d;
        return null;
    }
}

namespace HotCargo.RunModes;

/// <summary>
/// Spec 680 to 684: the factors that the vanilla difficulty puts on Hot Cargo's pressure. Pure numbers, no game types.
/// Floats multiply and tier offsets add, so the rows of the three difficulty settings combine in any order.
/// </summary>
public readonly record struct Pressure(float HeatRate, float HunterShips, int HunterTiers, float ContractFees, float TechValue)
{
    public static readonly Pressure Neutral = new(1f, 1f, 0, 1f, 1f);

    public bool IsNeutral => this == Neutral;

    public Pressure Combine(Pressure other) => new(HeatRate * other.HeatRate, HunterShips * other.HunterShips, HunterTiers + other.HunterTiers,
        ContractFees * other.ContractFees, TechValue * other.TechValue);

    /// <summary>Ships of a stage response. Rounded half away from zero, never below 1, so a stage always sends someone.</summary>
    public int Ships(int ships)
    {
        if (ships < 1) throw new ArgumentOutOfRangeException(nameof(ships), ships, "a stage sends at least 1 ship");
        return Math.Max(1, (int)MathF.Round(ships * HunterShips, MidpointRounding.AwayFromZero));
    }
}

public static class PressureModel
{
    /// <summary>Product of the rows. No rows gives Neutral.</summary>
    public static Pressure Combine(IEnumerable<Pressure> rows) => rows.Aggregate(Pressure.Neutral, (a, b) => a.Combine(b));
}

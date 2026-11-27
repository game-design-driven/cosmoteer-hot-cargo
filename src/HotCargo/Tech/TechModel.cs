namespace HotCargo.Tech;

/// <summary>Pure tech math on plain numbers. No game types.</summary>
public static class TechModel
{
    /// <summary>
    /// Spec 19: value of one unit = ship value × (base fraction + fraction per game hour × hours + fraction per 100 notoriety × notoriety / 100).
    /// Ship value is the highest ship value of the peak tier. Game time and notoriety only grow, and the peak tier never drops,
    /// so the value only goes up. Time and notoriety are rounded down to whole steps, so that a sale in flight sees one price.
    /// The result is rounded to a multiple of 100.
    /// </summary>
    public static long Value(int shipValue, float baseFraction, float fractionPerGameHour, float fractionPer100Notoriety,
        float priceStepMinutes, float notorietyStep, double gameSeconds, float notoriety) =>
        (long)ValueTerms(shipValue, 0, baseFraction, fractionPerGameHour, fractionPer100Notoriety, priceStepMinutes, notorietyStep, gameSeconds, notoriety).Total;

    /// <summary>Spec 19, 2583: the steps of <see cref="Value"/>: the base share, game time and notoriety, each as credits; then the round to 100.</summary>
    public static Core.Breakdown ValueTerms(int shipValue, int tier, float baseFraction, float fractionPerGameHour, float fractionPer100Notoriety,
        float priceStepMinutes, float notorietyStep, double gameSeconds, float notoriety)
    {
        if (shipValue <= 0) throw new ArgumentOutOfRangeException(nameof(shipValue), "must be above 0");
        double stepSeconds = Math.Max(1.0, priceStepMinutes * 60.0);
        double steppedHours = Math.Floor(Math.Max(0, gameSeconds) / stepSeconds) * stepSeconds / 3600.0;
        double step = Math.Max(1.0, notorietyStep);
        double steppedNotoriety = Math.Floor(Math.Max(0, notoriety) / step) * step;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new Core.Breakdown(Core.BreakdownUnit.Credits)
            .Base("TechBase", shipValue * (double)baseFraction, tier.ToString(inv))
            .Plus("TechTime", shipValue * fractionPerGameHour * steppedHours, steppedHours.ToString("0.0", inv))
            .Plus("TechNotoriety", shipValue * fractionPer100Notoriety * steppedNotoriety / 100.0, steppedNotoriety.ToString("0", inv))
            .Clamp("Clamp", 0, double.MaxValue)
            .Round("Round", 100);
    }

    /// <summary>Spec 1541: the vaults that hold the units, with perVault units in each (from the vault part rules). Rounded up.</summary>
    public static int VaultsNeeded(int units, int perVault)
    {
        if (units < 0) throw new ArgumentOutOfRangeException(nameof(units), "must not be negative");
        if (perVault <= 0) throw new ArgumentOutOfRangeException(nameof(perVault), "must be above 0");
        return (units + perVault - 1) / perVault;
    }
}

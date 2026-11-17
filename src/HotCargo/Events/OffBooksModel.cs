namespace HotCargo.Events;

/// <summary>Pure rules of Off the Books (spec 1801 to 1812). No game types, so the unit tests cover them.</summary>
public static class OffBooksModel
{
    /// <summary>
    /// Spec 1802: the price of one crate as a share of the tech value: base + perHeat × heat ÷ 100 + perHunter × hunters in the system +
    /// perReputation × reputation (−1 to 1) with the station's faction, kept between min and max.
    /// </summary>
    public static float PriceShare(float heat, int hunters, float reputation, float baseShare, float perHeat, float perHunter, float perReputation,
        float min, float max) =>
        Math.Clamp(baseShare + perHeat * heat / 100f + perHunter * hunters + perReputation * reputation, min, max);

    /// <summary>Spec 1803: the informant chance of one crate: base + perHeat × heat ÷ 100 − perReputation × reputation, at least 0.</summary>
    public static float RiskPerCrate(float heat, float reputation, float baseRisk, float perHeat, float perReputation) =>
        Math.Max(0f, baseRisk + perHeat * heat / 100f - perReputation * reputation);

    /// <summary>
    /// Spec 1805: the chance that the hand-over is a sting: risk per crate × crates + perCorporate × corporate ships near the meet, at most cap.
    /// </summary>
    public static float StingChance(float riskPerCrate, int crates, int corporateNear, float perCorporate, float cap) =>
        Math.Clamp(riskPerCrate * crates + perCorporate * corporateNear, 0f, cap);

    /// <summary>A share or chance as a whole percent, for the page.</summary>
    public static int Percent(float share) => (int)MathF.Round(share * 100f);
}

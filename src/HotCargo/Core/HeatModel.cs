namespace HotCargo.Core;

/// <summary>Pure heat math on plain numbers. No game or tunables types, so unit tests run on the Mac without the game.</summary>
public static class HeatModel
{
    public const float MaxHeat = 100f;

    public static float Multiplier(IReadOnlyList<float> byUnits, int units)
    {
        if (byUnits.Count == 0) throw new ArgumentException("multiplier table is empty", nameof(byUnits));
        return byUnits[Math.Clamp(units, 0, byUnits.Count - 1)];
    }

    public static float TimeHeat(float heatPerMinute, float multiplier, double seconds) =>
        multiplier * heatPerMinute * (float)(seconds / 60.0);

    /// <summary>Heat from fame. Dividing by the standard mission fame of the tier keeps "heat per mission" stable across tiers.</summary>
    public static float FameHeat(float heatPerStandardMission, float multiplier, int fameGained, int standardMissionFame) =>
        fameGained <= 0 ? 0f : multiplier * heatPerStandardMission * fameGained / Math.Max(1, standardMissionFame);

    public static float Clamp(float heat) => Math.Clamp(heat, 0f, MaxHeat);

    /// <summary>
    /// Spec 1817: the share of a paid heat cut that works now: trust (fewer recent buys, more trust) × heat share (heat ÷ fullHeat, at most 1).
    /// </summary>
    public static float BuyFactor(float recentBuys, float heat, float falloff, float fullHeat)
    {
        if (fullHeat <= 0f) throw new ArgumentOutOfRangeException(nameof(fullHeat), $"full heat {fullHeat}");
        return Trust(recentBuys, falloff) * Math.Clamp(heat / fullHeat, 0f, 1f);
    }

    /// <summary>Spec 1817, 1818: the trust left after some uses of a paid deal: 1 ÷ (1 + uses × falloff). Each use lowers it less.</summary>
    public static float Trust(float uses, float falloff)
    {
        if (uses < 0f || falloff < 0f) throw new ArgumentOutOfRangeException(nameof(uses), $"uses {uses}, falloff {falloff}");
        return 1f / (1f + uses * falloff);
    }

    /// <summary>Spec 1817: paid heat cuts are on offer while the factor is at least closeBelow.</summary>
    public static bool BuyOpen(float factor, float closeBelow) => factor >= closeBelow;

    /// <summary>Spec 1817: the count of recent paid heat cuts after some game seconds: it halves every halfLifeMinutes.</summary>
    public static float BuyDecay(float recentBuys, double seconds, float halfLifeMinutes) =>
        halfLifeMinutes <= 0f ? 0f : (float)(recentBuys * Math.Pow(0.5, seconds / (halfLifeMinutes * 60.0)));

    /// <summary>Heat on entry to a system. Jumping back to a hot system does not cool it down.</summary>
    public static float OnEnterSystem(float previousHeat, float storedHeatOfNewSystem, float carryOverFraction) =>
        Clamp(Math.Max(storedHeatOfNewSystem, previousHeat * carryOverFraction));

    /// <summary>Number of stage thresholds (ordered ascending) at or below the heat.</summary>
    public static int StagesReached(float heat, IReadOnlyList<float> thresholds)
    {
        int n = 0;
        while (n < thresholds.Count && heat >= thresholds[n]) n++;
        return n;
    }
}

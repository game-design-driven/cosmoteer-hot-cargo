namespace HotCargo.Diplomacy;

/// <summary>Spec 1524: what an AI ship does with its odds.</summary>
public enum Stance { Surrender, Flee, Fight, Demand }

/// <summary>Spec 1524: the confidence thresholds of the stances.</summary>
public readonly record struct StanceThresholds(float DemandAt, float FleeAt, float SurrenderAt);

/// <summary>Pure math of the risk model (spec 1520 to 1528). No game types.</summary>
public static class RiskModel
{
    /// <summary>Spec 1520: firepower × health × crew factor. Health and crew are shares 0..1; crew is the filled share of the crew places.</summary>
    public static float Strength(float firepower, float health, float crew, float crewWeight) =>
        Math.Max(0f, firepower) * DiplomacyModel.Clamp01(health) * (1f - DiplomacyModel.Clamp01(crewWeight) * (1f - DiplomacyModel.Clamp01(crew)));

    /// <summary>Spec 1522: (own range ÷ enemy range) ^ weight, clamped to 0.5..2. Without a range on either side it is 1.</summary>
    public static float RangeFactor(float ownRange, float enemyRange, float weight) =>
        ownRange <= 0f || enemyRange <= 0f ? 1f : Math.Clamp(MathF.Pow(ownRange / enemyRange, weight), 0.5f, 2f);

    /// <summary>Spec 1522: AI side strength ÷ players' side strength × range factor. No players' strength: infinite; no own strength: 0.</summary>
    public static float Odds(float own, float enemy, float rangeFactor)
    {
        if (own < 0 || enemy < 0) throw new ArgumentOutOfRangeException(nameof(own), $"strengths {own}, {enemy}");
        if (enemy == 0) return own == 0 ? 1f : float.PositiveInfinity;
        return own / enemy * rangeFactor;
    }

    /// <summary>Spec 1523: the group base ± spread, from a roll 0..1, clamped to 0..1.</summary>
    public static float Bravery(float groupBase, float spread, float roll) =>
        DiplomacyModel.Clamp01(groupBase + spread * (2f * DiplomacyModel.Clamp01(roll) - 1f));

    /// <summary>Spec 1523: odds × (0.5 + bravery).</summary>
    public static float Confidence(float odds, float bravery) => odds * (0.5f + DiplomacyModel.Clamp01(bravery));

    /// <summary>Spec 1524: demand at or above DemandAt, surrender at or below SurrenderAt, flee at or below FleeAt, else fight.
    /// proud: a corporate hunter never surrenders; it flees instead.</summary>
    public static Stance Decide(float confidence, StanceThresholds t, bool proud)
    {
        if (confidence >= t.DemandAt) return Stance.Demand;
        if (confidence <= t.SurrenderAt) return proud ? Stance.Flee : Stance.Surrender;
        return confidence <= t.FleeAt ? Stance.Flee : Stance.Fight;
    }

    /// <summary>Spec 1528: the allies line from its format, or null when no ally was counted. {0} is the counted number.</summary>
    public static string? AlliesLine(string format, int alliesCounted) =>
        alliesCounted <= 0 ? null : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, alliesCounted);
}

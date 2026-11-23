namespace HotCargo.Debris;

public enum DebrisPhase { Waiting, Glowing, Burning }

/// <summary>Pure rules of the debris decay (spec 1242, 1243). No game types, so the unit tests cover them.</summary>
public static class DebrisModel
{
    /// <summary>The phase of a junk piece that could decay for ageSeconds: it waits, glows for the last glowSeconds, then burns up.</summary>
    public static DebrisPhase Phase(double ageSeconds, double decaySeconds, double glowSeconds)
    {
        if (ageSeconds >= decaySeconds) return DebrisPhase.Burning;
        return ageSeconds >= decaySeconds - glowSeconds ? DebrisPhase.Glowing : DebrisPhase.Waiting;
    }

    /// <summary>Glow strength from 0 (the glow starts) to 1 (the burn-up), for the outline color. 0 outside the glow.</summary>
    public static float GlowLevel(double ageSeconds, double decaySeconds, double glowSeconds)
    {
        if (glowSeconds <= 0 || Phase(ageSeconds, decaySeconds, glowSeconds) != DebrisPhase.Glowing) return 0f;
        return (float)Math.Clamp((ageSeconds - (decaySeconds - glowSeconds)) / glowSeconds, 0, 1);
    }

    /// <summary>
    /// Spec 1248: quantity × factor as a whole number. The fraction becomes one more unit when roll (0 to 1) is below it, so the
    /// mean is quantity × factor also for the single-unit ore of a deposit part.
    /// </summary>
    public static int ScaleQuantity(int quantity, float factor, float roll)
    {
        if (quantity < 0 || factor < 0) throw new ArgumentOutOfRangeException(nameof(factor), $"quantity {quantity} and factor {factor} must not be negative");
        double exact = quantity * (double)factor;
        int whole = (int)Math.Floor(exact);
        return whole + (roll < exact - whole ? 1 : 0);
    }

    /// <summary>Spec 1248: the ore factor of a part by its salvage share: weapon breaks get the mining factor, laser breaks the laser factor.</summary>
    public static float OreFactor(float salvageFraction, float miningFactor, float laserFactor) =>
        miningFactor + (laserFactor - miningFactor) * Math.Clamp(salvageFraction, 0f, 1f);

    /// <summary>
    /// Spec 1832: the salvage cut of one resource. A resource whose rules BuyPrice is at least componentPrice is an expensive
    /// component (processor, diamond, enriched uranium) and gets the mild componentFactor; every cheaper resource gets baseFactor.
    /// </summary>
    public static float SalvageFactor(int buyPrice, float baseFactor, float componentFactor, int componentPrice) =>
        buyPrice >= componentPrice ? componentFactor : baseFactor;

    /// <summary>Sim ticks between two checks: at least 1, so a very short check time checks every tick.</summary>
    public static int CheckTicks(double checkSeconds, double tickSeconds) =>
        tickSeconds <= 0 ? 1 : Math.Max(1, (int)Math.Round(checkSeconds / tickSeconds));
}

namespace HotCargo.MultiChoice;

/// <summary>Pure math of multiselect pages (spec 590 and later). No game types, so the unit tests run on the Mac.</summary>
public static class MultiChoiceModel
{
    /// <summary>The chances of the selected options add up, capped at <paramref name="cap"/> and kept in 0..1.</summary>
    public static float Combine(IEnumerable<float> chances, float cap) =>
        (float)CombineTerms(chances.Select(c => ("Option", c, (string?)null)), 1f, null, cap).Total;

    /// <summary>
    /// Spec 594, 2584: the steps of a combined chance: one addend for each option (text key, chance, text argument), then the factor
    /// on the whole offer (key, or null for none), then the cap and the 0..1 range.
    /// </summary>
    public static Core.Breakdown CombineTerms(IEnumerable<(string Key, float Chance, string? Arg)> options, float factor, string? factorKey, float cap,
        string? factorArg = null)
    {
        if (float.IsNaN(cap) || float.IsNaN(factor)) throw new ArgumentOutOfRangeException(nameof(cap), $"cap {cap}, factor {factor}");
        var b = new Core.Breakdown(Core.BreakdownUnit.Chance).Base("OfferNone", 0);
        foreach (var (key, chance, arg) in options) b.Plus(key, chance, arg);
        if (factorKey is not null) b.Times(factorKey, factor, factorArg);
        else if (factor != 1f) throw new ArgumentException("a factor needs a text key", nameof(factorKey));
        return b.Clamp("Clamp", 0, Math.Clamp(cap, 0f, 1f));
    }

    /// <summary>A slider option's chance: 0 at <paramref name="min"/>, <paramref name="atMax"/> at <paramref name="max"/>, linear between.</summary>
    public static float Scaled(int value, int min, int max, float atMax)
    {
        if (max < min) throw new ArgumentOutOfRangeException(nameof(max));
        if (max == min) return value >= max ? atMax : 0f;
        return atMax * Math.Clamp((value - min) / (float)(max - min), 0f, 1f);
    }

    /// <summary>The value kept in min..max on the step grid, like the slider shows it.</summary>
    public static int Snap(int value, int min, int max, int step)
    {
        if (max < min || step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        int v = Math.Clamp(value, min, max);
        return Math.Min(max, min + (v - min + step / 2) / step * step);
    }
}

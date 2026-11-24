namespace HotCargo.Journey;

/// <summary>Spec 2414: pure rules for the galaxy tunables (no game types).</summary>
public static class MapRulesModel
{
    /// <summary>
    /// A tier value of the galaxy rules that derives from FinalSectorTier: the value at or above the rules' final tier moves with the
    /// tunable, a lower one stays. The galaxy rules have three such values: the final tier itself, the faction home tiers above it
    /// (FinalSectorTier + TierSpread + 1, and that minus TierSpread), and the cap of changeable tiers. The low ends (1, 1 + TierSpread) stay.
    /// </summary>
    public static int Shift(int rulesValue, int rulesFinal, int final) => rulesValue >= rulesFinal ? rulesValue + final - rulesFinal : rulesValue;

    /// <summary>The weight of a system type: its entry in the tunable list, else 0 (a type that the list leaves out does not appear).</summary>
    public static float Weight(IEnumerable<(string Type, float Weight)> weights, string type) =>
        weights.Where(w => w.Type == type).Select(w => w.Weight).DefaultIfEmpty(0f).Last();
}

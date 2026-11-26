namespace HotCargo.Journey;

/// <summary>Spec 2414: pure rules for the galaxy tunables (no game types).</summary>
public static class MapRulesModel
{
    /// <summary>The weight of a system type: its entry in the tunable list, else 0 (a type that the list leaves out does not appear).</summary>
    public static float Weight(IEnumerable<(string Type, float Weight)> weights, string type) =>
        weights.Where(w => w.Type == type).Select(w => w.Weight).DefaultIfEmpty(0f).Last();
}

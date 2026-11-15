namespace HotCargo.Tactics;

/// <summary>
/// Spec 1305. Where a ship that falls back repairs. None: no plan picked yet.
/// The values are saved with the ship, so a new plan goes at the end; the pick order is in PickPlan.
/// </summary>
public enum RepairPlan { None, Cover, Pool, Station, Away, Wreck }

/// <summary>What the ship can reach when it picks a plan.</summary>
public readonly record struct RepairOptions(bool HasRock, bool HasPoolMate, bool HasWreck, bool HasStation);

/// <summary>Pure repair plan decisions (spec 1305-1309) on plain numbers. No game types.</summary>
public static class RepairModel
{
    /// <summary>Spec 1305: the first plan that applies, in the order cover, pool, wreck, station, away.</summary>
    public static RepairPlan PickPlan(bool lacksResources, bool shortOfCrew, RepairOptions o)
    {
        if (!lacksResources && !shortOfCrew && o.HasRock) return RepairPlan.Cover;
        if (lacksResources && o.HasPoolMate) return RepairPlan.Pool;
        if (lacksResources && o.HasWreck) return RepairPlan.Wreck;
        if ((lacksResources || shortOfCrew) && o.HasStation) return RepairPlan.Station;
        return RepairPlan.Away;
    }

    /// <summary>Spec 1305: the ship is short of crew below this fraction of its crew places. A ship without crew places is never short.</summary>
    public static bool ShortOfCrew(int crew, int places, float fraction) => places > 0 && crew < places * fraction;

    /// <summary>Spec 1305: per resource type, the cost minus what the ship can use, for the types where that is above 0.</summary>
    public static Dictionary<string, int> Lacking(IReadOnlyDictionary<string, int> cost, Func<string, int> usable)
    {
        var lacking = new Dictionary<string, int>();
        foreach (var (type, need) in cost.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (need < 0) throw new ArgumentOutOfRangeException(nameof(cost), $"{type}: cost {need}");
            int missing = need - Math.Max(0, usable(type));
            if (missing > 0) lacking[type] = missing;
        }
        return lacking;
    }

    /// <summary>Spec 1309: pool, wreck and station trips get the longer time limit.</summary>
    public static float TimeLimit(RepairPlan plan, float maxSeconds, float tripSeconds) =>
        plan is RepairPlan.Pool or RepairPlan.Wreck or RepairPlan.Station ? tripSeconds : maxSeconds;
}

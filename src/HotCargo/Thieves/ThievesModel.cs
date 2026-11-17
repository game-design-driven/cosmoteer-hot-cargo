namespace HotCargo.Thieves;

/// <summary>What a thief's guns aim at first (spec 1776): lower is first.</summary>
public enum RobRank { FullStorage = 0, Storage = 1, Other = 2, Never = 3 }

/// <summary>One loose resource for the trigger sum (spec 1778): whether a player part dropped it, its distance to the nearest thief, units and unit value.</summary>
public readonly record struct Loose(bool FromPlayer, float Distance, int Units, int UnitValue);

/// <summary>Pure rules of the pirate thieves (spec 1770 to 1799). No game types, so the unit tests cover them.</summary>
public static class ThievesModel
{
    /// <summary>
    /// Spec 1776: the rank of a part. A storage part that holds resources first, then an empty one, then any other part. Never the command
    /// part or a reactor: the thieves want the cargo, not the ship.
    /// </summary>
    public static RobRank Rank(bool storage, bool holdsResources, bool command, bool reactor) =>
        command || reactor ? RobRank.Never : storage ? holdsResources ? RobRank.FullStorage : RobRank.Storage : RobRank.Other;

    /// <summary>
    /// Spec 1776: the index of the part to aim at, or -1 when every part ranks Never: the lowest rank, then the nearest. The current part
    /// (index current) counts switchFactor times nearer, so the guns do not jump between parts of the same rank.
    /// </summary>
    public static int Pick(IReadOnlyList<(RobRank Rank, float Distance)> parts, int current, float switchFactor)
    {
        int best = -1;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Rank == RobRank.Never) continue;
            if (best < 0 || Better(parts[i], i == current, parts[best], best == current, switchFactor)) best = i;
        }
        return best;
    }

    static bool Better((RobRank Rank, float Distance) a, bool aCurrent, (RobRank Rank, float Distance) b, bool bCurrent, float switchFactor)
    {
        if (a.Rank != b.Rank) return a.Rank < b.Rank;
        float da = aCurrent ? a.Distance / switchFactor : a.Distance, db = bCurrent ? b.Distance / switchFactor : b.Distance;
        return da < db;
    }

    /// <summary>Spec 1778: the value of the loose resources that player parts dropped within the radius of the thieves.</summary>
    public static long LootValue(IEnumerable<Loose> loose, float radius) =>
        loose.Where(l => l.FromPlayer && l.Distance <= radius).Sum(l => (long)l.Units * l.UnitValue);

    /// <summary>Spec 1778: the trigger value of a group: share × the vanilla value maximum of its tier.</summary>
    public static long Threshold(float share, int tierValueMaximum) => (long)Math.Ceiling(share * tierValueMaximum);
}

namespace HotCargo.Missions.Mechanics;

/// <summary>Pure math of the mechanics missions. No game types, so unit tests run on the Mac without the game.</summary>
public static class MechanicsModel
{
    /// <summary>
    /// Spec 132: ships in the next hold-out wave. Base + perTier × tier, rounded, then capped so that the live wave ships stay at maxLive or below.
    /// </summary>
    public static int WaveShipCount(float shipsBase, float shipsPerTier, int tier, int liveShips, int maxLive)
    {
        int wanted = Math.Max(0, (int)MathF.Round(shipsBase + shipsPerTier * tier, MidpointRounding.AwayFromZero));
        return Math.Clamp(wanted, 0, Math.Max(0, maxLive - liveShips));
    }

    /// <summary>Spec 132: tier of each wave ship. Base + perTier × tier, rounded, in 1..maxTier.</summary>
    public static int WaveShipTier(float tierBase, float tierPerTier, int tier, int maxTier) =>
        Math.Clamp((int)MathF.Round(tierBase + tierPerTier * tier, MidpointRounding.AwayFromZero), 1, Math.Max(1, maxTier));

    /// <summary>Hold-out waves that are due after the guarded clock moves from before to after. The first wave is at firstWave, then one every interval.</summary>
    public static int WavesDue(double before, double after, double firstWave, double interval)
    {
        if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval), interval, "must be above 0");
        static int Count(double t, double first, double step) => t < first ? 0 : 1 + (int)Math.Floor((t - first) / step);
        return Math.Max(0, Count(after, firstWave, interval) - Count(before, firstWave, interval));
    }

    /// <summary>Wave intervals in a hold of total seconds: total ÷ interval, rounded up, at least 1.</summary>
    public static int Intervals(double total, double interval)
    {
        if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval), interval, "must be above 0");
        return Math.Max(1, (int)Math.Ceiling(total / interval));
    }

    /// <summary>
    /// Spec 141: the share of the pay that leaving early keeps. One share for each whole wave interval survived, out of Intervals(total, interval).
    /// </summary>
    public static float EarnedFraction(double guarded, double total, double interval) =>
        Math.Clamp((float)Math.Floor(Math.Max(0, guarded) / interval) / Intervals(total, interval), 0f, 1f);

    /// <summary>Gap between two circles, 0 when they touch or overlap.</summary>
    public static float Gap(float centerDistance, float radiusA, float radiusB) => Math.Max(0f, centerDistance - radiusA - radiusB);

    /// <summary>
    /// Spec 1861: the ore that fills the miner's hold. The free room for that ore when the miner first goes live, capped at oreGoal,
    /// so a large hold does not make the job long. A miner without room is a broken design.
    /// </summary>
    public static int OreGoal(int room, int oreGoal)
    {
        if (room <= 0) throw new ArgumentOutOfRangeException(nameof(room), room, "the miner has no room for the ore");
        if (oreGoal <= 0) throw new ArgumentOutOfRangeException(nameof(oreGoal), oreGoal, "must be above 0");
        return Math.Min(room, oreGoal);
    }

    /// <summary>
    /// Spec 1863: mining waves that are due when the ore moves from before to after, out of goal. A wave comes when the ore first
    /// reaches each share of the goal, so every wave comes once, also when players' ore makes the count jump.
    /// </summary>
    public static int MiningWavesDue(int before, int after, int goal, IReadOnlyList<float> shares)
    {
        if (goal <= 0) throw new ArgumentOutOfRangeException(nameof(goal), goal, "must be above 0");
        return shares.Count(s => before < s * goal && after >= s * goal);
    }

    /// <summary>Whole seconds left on a countdown, rounded up, never below 0.</summary>
    public static int SecondsLeft(double total, double elapsed) => Math.Max(0, (int)Math.Ceiling(total - elapsed));
}

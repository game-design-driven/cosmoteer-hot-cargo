namespace HotCargo.Ai;

/// <summary>Spec 1902: how a kited ship answers. None: it is not kited.</summary>
public enum KiteAnswer { None, CloseIn, Call, Team, BreakOff }

/// <summary>Counter kiting on plain numbers (spec 1900 to 1919). No game types, so the unit tests cover it.</summary>
public static class CounterKiteModel
{
    /// <summary>
    /// Spec 1900: the kiter reaches the ship (edge to edge), and the armed ship does not reach the kiter. A ship without a weapon
    /// range is not kited: it flees (requirement 194).
    /// </summary>
    public static bool Outranged(float edge, float ownRange, float kiterRange) => ownRange > 0f && edge <= kiterRange && edge > ownRange;

    /// <summary>
    /// Spec 1900: how far a kiter reaches: its longest weapon range. Some weapons report no range limit (an infinite range); they
    /// reach the kiter's radar radius. A radar radius that is not finite is a bug.
    /// </summary>
    public static float Reach(float weaponRange, float radarRadius)
    {
        if (!float.IsFinite(radarRadius) || radarRadius < 0f) throw new ArgumentOutOfRangeException(nameof(radarRadius), radarRadius, "a radar radius is finite and not negative");
        return float.IsFinite(weaponRange) ? weaponRange : radarRadius;
    }

    /// <summary>
    /// Spec 1901: the ship is kited when it stayed outranged for readSeconds and lost readLoss of its health or more in that time.
    /// lost: the health fraction at the window start minus the health fraction now.
    /// </summary>
    public static bool Kited(double windowSeconds, float lost, float readSeconds, float readLoss) => windowSeconds >= readSeconds && lost >= readLoss;

    /// <summary>
    /// Spec 1902: the answer of a kited ship, first match. With personalities: a fast ship (agility closeInAgility or more) with the
    /// Brawl or Pincer trait closes in, a ship with Call calls, a ship with Team regroups, any other ship breaks off. Without
    /// personalities only the build decides: fast ships close in, the others break off.
    /// </summary>
    public static KiteAnswer Pick(TraitSet traits, bool personalities, float agility, float closeInAgility)
    {
        bool fast = agility >= closeInAgility;
        if (!personalities) return fast ? KiteAnswer.CloseIn : KiteAnswer.BreakOff;
        if (fast && (traits.Has(Trait.Brawl) || traits.Has(Trait.Pincer))) return KiteAnswer.CloseIn;
        if (traits.Has(Trait.Call)) return KiteAnswer.Call;
        if (traits.Has(Trait.Team)) return KiteAnswer.Team;
        return KiteAnswer.BreakOff;
    }

    /// <summary>
    /// Spec 1903: a close in fails when, after closeInSeconds, the gap (edge to edge) shrank by less than closeInGain of the gap
    /// at the start. Then the ship breaks off.
    /// </summary>
    public static bool CloseInFailed(double seconds, float startGap, float gap, float closeInSeconds, float closeInGain) =>
        seconds >= closeInSeconds && startGap - gap < closeInGain * startGap;

    /// <summary>
    /// Spec 1906: the side of the kiter that an attacker takes (radians, from the kiter to the attacker). With a nebula point: the far
    /// side of the kiter from the nebula, spread by the pincer offsets of the attacker's slot. Without one: the far side from the
    /// other attackers' center. Null when neither applies (one attacker and no nebula).
    /// </summary>
    public static float? AnvilAngle((float X, float Y) kiter, (float X, float Y)? nebula, (float X, float Y)? othersCenter, int slot, int groupSize,
        float spreadDegrees)
    {
        if (nebula is { } n)
            return Angle(n, kiter) + MathF.PI / 180f * Tactics.TacticsModel.PincerOffsetDegrees(slot, groupSize, spreadDegrees);
        return othersCenter is { } c ? Angle(c, kiter) : null;
    }

    /// <summary>The direction from a to b in radians, 0 along +X.</summary>
    static float Angle((float X, float Y) a, (float X, float Y) b) => MathF.Atan2(b.Y - a.Y, b.X - a.X);
}

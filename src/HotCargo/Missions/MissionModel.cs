using System.Numerics;

namespace HotCargo.Missions;

/// <summary>Pure mission math on plain numbers. No game types, so unit tests run on the Mac without the game.</summary>
public static class MissionModel
{
    /// <summary>Spec requirement 39: chance that a shady mission is a trap, clamped to 0..1.</summary>
    public static float TrapChance(float chanceBase, float chancePerHeat, float heat) =>
        Math.Clamp(chanceBase + chancePerHeat * heat, 0f, 1f);

    /// <summary>A roll in [0, 1) below the chance is a trap. Chance 0 is never a trap, chance 1 is always a trap.</summary>
    public static bool IsTrap(float roll01, float chance)
    {
        if (roll01 is < 0f or >= 1f) throw new ArgumentOutOfRangeException(nameof(roll01), roll01, "must be in [0, 1)");
        return roll01 < chance;
    }

    /// <summary>Scales a reward amount and rounds it to the nearest multiple. A positive amount never scales below one multiple.</summary>
    public static int ScaleReward(int amount, float factor, int roundToMultiple)
    {
        if (roundToMultiple < 1) throw new ArgumentOutOfRangeException(nameof(roundToMultiple), roundToMultiple, "must be 1 or more");
        if (factor < 0f) throw new ArgumentOutOfRangeException(nameof(factor), factor, "must not be negative");
        int scaled = (int)Math.Round(amount * (double)factor / roundToMultiple, MidpointRounding.AwayFromZero) * roundToMultiple;
        return amount > 0 && factor > 0f ? Math.Max(roundToMultiple, scaled) : scaled;
    }

    /// <summary>Directions that ConvoyDestination tries: the preferred one, then turns of this many radians to each side in turn.</summary>
    const double RouteTurnStep = Math.PI / 12;

    /// <summary>
    /// Spec 36: the convoy destination, <paramref name="length"/> from the start (center of the system at 0, 0).
    /// It tries the preferred direction first, then turns in steps to each side in turn, until the destination is within
    /// <paramref name="reach"/> of the center and the straight route keeps <paramref name="sunClearance"/> from every sun.
    /// Null when no direction fits.
    /// </summary>
    public static Vector2? ConvoyDestination(Vector2 start, Vector2 preferredDirection, float length, float reach, float sunClearance, IReadOnlyList<Vector2> suns) =>
        RouteCandidates(start, preferredDirection, length, reach, sunClearance, suns) is [var first, ..] ? first : null;

    /// <summary>
    /// Spec 36, 1890: every destination that fits the convoy route rules, in the order that ConvoyDestination tries them: the preferred
    /// direction, then turns of RouteTurnStep to each side in turn.
    /// </summary>
    public static List<Vector2> RouteCandidates(Vector2 start, Vector2 preferredDirection, float length, float reach, float sunClearance, IReadOnlyList<Vector2> suns)
    {
        if (length <= 0f) throw new ArgumentOutOfRangeException(nameof(length), length, "must be above 0");
        if (preferredDirection == Vector2.Zero) throw new ArgumentException("must not be zero", nameof(preferredDirection));
        double baseAngle = Math.Atan2(preferredDirection.Y, preferredDirection.X);
        int steps = (int)Math.Round(2 * Math.PI / RouteTurnStep);
        var fits = new List<Vector2>();
        for (int i = 0; i < steps; i++)
        {
            int turn = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1); // 0, +1, -1, +2, -2, ...
            double angle = baseAngle + turn * RouteTurnStep;
            Vector2 dest = start + length * new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            if (dest.Length() <= reach && suns.All(s => SegmentDistance(start, dest, s) >= sunClearance)) fits.Add(dest);
        }
        return fits;
    }

    /// <summary>
    /// Spec 1890: the encounters within <paramref name="range"/> of the straight route from start to destination, and the sum of their
    /// weights. A blocking encounter (a hostile ship) within <paramref name="clear"/> of the destination rules the destination out (null):
    /// the courier must be able to arrive.
    /// </summary>
    public static (float Score, List<RouteEncounter> Past)? RouteEncounters(Vector2 start, Vector2 dest, IReadOnlyList<RouteEncounter> encounters, float range, float clear)
    {
        if (range <= 0f) throw new ArgumentOutOfRangeException(nameof(range), range, "must be above 0");
        if (encounters.Any(e => e.Blocks && Vector2.Distance(e.At, dest) < clear)) return null;
        var past = encounters.Where(e => SegmentDistance(start, dest, e.At) <= range).ToList();
        return (past.Sum(e => e.Weight), past);
    }

    /// <summary>Spec 1890: the group with the most encounter weight (a tie goes to the lower name), or null when there is none.</summary>
    public static string? MainGroup(IReadOnlyList<RouteEncounter> encounters) =>
        encounters.GroupBy(e => e.Group).OrderByDescending(g => g.Sum(e => e.Weight)).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault();

    /// <summary>
    /// Spec 1890: the candidate whose straight route passes the most encounter weight; a tie goes to the earlier candidate (closer to the
    /// preferred direction). Null when every candidate is ruled out.
    /// </summary>
    public static (Vector2 Destination, List<RouteEncounter> Past)? BestEncounterRoute(Vector2 start, IReadOnlyList<Vector2> candidates,
        IReadOnlyList<RouteEncounter> encounters, float range, float clear)
    {
        (Vector2, List<RouteEncounter>)? best = null;
        float bestScore = -1f;
        foreach (var dest in candidates)
            if (RouteEncounters(start, dest, encounters, range, clear) is { } r && r.Score > bestScore)
            {
                best = (dest, r.Past);
                bestScore = r.Score;
            }
        return best;
    }

    /// <summary>Distance from a point to the segment from a to b.</summary>
    public static float SegmentDistance(Vector2 a, Vector2 b, Vector2 point)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared == 0f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + t * ab);
    }

    /// <summary>
    /// Spec 1851: where a waiting escort stops. It is on the line from the player ship to the escort, <paramref name="gap"/> from the
    /// player's hull, closer when the player's comms circle (<paramref name="commsRadius"/> from its center) would not reach the escort
    /// with <paramref name="commsMargin"/> to spare, but never inside the player's hull. Never more than <paramref name="meetRange"/>
    /// from the escort's start.
    /// </summary>
    public static Vector2 MeetSpot(Vector2 player, float playerRadius, float commsRadius, Vector2 escort, float escortRadius, float gap, float commsMargin, Vector2 start, float meetRange)
    {
        if (gap < 0f) throw new ArgumentOutOfRangeException(nameof(gap), gap, "must not be negative");
        if (meetRange < 0f) throw new ArgumentOutOfRangeException(nameof(meetRange), meetRange, "must not be negative");
        Vector2 away = escort - player;
        away = away == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(away);
        float reachGap = Math.Max(0f, commsRadius - playerRadius - commsMargin);
        Vector2 spot = player + away * (playerRadius + escortRadius + Math.Min(gap, reachGap));
        Vector2 fromStart = spot - start;
        return fromStart.Length() <= meetRange ? spot : start + Vector2.Normalize(fromStart) * meetRange;
    }

    /// <summary>
    /// Spec 1892: where a following ship goes. It is on the line from the lead ship to the follower, <paramref name="gap"/> from the lead's
    /// hull, so it stops where the lead stops.
    /// </summary>
    public static Vector2 FollowSpot(Vector2 lead, float leadRadius, Vector2 follower, float followerRadius, float gap)
    {
        if (gap < 0f) throw new ArgumentOutOfRangeException(nameof(gap), gap, "must not be negative");
        Vector2 away = follower - lead;
        away = away == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(away);
        return lead + away * (leadRadius + followerRadius + gap);
    }

    /// <summary>Spec 1852: true when the straight route keeps <paramref name="clearance"/> from every sun.</summary>
    public static bool RouteClear(Vector2 from, Vector2 to, float clearance, IEnumerable<Vector2> suns) =>
        suns.All(s => SegmentDistance(from, to, s) >= clearance);

    /// <summary>Share of the route that the convoy covered, 0 at the start and 1 at the destination. Never below 0.</summary>
    public static float RouteProgress(float routeLength, float distanceLeft)
    {
        if (routeLength <= 0f) throw new ArgumentOutOfRangeException(nameof(routeLength), routeLength, "must be above 0");
        return Math.Max(0f, 1f - distanceLeft / routeLength);
    }

    /// <summary>
    /// Spec 1882 to 1886: one sim tick of the alone clock of an escort that is under way. A guarded escort resets the clock and can draw a
    /// new raid later. Unguarded, the clock runs. It starts with a warning; at <paramref name="raidSeconds"/> a live escort draws one raid
    /// (an escort in stasis draws it when it is live again, because raiders that spawn in stasis do not move); <paramref name="warnSeconds"/>
    /// before <paramref name="lostSeconds"/> a second warning comes; at <paramref name="lostSeconds"/> the escort is lost, live or in stasis.
    /// </summary>
    public static EscortGuardEvent GuardStep(ref double exposed, ref bool raided, double seconds, bool guarded, bool live, float raidSeconds, float lostSeconds, float warnSeconds)
    {
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "must not be negative");
        if (raidSeconds <= 0f || lostSeconds <= raidSeconds) throw new ArgumentOutOfRangeException(nameof(lostSeconds), lostSeconds, "need 0 < raidSeconds < lostSeconds");
        if (warnSeconds <= 0f || warnSeconds >= lostSeconds) throw new ArgumentOutOfRangeException(nameof(warnSeconds), warnSeconds, "need 0 < warnSeconds < lostSeconds");
        if (guarded)
        {
            exposed = 0;
            raided = false;
            return EscortGuardEvent.None;
        }
        double before = exposed;
        exposed += seconds;
        var events = EscortGuardEvent.None;
        if (before == 0 && exposed > 0) events |= EscortGuardEvent.Alone;
        if (before < lostSeconds - warnSeconds && exposed >= lostSeconds - warnSeconds) events |= EscortGuardEvent.Warning;
        if (live && !raided && exposed >= raidSeconds)
        {
            raided = true;
            events |= EscortGuardEvent.Raid;
        }
        if (exposed >= lostSeconds) events |= EscortGuardEvent.Lost;
        return events;
    }

    /// <summary>Spec 1886: whole seconds left on the alone clock, never below 0.</summary>
    public static int SecondsLeft(float lostSeconds, double exposed) => (int)Math.Max(0, Math.Ceiling(lostSeconds - exposed));
}

/// <summary>What the alone clock of an escort under way asks for in a tick (spec 1882 to 1886). One tick can ask for more than one.</summary>
[Flags]
public enum EscortGuardEvent
{
    None = 0,
    /// <summary>The clock starts: warn the players.</summary>
    Alone = 1,
    /// <summary>EscortLostWarnSeconds are left: warn again.</summary>
    Warning = 2,
    /// <summary>Raiders find the lone escort: spawn one raid around it.</summary>
    Raid = 4,
    /// <summary>The clock ran out: raiders take the escort, so the mission fails.</summary>
    Lost = 8,
}

/// <summary>Spec 1890: something near a route that the players must deal with or go around: a hostile ship or a nebula cell.</summary>
/// <param name="Group">What the hint names it by, for example a faction and whether it is a station. Equal groups count together.</param>
/// <param name="Blocks">A destination this close to it does not fit (hostile ships; a nebula does not block).</param>
public readonly record struct RouteEncounter(Vector2 At, float Weight, string Group, bool Blocks);


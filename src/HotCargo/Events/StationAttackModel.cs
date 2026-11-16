using System.Numerics;

namespace HotCargo.Events;

/// <summary>How a station attack ends (spec 94).</summary>
public enum AttackEnd { None, Repelled, Fell, Held }

/// <summary>Pure rules of the station attack (spec 90 to 98). No game types, so the unit tests cover them.</summary>
public static class StationAttackModel
{
    /// <summary>
    /// Spec 93: a player ship is in the way of a raider when its hull is within <paramref name="range"/> of the raider's hull, or when it
    /// sits on the line of fire: the line from the raider to the station passes through the player's hull circle, between the two.
    /// </summary>
    public static bool InTheWay(Vector2 raider, float raiderRadius, Vector2 station, Vector2 player, float playerRadius, float range)
    {
        if (Vector2.Distance(raider, player) - raiderRadius - playerRadius <= range) return true;
        Vector2 line = station - raider;
        float length2 = line.LengthSquared();
        if (length2 <= 0f) return false;
        float t = Vector2.Dot(player - raider, line) / length2;
        if (t <= 0f || t >= 1f) return false;
        return Vector2.DistanceSquared(raider + line * t, player) <= playerRadius * playerRadius;
    }

    /// <summary>
    /// Spec 94: the end of the attack, or None while it goes on. The raiders are gone: Repelled. The station's health is below the
    /// fall health: Fell. The time is over: Held. A station that is gone counts as fallen.
    /// </summary>
    public static AttackEnd End(int raidersLeft, bool stationExists, float stationHealth, float fallHealth, double seconds, double maxSeconds)
    {
        if (raidersLeft == 0) return AttackEnd.Repelled;
        if (!stationExists || stationHealth < fallHealth) return AttackEnd.Fell;
        return seconds >= maxSeconds ? AttackEnd.Held : AttackEnd.None;
    }

    /// <summary>
    /// Spec 96: the station parts that a step rebuilds from the debris parts that it takes in: <paramref name="perDebrisPart"/> for each,
    /// at most the destroyed parts.
    /// </summary>
    public static int RebuildCount(int debrisParts, float perDebrisPart, int destroyed) =>
        Math.Clamp((int)MathF.Ceiling(debrisParts * perDebrisPart), 0, destroyed);
}

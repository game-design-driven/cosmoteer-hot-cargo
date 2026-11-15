namespace HotCargo.Tactics;

/// <summary>What the pool rule needs to know about one ship. No game types.</summary>
public readonly record struct PoolSide(int Player, int Team, bool Human, bool Ai, bool Station, bool Junk);

/// <summary>
/// Spec 1307, 1318. One rule decides whether two ships pool construction resources and help each other build, as the ships of one
/// player do in vanilla (Ship.IsSamePlayerAs).
/// </summary>
public static class PoolModel
{
    /// <summary>
    /// Vanilla: the same player index, never a negative (special) index (SimModeManager.AreSamePlayer).
    /// Spec 1307: two AI ships of one team (non-negative indexes), not stations, not junk, while factionPool is on.
    /// Spec 1318: two human ships of different players while the co-op switch is on.
    /// </summary>
    public static bool Pools(PoolSide a, PoolSide b, bool factionPool, bool sharedPlayers)
    {
        if (a.Player >= 0 && a.Player == b.Player) return true;
        if (a.Junk || b.Junk) return false;
        if (a.Human || b.Human) return sharedPlayers && a.Human && b.Human;
        // Special (negative) indexes are never allies (SimModeManager.AreAllies).
        return factionPool && a.Ai && b.Ai && !a.Station && !b.Station && a.Player >= 0 && b.Player >= 0 && a.Team == b.Team;
    }
}

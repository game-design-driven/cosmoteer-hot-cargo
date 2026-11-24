namespace HotCargo.Ux;

/// <summary>
/// Spec 2121: where the players' tech is when it is not in a vault of a player ship. The order is the line order: the most urgent first.
/// Design: a ship whose design holds less tech in vaults than the ship carries. Storage: normal storage of a player ship (it decays).
/// Derelict, Crates, Hunter, Friend: tech to recover. Waiting: tech of a vault taken apart that waits for the rebuilt vault.
/// </summary>
public enum WatchKind { Design, Storage, Derelict, Crates, Hunter, Friend, Waiting }

/// <summary>A crate in space, for the grouping of spec 2121. Id: a stable order, so the same crate leads its group in every frame.</summary>
public readonly record struct CratePoint(uint Id, float X, float Y, int Units);

public static class TechWatchModel
{
    /// <summary>Spec 2123: kinds that start the banner. The players chose the others (a hold, a vault move in progress).</summary>
    public static bool Alarms(WatchKind kind) => kind is not (WatchKind.Friend or WatchKind.Waiting);

    /// <summary>
    /// Spec 2121: crates in groups. The crate with the lowest ID leads; each other crate joins the first leader within the radius,
    /// else leads a new group. Returns the leaders with the units of their group.
    /// </summary>
    public static List<CratePoint> Groups(IEnumerable<CratePoint> crates, float radius)
    {
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "the group radius is negative");
        var groups = new List<CratePoint>();
        foreach (var c in crates.OrderBy(c => c.Id))
        {
            int i = groups.FindIndex(g => (g.X - c.X) * (g.X - c.X) + (g.Y - c.Y) * (g.Y - c.Y) <= radius * radius);
            if (i < 0) groups.Add(c);
            else groups[i] = groups[i] with { Units = groups[i].Units + c.Units };
        }
        return groups;
    }

    /// <summary>
    /// Spec 2124: tech that the new design of a ship has no vault room for. Only while the design removes a built vault
    /// (fewer vault blueprints than built vaults): then crew take a vault apart and the rest goes to normal storage, where it decays.
    /// A ship that never had room for its tech shows as Storage instead.
    /// </summary>
    public static int DesignShort(int shipTech, int builtVaults, int blueprintVaults, int unitsPerVault)
    {
        if (unitsPerVault <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerVault), unitsPerVault, "a vault holds no tech");
        return blueprintVaults < builtVaults ? Math.Max(0, shipTech - blueprintVaults * unitsPerVault) : 0;
    }
}

/// <summary>
/// Spec 2123: the banner shows once when tech has been at risk for <c>delay</c> seconds without a break.
/// It can show again only after all of that tech was safe again. The delay keeps a short move between vaults quiet.
/// </summary>
public sealed class AlarmWatch
{
    double? _since;
    bool _shown;

    public bool Update(double now, bool atRisk, double delay)
    {
        if (!atRisk)
        {
            _since = null;
            _shown = false;
            return false;
        }
        _since ??= now;
        if (_shown || now - _since.Value < delay) return false;
        _shown = true;
        return true;
    }
}

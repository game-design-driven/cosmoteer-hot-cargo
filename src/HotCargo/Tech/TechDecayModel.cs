namespace HotCargo.Tech;

/// <summary>Spec 1548: what a vault does to the decay of a unit that comes back into it.</summary>
public enum DecayInVault
{
    /// <summary>The decay stops in the vault and goes on when the unit leaves it again. It never recovers.</summary>
    Pause,
    /// <summary>A unit in a vault recovers fully: it has the whole decay time again when it leaves.</summary>
    Reset,
}

/// <summary>
/// One place that holds a stack of tech this tick: a vault tile, a normal storage tile or a crew member that carries tech.
/// Key names the place. Exposed is true outside a vault. From and To are the keys of the tiles that a carrying crew member
/// takes from and brings to (its transfer job), else null. Group (a ship) pairs tiles when tech moves without crew.
/// </summary>
public readonly record struct TechSpot(string Key, bool Exposed, string? From = null, string? To = null, string? Group = null);

/// <summary>Saved decay of the stack at Key: game seconds outside a vault. To and Group as in <see cref="TechSpot"/>.</summary>
public readonly record struct TechWear(string Key, double Wear, string? To = null, string? Group = null);

/// <summary>
/// Spec 1547-1549: tech decays outside a vault. The game stores tech as counts, not as items, so the model follows each stack
/// from place to place. A tile holds up to 4 units as one stack with one wear. A stack keeps its key while it stays.
/// A crew member that picks up tech takes the wear of the tile in its job's source (the tile keeps its wear when units stay).
/// A tile that receives tech takes the wear of the vanished crew member whose job named that tile; on a stack that was there,
/// the higher wear wins, so merging never makes tech recover. A stack that moves on one ship without crew (an instant transfer)
/// takes the highest wear that vanished on that ship. Anything else is new tech with no wear (a crate, an event, a purchase).
/// Pure math, no game types.
/// </summary>
public static class TechDecayModel
{
    /// <summary>
    /// One step of dt game seconds. prev is the saved wear (only stacks with wear above 0), seen the places of this tick in a
    /// stable order. Returns the new saved wear and the keys of exposed stacks whose decay ran out (the caller destroys them).
    /// destroyable says which exposed places can lose their stack now: a carried stack is destroyed when it is set down.
    /// </summary>
    public static (List<TechWear> Next, List<string> Destroy) Step(IReadOnlyList<TechWear> prev, IReadOnlyList<TechSpot> seen,
        double dt, double decaySeconds, DecayInVault inVault, Func<TechSpot, bool> destroyable)
    {
        if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt), "must not be negative");
        if (decaySeconds <= 0) throw new ArgumentOutOfRangeException(nameof(decaySeconds), "must be above 0");
        var seenKeys = new HashSet<string>();
        foreach (var s in seen)
            if (!seenKeys.Add(s.Key)) throw new ArgumentException($"place {s.Key} seen twice", nameof(seen));
        var byKey = prev.ToDictionary(w => w.Key);
        var vanished = prev.Where(w => !seenKeys.Contains(w.Key)).ToList();
        var used = new HashSet<string>();

        double? Take(Func<TechWear, bool> match)
        {
            foreach (var w in vanished)
                if (!used.Contains(w.Key) && match(w)) { used.Add(w.Key); return w.Wear; }
            return null;
        }

        var wear = new double?[seen.Count];
        for (int i = 0; i < seen.Count; i++)
        {
            var s = seen[i];
            // A pickup copies the wear of its source tile, which may still hold units.
            double? picked = s.From is { } from && !byKey.ContainsKey(s.Key) && byKey.TryGetValue(from, out var source)
                ? (seenKeys.Contains(from) ? source.Wear : Take(w => w.Key == from)) : null;
            double? delivered = Take(w => w.To == s.Key);
            double? kept = byKey.TryGetValue(s.Key, out var k) ? k.Wear : null;
            wear[i] = Max(Max(kept, delivered), picked);
        }
        // Instant moves on one ship, after every crew match. Highest wear first, so a move never makes tech recover.
        for (int i = 0; i < seen.Count; i++)
        {
            if (wear[i] is not null || seen[i].Group is not { } group) continue;
            var best = vanished.Where(w => !used.Contains(w.Key) && w.Group == group).OrderByDescending(w => w.Wear).FirstOrDefault();
            if (best.Key is null) continue;
            used.Add(best.Key);
            wear[i] = best.Wear;
        }

        var next = new List<TechWear>();
        var destroy = new List<string>();
        for (int i = 0; i < seen.Count; i++)
        {
            var s = seen[i];
            double w = wear[i] ?? 0;
            if (s.Exposed) w += dt;
            else if (inVault == DecayInVault.Reset) w = 0;
            if (s.Exposed && w >= decaySeconds && destroyable(s)) { destroy.Add(s.Key); continue; }
            if (w > 0) next.Add(new TechWear(s.Key, w, s.To, s.Group));
        }
        return (next, destroy);
    }

    static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    /// <summary>Game seconds until the stack decays, at least 0.</summary>
    public static double SecondsLeft(double wear, double decaySeconds) => Math.Max(0, decaySeconds - wear);
}

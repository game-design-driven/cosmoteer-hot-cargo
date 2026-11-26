namespace HotCargo.Journey;

/// <summary>A system of the run map: its band (0 = start, last = the Buyer) and its map location.</summary>
public readonly record struct MapPoint(int Band, float X, float Y);

/// <summary>A link between two systems, by index; A is lower than B.</summary>
public readonly record struct MapLink(int A, int B);

public sealed record RunMapLayout(IReadOnlyList<MapPoint> Nodes, IReadOnlyList<MapLink> Links);

public enum LinkKind { Side, Forward, OldLane }

/// <summary>
/// Spec 2550 to 2556: the run map. A lens of systems in bands from one start system to one Buyer system. The number of steps to the
/// Buyer (Journey.RunSectorCount) sets every size. The band of a system follows from its X location, so the map needs no save state
/// of its own. No game types; the unit tests cover it.
/// </summary>
public static class RunMapModel
{
    /// <summary>Map distance from one band to the next. Vanilla links are 5 to 10 long (base_galaxy.rules), so fuel costs stay vanilla.</summary>
    public const float BandLength = 8f;

    /// <summary>Vertical gap between systems of one band.</summary>
    public const float RowGap = 7f;

    /// <summary>A system sits at most this share of a band before or after its band center. Under 0.5, so bands never overlap.</summary>
    public const float BandJitter = 0.3f;

    /// <summary>Spec 2551: a system links forward to 2 or 3 systems of the next band, and to a side neighbor with this chance.</summary>
    public const float SideLinkChance = 0.3f;

    public static int LastBand(int steps) => Math.Max(1, steps - 1);

    /// <summary>
    /// Spec 2550: systems in a band. The start and the Buyer bands have 1. The others follow a lens, widest (steps − 1) in the middle,
    /// and at least 2, so every system before the last band can link to 2 systems ahead.
    /// </summary>
    public static int Width(int band, int steps)
    {
        int last = LastBand(steps);
        if (band <= 0 || band >= last) return 1;
        double shape = Math.Pow(Math.Sin(Math.PI * band / last), 0.7);
        return Math.Max(2, 1 + (int)Math.Round((Math.Max(2, steps - 1) - 1) * shape));
    }

    /// <summary>Spec 2552: the low tier of a band, from tier 1 at the start to <paramref name="finalTier"/> at the Buyer.</summary>
    public static int Tier(int band, int steps, int finalTier) =>
        1 + (int)Math.Round((finalTier - 1) * (double)Math.Clamp(band, 0, LastBand(steps)) / LastBand(steps));

    /// <summary>Spec 2551: old lanes in a map: one for each 3 steps.</summary>
    public static int OldLanes(int steps) => steps / 3;

    /// <summary>The map position of a system in bands (band center = whole number).</summary>
    public static float Position(float x) => x / BandLength;

    public static int Band(float x) => (int)MathF.Round(Position(x));

    public static LinkKind Kind(int bandA, int bandB) => Math.Abs(bandA - bandB) switch
    {
        0 => LinkKind.Side,
        1 => LinkKind.Forward,
        _ => LinkKind.OldLane,
    };

    /// <summary>
    /// Spec 2550, 2551: makes the map. <paramref name="next"/> returns a random number in [0, 1); the galaxy random source in the game.
    /// Every system before the last band links forward to at least 2 systems of the next band (the last band links only to the Buyer),
    /// and every system after the start has a link back, so the Buyer is reachable from every system.
    /// </summary>
    public static RunMapLayout Generate(int steps, Func<float> next)
    {
        if (steps < 2) throw new ArgumentOutOfRangeException(nameof(steps), steps, "the run map needs 2 or more steps");
        int last = LastBand(steps);
        var nodes = new List<MapPoint>();
        var bands = new List<int[]>();
        for (int band = 0; band <= last; band++)
        {
            int width = Width(band, steps);
            var ids = new int[width];
            for (int i = 0; i < width; i++)
            {
                bool end = band == 0 || band == last;
                float x = (band + (end ? 0f : (next() * 2f - 1f) * BandJitter)) * BandLength;
                float row = i - (width - 1) / 2f + (end ? 0f : (next() - 0.5f) * 0.6f);
                ids[i] = nodes.Count;
                nodes.Add(new MapPoint(band, x, row * RowGap));
            }
            bands.Add(ids);
        }

        var links = new HashSet<MapLink>();
        void Add(int a, int b) => links.Add(new MapLink(Math.Min(a, b), Math.Max(a, b)));
        int[] Nearest(int from, int[] targets) => targets.OrderBy(t => Math.Abs(nodes[t].Y - nodes[from].Y)).ThenBy(t => t).ToArray();

        for (int band = 0; band < last; band++)
        {
            int[] ahead = bands[band + 1];
            foreach (int id in bands[band])
            {
                int count = Math.Min(ahead.Length, 2 + (next() < 0.35f ? 1 : 0));
                foreach (int t in Nearest(id, ahead).Take(count)) Add(id, t);
            }
            // A system that no system behind links to gets a link from the nearest one.
            foreach (int t in ahead.Where(t => !links.Any(l => (l.A == t || l.B == t) && nodes[l.A + l.B - t].Band == band)))
                Add(Nearest(t, bands[band])[0], t);
            // Side links between row neighbors.
            int[] row = bands[band];
            for (int i = 0; i + 1 < row.Length; i++)
                if (next() < SideLinkChance) Add(row[i], row[i + 1]);
        }

        // Old lanes: a long link from the early middle that skips 2 or 3 bands, never into the Buyer's band.
        var sources = Enumerable.Range(1, Math.Max(0, last - 3)).SelectMany(b => bands[b]).ToList();
        for (int lane = 0; lane < OldLanes(steps) && sources.Count > 0; lane++)
        {
            int from = sources[Math.Min(sources.Count - 1, (int)(next() * sources.Count))];
            int skip = Math.Min(last - 1 - nodes[from].Band, 2 + (next() < 0.5f ? 1 : 0));
            if (skip < 2) continue;
            Add(from, Nearest(from, bands[nodes[from].Band + skip])[0]);
            sources.Remove(from);
        }
        return new RunMapLayout(nodes, links.OrderBy(l => l.A).ThenBy(l => l.B).ToList());
    }

    /// <summary>Indexes of the systems linked to <paramref name="node"/>.</summary>
    public static IEnumerable<int> Neighbors(RunMapLayout map, int node) =>
        map.Links.Where(l => l.A == node || l.B == node).Select(l => l.A + l.B - node);
}

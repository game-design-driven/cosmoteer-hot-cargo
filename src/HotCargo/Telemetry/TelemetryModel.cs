using System.Text.Json;

namespace HotCargo.Telemetry;

/// <summary>Pure telemetry rules with no game types (spec 1580 to 1593): fight joining, wreck lineage, session numbers, privacy, event fields.</summary>
public static class TelemetryModel
{
    /// <summary>Sides whose damage makes a fight: the players and their enemies. Neutral damage alone (rocks, the sun) is no fight.</summary>
    public static readonly string[] FightSides = ["player", "hunter", "hostile"];

    /// <summary>
    /// Spec 1584: the number of a new session of a run. A load of an older save repeats the saved count, so the files of the run that
    /// this machine already wrote count too. Part files (".part") belong to their session.
    /// Limit: the folder prune (MaxFolderMegabytes) removes old files, so a pruned run can repeat a number. Revisit if runs outgrow the folder.
    /// </summary>
    public static int SessionNumber(int savedSessions, IEnumerable<string> fileNames, string runId)
    {
        string prefix = runId + "__";
        int written = fileNames.Count(f => f.StartsWith(prefix, StringComparison.Ordinal) && f.EndsWith(".jsonl", StringComparison.Ordinal)
            && !f.Contains(".part", StringComparison.Ordinal));
        return Math.Max(savedSessions, written) + 1;
    }

    const string SteamIdStart = "7656119";
    const int SteamIdLength = 17;

    /// <summary>
    /// Spec 1585: removes personal data from a text: the user folder name after "\Users\", "/Users/" or "/home/", and Steam IDs
    /// (17 digits that start with 7656119).
    /// Limit: a user folder name with a space is masked only up to the space. Revisit if such names show up.
    /// </summary>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (string marker in (string[])["\\Users\\", "/Users/", "/home/"])
            text = MaskAfter(text, marker);
        int at = 0;
        while ((at = text.IndexOf(SteamIdStart, at, StringComparison.Ordinal)) >= 0)
        {
            int end = at;
            while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
            bool alone = (at == 0 || !char.IsAsciiDigit(text[at - 1])) && end - at == SteamIdLength;
            if (alone) text = string.Concat(text.AsSpan(0, at), "<steam id>", text.AsSpan(end));
            at += alone ? "<steam id>".Length : 1;
        }
        return text;
    }

    static string MaskAfter(string text, string marker)
    {
        int at = 0;
        while ((at = text.IndexOf(marker, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int start = at + marker.Length, end = start;
            while (end < text.Length && text[end] is not ('\\' or '/' or '"' or '\'') && !char.IsWhiteSpace(text[end])) end++;
            if (end > start && text.AsSpan(start, end - start) is not "<user>")
                text = string.Concat(text.AsSpan(0, start), "<user>", text.AsSpan(end));
            at = start;
        }
        return text;
    }

    /// <summary>Spec 1581: how a ship came to the players, from the caller frames of the allegiance change (innermost first).</summary>
    public static string CaptureHow(int oldPlayer, string caller)
    {
        foreach (var (key, how) in CaptureSources)
            if (caller.Contains(key, StringComparison.Ordinal)) return how;
        return oldPlayer == JunkPlayer ? "claim" : "other";
    }

    /// <summary>The allegiance of a wreck (vanilla PlayerIndexes.Junk).</summary>
    public const int JunkPlayer = -3;

    static readonly (string Key, string How)[] CaptureSources =
    [
        ("MPSetCrewHomeShipInput", "claim"), ("Join.", "joined"), ("Autotest", "test"),
        ("MechanicsArea", "test"), ("HunterArea", "test"), ("GameDebugTools", "debug"),
    ];

    // ---- Event fields

    /// <summary>One ship of a jump (spec 1582): drive, hyperium, drive capacity, the cost of this jump and the tech aboard.</summary>
    public sealed record JumpShip(string Id, string Name, int Player, bool Drive, int Fuel, int Capacity, int? Cost, int Tech, int Parts);

    public static void WriteJumpShips(Utf8JsonWriter w, string name, IEnumerable<JumpShip> ships)
    {
        w.WriteStartArray(name);
        foreach (var s in ships)
        {
            w.WriteStartObject();
            w.WriteString("ship", s.Id);
            w.WriteString("name", s.Name);
            w.WriteNumber("player", s.Player);
            w.WriteBoolean("drive", s.Drive);
            w.WriteNumber("fuel", s.Fuel);
            w.WriteNumber("capacity", s.Capacity);
            if (s.Cost is { } c) w.WriteNumber("cost", c); else w.WriteNull("cost");
            w.WriteBoolean("too_big", s.Cost is { } cost && cost > s.Capacity);
            w.WriteNumber("tech", s.Tech);
            w.WriteNumber("parts", s.Parts);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    /// <summary>The side, name and value of a ship before it became a wreck or a player ship (spec 1580, 1581).</summary>
    public sealed record ShipInfo(string Id, string Name, int Player, string Side, string? Faction, bool Hunter, int Value, IReadOnlyList<string> Tags);

    public static void WriteShipInfo(Utf8JsonWriter w, ShipInfo s, string prefix = "")
    {
        w.WriteString(prefix + "ship", s.Id);
        w.WriteString(prefix + "name", s.Name);
        w.WriteNumber(prefix + "player", s.Player);
        w.WriteString(prefix + "side", s.Side);
        if (s.Faction is not null) w.WriteString(prefix + "faction", s.Faction);
        w.WriteBoolean(prefix + "hunter", s.Hunter);
        w.WriteNumber(prefix + "value", s.Value);
        w.WriteStartArray(prefix + "tags");
        foreach (string t in s.Tags) w.WriteStringValue(t);
        w.WriteEndArray();
    }

    /// <summary>Spec 1581: a ship that became a player ship. Old is its last state before it was a wreck or another side's ship.</summary>
    public static void WriteCapture(Utf8JsonWriter w, ShipInfo old, int player, string how, int value, bool drive, int tech)
    {
        w.WriteString("ship", old.Id);
        w.WriteString("name", old.Name);
        w.WriteNumber("player", player);
        w.WriteNumber("old_player", old.Player);
        w.WriteString("old_side", old.Side);
        if (old.Faction is not null) w.WriteString("old_faction", old.Faction);
        w.WriteBoolean("hunter", old.Hunter);
        w.WriteString("how", how);
        w.WriteNumber("value", value);
        w.WriteBoolean("drive", drive);
        w.WriteNumber("tech", tech);
    }

    public static void WriteFight(Utf8JsonWriter w, Fight f)
    {
        w.WriteNumber("start_gs", Math.Round(f.Start, 3));
        w.WriteNumber("end_gs", Math.Round(f.End, 3));
        w.WriteNumber("duration_s", Math.Round(f.End - f.Start, 3));
        w.WriteNumber("fight_sec", f.Sector);
        w.WriteStartArray("sides");
        foreach (string s in f.Damage.Keys.Concat(f.Destroyed.Keys).Distinct().Order(StringComparer.Ordinal)) w.WriteStringValue(s);
        w.WriteEndArray();
        WriteCounts(w, "damage", f.Damage);
        WriteCounts(w, "destroyed", f.Destroyed);
        w.WriteNumber("losses", f.Destroyed.GetValueOrDefault("player"));
        w.WriteNumber("kills", f.Destroyed.Where(kv => kv.Key is "hunter" or "hostile").Sum(kv => kv.Value));
        w.WriteNumber("crew_lost", f.CrewLost);
        w.WriteNumber("windows", f.Windows);
    }

    static void WriteCounts<T>(Utf8JsonWriter w, string name, Dictionary<string, T> counts) where T : struct, IConvertible
    {
        w.WriteStartObject(name);
        foreach (var (k, v) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal)) w.WriteNumber(k, Convert.ToInt64(v));
        w.WriteEndObject();
    }
}

/// <summary>One fight: damage windows and losses that follow each other with no quiet gap of FightGapSeconds (spec 1588).</summary>
public sealed class Fight(double start, int sector)
{
    public double Start { get; } = start;
    public double End { get; set; } = start;
    public int Sector { get; } = sector;
    public Dictionary<string, long> Damage { get; } = [];
    public Dictionary<string, int> Destroyed { get; } = [];
    public int CrewLost { get; set; }
    public int Windows { get; set; }
    public long CombatDamage => TelemetryModel.FightSides.Sum(s => Damage.GetValueOrDefault(s));
}

/// <summary>
/// Spec 1588: joins damage windows into fights. A window or a destroyed ship of a fight side starts a fight or extends the current one.
/// A fight ends after gapSeconds of game time without either, at a system change and at the session end. A fight without damage
/// to a fight side (a lone wreck) is dropped.
/// </summary>
public sealed class FightJoiner(double gapSeconds)
{
    public Fight? Current { get; private set; }

    /// <summary>A damage window that ends at game second at. Returns the fight that it ended, if any.</summary>
    public Fight? AddWindow(double at, double windowSeconds, int sector, IReadOnlyDictionary<string, long> damage)
    {
        long combat = TelemetryModel.FightSides.Sum(s => damage.GetValueOrDefault(s));
        var ended = Tick(at, sector);
        if (combat <= 0) return ended;
        var f = Current ??= new Fight(Math.Max(0, at - windowSeconds), sector);
        f.End = Math.Max(f.End, at);
        f.Windows++;
        foreach (var (side, d) in damage) f.Damage[side] = f.Damage.GetValueOrDefault(side) + d;
        return ended;
    }

    /// <summary>A ship of a side became a wreck for good. Returns the fight that it ended, if any.</summary>
    public Fight? AddDestroyed(double at, int sector, string side)
    {
        var ended = Tick(at, sector);
        if (!TelemetryModel.FightSides.Contains(side) && Current is null) return ended;
        var f = Current ??= new Fight(at, sector);
        f.End = Math.Max(f.End, at);
        f.Destroyed[side] = f.Destroyed.GetValueOrDefault(side) + 1;
        return ended;
    }

    public void AddCrewDeath() { if (Current is { } f) f.CrewLost++; }

    /// <summary>Ends the current fight after the quiet gap or in another sector.</summary>
    public Fight? Tick(double now, int sector) =>
        Current is { } f && (now - f.End > gapSeconds || sector != f.Sector) ? Close() : null;

    /// <summary>Ends the current fight now. Null when there is none, or when it had no damage to a fight side.</summary>
    public Fight? Close()
    {
        var f = Current;
        Current = null;
        return f is { CombatDamage: > 0 } ? f : null;
    }
}

/// <summary>
/// Spec 1580: the pieces of a ship that broke apart. Vanilla turns each piece into its own ship (SimModeManager.OnShipSplit) and junks
/// each one, so one destroyed ship made up to 10 wreck events. A lineage holds the root ship and its live pieces. The ship counts as
/// destroyed once, when its last live piece becomes a wreck, with the side and value of the root.
/// </summary>
public sealed class ShipLineage<TId, TInfo> where TId : notnull where TInfo : class
{
    readonly Dictionary<TId, TId> _rootOf = [];
    readonly Dictionary<TId, (TInfo Info, int Live)> _roots = [];

    /// <summary>A live ship split. Pieces join the lineage of the old ship; a first split starts a lineage with the old ship's info.</summary>
    public void Split(TId old, IEnumerable<TId> pieces, Func<TInfo> infoOfOld)
    {
        if (!_rootOf.TryGetValue(old, out var root))
        {
            root = old;
            _rootOf[old] = old;
            _roots[old] = (infoOfOld(), 1);
        }
        var r = _roots[root];
        foreach (var p in pieces)
        {
            if (!_rootOf.TryAdd(p, root)) continue;
            r.Live++;
        }
        _roots[root] = r;
    }

    /// <summary>The info of the lineage root of a ship, or null for a ship that never split.</summary>
    public TInfo? InfoOf(TId id) => _rootOf.TryGetValue(id, out var root) && _roots.TryGetValue(root, out var r) ? r.Info : null;

    /// <summary>
    /// A ship became a wreck. Ends: true when this was the last live piece of its lineage, or a ship that never split.
    /// Root: the info of the lineage root, or null for a ship that never split.
    /// </summary>
    public (bool Ends, TInfo? Root) Junked(TId id)
    {
        if (!_rootOf.TryGetValue(id, out var root) || !_roots.TryGetValue(root, out var r)) return (true, null);
        _rootOf.Remove(id);
        r.Live--;
        if (r.Live > 0) { _roots[root] = r; return (false, r.Info); }
        _roots.Remove(root);
        foreach (var k in _rootOf.Where(kv => EqualityComparer<TId>.Default.Equals(kv.Value, root)).Select(kv => kv.Key).ToList()) _rootOf.Remove(k);
        return (true, r.Info);
    }

    public void Clear() { _rootOf.Clear(); _roots.Clear(); }
}

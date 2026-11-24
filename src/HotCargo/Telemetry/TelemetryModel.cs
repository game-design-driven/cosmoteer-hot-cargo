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

/// <summary>
/// Spec 1592, 1593: the rules of the co-op relay. Each client sends its telemetry files to the host in chunks. The host keeps one copy of
/// each file under the player's folder and confirms the length it holds. A file is an append-only byte stream, so the length is the
/// whole state: a lost, repeated or late chunk changes nothing.
/// </summary>
public static class RelayModel
{
    /// <summary>
    /// The part of a chunk at offset that the host appends to a copy of length have: (skip, write). A chunk past the end (a gap) writes
    /// nothing; the host's answer, its length, makes the client send from there.
    /// </summary>
    public static (int Skip, int Write) Accept(long have, long offset, int length)
    {
        if (length <= 0 || offset > have) return (0, 0);
        long skip = Math.Min(have - offset, length);
        return ((int)skip, length - (int)skip);
    }

    /// <summary>The next chunk of a file for the client to send: a probe (length 0) while the host's length is unknown, null when the host holds it all.</summary>
    public static (long Offset, int Length)? Next(long? confirmed, long size, int chunkBytes)
    {
        if (confirmed is not { } have) return (0, 0);
        if (have >= size) return null;
        return (have, (int)Math.Min(chunkBytes, size - have));
    }

    /// <summary>
    /// The kind of a file name that the host accepts, or null: a plain name without a folder part, a telemetry file (.jsonl) or a
    /// note screenshot for the host (.jpg, spec 1995).
    /// </summary>
    public static RelayFile? Kind(string? name)
    {
        if (name is not { Length: > 4 and <= 200 } || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Contains("..", StringComparison.Ordinal) || name.Trim() != name)
            return null;
        if (name.EndsWith(".jsonl", StringComparison.Ordinal)) return RelayFile.Session;
        if (name.EndsWith(".jpg", StringComparison.Ordinal)) return RelayFile.NoteShot;
        return null;
    }

    /// <summary>Spec 1995: the host refuses note screenshot bytes past this size. A 1280 px JPEG is far below it.</summary>
    public const long MaxNoteShotBytes = 8 * 1024 * 1024;

    /// <summary>Spec 1995: the name of a client's note screenshot in the host's telemetry/notes/ folder.</summary>
    public static string HostShotName(string? playerName, string file) => $"{FolderName(playerName)}__{file}";

    /// <summary>Spec 1995: the size of the screenshot for the host: at most maxWidth wide, same aspect ratio, never larger than the source.</summary>
    public static (int Width, int Height) ShotSize(int width, int height, int maxWidth) =>
        width <= maxWidth ? (width, height) : (maxWidth, Math.Max(1, (int)Math.Round((double)height * maxWidth / width)));

    /// <summary>
    /// Spec 1996: a client's note line as the host writes it to its notes.jsonl. With a screenshot for the host, "screenshot" names
    /// the host's copy and "screenshot_client" the client's own file. Null for a line that is no JSON object.
    /// </summary>
    public static string? HostNoteLine(string line, string? playerName)
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(line) is not System.Text.Json.Nodes.JsonObject o) return null;
        if (o["screenshot_small"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? small) && Kind(small) == RelayFile.NoteShot)
        {
            o["screenshot_client"] = o["screenshot"]?.DeepClone();
            o["screenshot"] = HostShotName(playerName, small);
        }
        o.Remove("screenshot_small");
        return o.ToJsonString();
    }

    /// <summary>The folder of a player on the host: the player's name with letters, digits, '-' and '_' only, at most 40 characters.</summary>
    public static string FolderName(string? playerName)
    {
        var s = new string([.. (playerName ?? "").Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')]).Trim('_');
        return s.Length == 0 ? "player" : s[..Math.Min(s.Length, 40)];
    }
}

/// <summary>What a relayed file is: a telemetry file (host: players/&lt;name&gt;/) or a note screenshot (host: notes/&lt;name&gt;__&lt;file&gt;).</summary>
public enum RelayFile { Session, NoteShot }

/// <summary>Spec 1760: what the players' fleet does, in the order of the priority rule. The first activity whose signal is on wins.</summary>
public enum Activity { Fight, Build, Trade, Travel, Repair, Idle }

/// <summary>Spec 1761: the signals of one sample. Idle has no signal: it is the time when none is on.</summary>
public readonly record struct ActivitySignals(bool Fight, bool Build, bool Trade, bool Travel, bool Repair);

/// <summary>
/// Spec 1760, 1762: the game seconds of a system stay by activity. Each second goes to exactly one activity (the priority rule),
/// so the sums add up to the stay. Next to it, the seconds that each signal was on at all, also while a higher activity won.
/// </summary>
public sealed class TimeUse
{
    public static readonly Activity[] All = Enum.GetValues<Activity>();
    readonly double[] _main = new double[All.Length], _any = new double[All.Length];

    public static Activity Pick(ActivitySignals s) =>
        s.Fight ? Activity.Fight : s.Build ? Activity.Build : s.Trade ? Activity.Trade : s.Travel ? Activity.Travel : s.Repair ? Activity.Repair : Activity.Idle;

    public void Add(ActivitySignals s, double seconds)
    {
        if (seconds <= 0) return;
        _main[(int)Pick(s)] += seconds;
        if (s.Fight) _any[(int)Activity.Fight] += seconds;
        if (s.Build) _any[(int)Activity.Build] += seconds;
        if (s.Trade) _any[(int)Activity.Trade] += seconds;
        if (s.Travel) _any[(int)Activity.Travel] += seconds;
        if (s.Repair) _any[(int)Activity.Repair] += seconds;
    }

    public double this[Activity a] => _main[(int)a];
    public double Any(Activity a) => _any[(int)a];
    public double Total => _main.Sum();

    public void Reset() { Array.Clear(_main); Array.Clear(_any); }

    public TimeUse Copy()
    {
        var c = new TimeUse();
        _main.CopyTo(c._main, 0);
        _any.CopyTo(c._any, 0);
        return c;
    }

    public static string Name(Activity a) => a.ToString().ToLowerInvariant();

    /// <summary>Writes "&lt;prefix&gt;time_use" (seconds by activity, they add up to the stay) and "&lt;prefix&gt;time_any" (seconds each signal was on).</summary>
    public void Write(System.Text.Json.Utf8JsonWriter w, string prefix = "")
    {
        w.WriteStartObject(prefix + "time_use");
        foreach (var a in All) w.WriteNumber(Name(a), Math.Round(this[a], 3));
        w.WriteEndObject();
        w.WriteStartObject(prefix + "time_any");
        foreach (var a in All.Where(a => a != Activity.Idle)) w.WriteNumber(Name(a), Math.Round(Any(a), 3));
        w.WriteEndObject();
    }
}

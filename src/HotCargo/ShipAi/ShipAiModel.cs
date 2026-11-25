using System.Globalization;
using System.Text;

namespace HotCargo.ShipAi;

/// <summary>Spec 410: the AI button of the ships card for the selected ships: Auto on in none, some or all of them.</summary>
public enum CardState { Off, Mixed, On }

public enum Preset { Aggressive, Defensive, Sniper, Escort, VaultGuard, Looter, Miner, SupplyRunner, Salvager, EscortDrone, Formation }

/// <summary>Spec 2323: the comparison of a term.</summary>
public enum Op { Less, Greater, Equal }

/// <summary>Spec 2322, 2325: a value with its arguments. Slot: a design slot (1 to MaxDesigns) or 0; Resource: a resource ID or "".</summary>
public readonly record struct Operand(Value Value, int Slot = 0, string Resource = "");

/// <summary>Spec 416: what a term tests: a fact or a comparison.</summary>
public abstract record TermBody;

/// <summary>Spec 2324: a fact with its parameter (what ShipAiRegistry.Of(Cond).Param says, 0 without one).</summary>
public sealed record FactTerm(Cond Cond, int Param = 0) : TermBody;

/// <summary>Spec 2323: Left Op (Right, or Number when Right is null). Right has the unit of Left.</summary>
public sealed record CompareTerm(Operand Left, Op Op, Operand? Right, int Number) : TermBody;

/// <summary>
/// One condition term of a row (spec 416). Not inverts it. Or joins it (with the brackets that it opens) to the item before it
/// with OR, else with AND; Or of the first item in a bracket means nothing. Open: brackets that open before the term; Close:
/// brackets that close after it (spec 2320). The brackets of a row are balanced (ShipAiModel.BracketError).
/// </summary>
public readonly record struct Term(TermBody Body, bool Not = false, bool Or = false, int Open = 0, int Close = 0)
{
    public static Term Fact(Cond cond, int param = 0) => new(new FactTerm(cond, param));

    public static Term Compare(Value value, Op op, int number) => new(new CompareTerm(new Operand(value), op, null, number));

    public static Term Less(Value value, int number) => Compare(value, Op.Less, number);

    public static Term Greater(Value value, int number) => Compare(value, Op.Greater, number);
}

/// <summary>One "conditions → action" row. ActParam means what ShipAiRegistry.Of(Act).Param says. Terms is never empty; nobody changes it.</summary>
public sealed record TacticRow(Term[] Terms, Act Act, int ActParam = 0)
{
    public TacticRow(Term term, Act act, int actParam = 0) : this([term], act, actParam) { }

    public bool Equals(TacticRow? other) =>
        other is not null && Act == other.Act && ActParam == other.ActParam && Terms.AsSpan().SequenceEqual(other.Terms);

    public override int GetHashCode() => HashCode.Combine(Act, ActParam, Terms.Length, Terms.Length > 0 ? Terms[0] : default);
}

/// <summary>
/// What terms read, from the sim each AI tick (spec 2324, 2325). Value: in the unit of the value (percent 0 to 100, distance edge to
/// edge, +infinity when there is nothing to measure to), or null when the ship has no such value (no shields, no Strike wave, an
/// empty slot). A row reads only what its terms name.
/// </summary>
public interface IShipFacts
{
    bool Fact(Cond cond, int param);

    double? Value(Operand operand);
}

/// <summary>The last value of one term and the game second it took that value (spec 418). Rows keep one per term, in term order.</summary>
public readonly record struct TermMemory(bool On, double Since);

/// <summary>
/// Spec 418. Margin: a threshold comparison that holds keeps holding until its value passes the other side by this much
/// (percent: points × 100, distance and seconds: a fraction of the other side). MinHoldSeconds: it keeps a new result at least this long.
/// </summary>
public readonly record struct Hysteresis(float Margin, double MinHoldSeconds)
{
    public static readonly Hysteresis None = new(0f, 0);
}

/// <summary>
/// Result of one evaluation (spec 403, 1067). TargetRow, MoveRow, FacingRow, TradeRow and BuildRow are row indexes or -1.
/// Matched holds, for each row, whether its conditions held and its action could run. Weapon rows resolve per weapon (WeaponAct);
/// every matched flag row runs (spec 2326). Memory is the new term memory of all rows, in term order.
/// </summary>
public sealed record Decision(int TargetRow, int MoveRow, int FacingRow, bool[] Matched, TermMemory[] Memory, int TradeRow = -1, int BuildRow = -1)
{
    public static readonly Decision None = new(-1, -1, -1, [], []);
}

/// <summary>
/// Player ship tactics, Dragon Age style (spec 400 to 439, 2320 to 2329). Pure: no game types, so the unit tests cover it.
/// </summary>
public static class ShipAiModel
{
    public const int MaxRows = 16;
    public const int MaxGroup = 10;
    public const int MaxFlags = 4;
    /// <summary>
    /// The highest stage that stored tactics text may name. The real limit is the number of heat stages (Heat.Stages), which the
    /// tactics input checks (StageError).
    /// </summary>
    public const int MaxStage = 10;
    public const string Prefix = "HC2:";
    public const string Arrow = "->";

    public static ActKind KindOf(Act act) => ShipAiRegistry.Of(act).Kind;

    public static ParamKind ParamOf(Cond cond) => ShipAiRegistry.Of(cond).Param;

    public static ParamKind ParamOf(Act act) => ShipAiRegistry.Of(act).Param;

    public static Unit UnitOf(Value value) => ShipAiRegistry.Of(value).Unit;

    /// <summary>Spec 418: comparisons of percent, distance and seconds values get hysteresis; facts and '=' do not.</summary>
    public static bool IsThreshold(TermBody body) =>
        body is CompareTerm { Op: not Op.Equal } c && ShipAiRegistry.IsThreshold(UnitOf(c.Left.Value));

    /// <summary>Spec 413: an error for a heat stage comparison above the number of heat stages, or null when all fit.</summary>
    public static string? StageError(IEnumerable<TacticRow> rows, int stages)
    {
        foreach (var row in rows)
            foreach (var t in row.Terms)
                if (t.Body is CompareTerm { Left.Value: Value.HeatStage, Right: null } c && c.Number > stages)
                    return $"HeatStage {c.Number}: the run has {stages} heat stages";
        return null;
    }

    /// <summary>The design slots that a row names, in its terms and its action.</summary>
    public static IEnumerable<int> DesignSlots(TacticRow row)
    {
        foreach (var t in row.Terms)
        {
            if (t.Body is FactTerm f && ParamOf(f.Cond) == ParamKind.Design) yield return f.Param;
            foreach (var o in Operands(t.Body))
                if (o.Slot > 0) yield return o.Slot;
        }
        if (ParamOf(row.Act) == ParamKind.Design) yield return row.ActParam;
    }

    /// <summary>The values that a term reads: the left side, and the right side when it is a value.</summary>
    public static IEnumerable<Operand> Operands(TermBody body)
    {
        if (body is not CompareTerm c) yield break;
        yield return c.Left;
        if (c.Right is { } r) yield return r;
    }

    /// <summary>Spec 2322: an error for a row that names an empty design slot, or null when every named slot holds a design.</summary>
    public static string? DesignError(IEnumerable<TacticRow> rows, Func<int, bool> filled)
    {
        foreach (var row in rows)
            foreach (int slot in DesignSlots(row))
                if (!filled(slot)) return $"design slot {slot} is empty: pick a design for it first";
        return null;
    }

    /// <summary>Spec 2322: an error for a resource argument that isResource refuses, or null.</summary>
    public static string? ResourceError(IEnumerable<TacticRow> rows, Func<string, bool> isResource)
    {
        foreach (var row in rows)
            foreach (var t in row.Terms)
                foreach (var o in Operands(t.Body))
                    if (o.Resource.Length > 0 && !isResource(o.Resource)) return $"unknown resource '{o.Resource}'";
        return null;
    }

    /// <summary>Whether a term holds, without Not and without hysteresis.</summary>
    public static bool Holds(Term term, IShipFacts f) => Holds(term.Body, f, wasOn: false, margin: 0f);

    /// <summary>
    /// Spec 402: the engage range of a target row: the smallest number of its "nearest enemy &lt; R" terms, when every term must hold
    /// (no OR) and that term has no NOT. The pick takes only enemies within R × (1 + margin), the margin of requirement 418, so a target
    /// that the term keeps also stays pickable. Null: the row picks among every enemy on radar.
    /// </summary>
    public static float? EngageRange(TacticRow row, float margin)
    {
        if (row.Terms.Any(t => t.Or)) return null;
        var within = row.Terms.Where(t => !t.Not && t.Body is CompareTerm { Left.Value: Value.NearestEnemy, Op: Op.Less, Right: null })
            .Select(t => ((CompareTerm)t.Body).Number).ToList();
        return within.Count == 0 ? null : within.Min() * (1f + margin);
    }

    /// <summary>A term's body, without Not. A threshold comparison that was on gets the margin in its favor (spec 418).</summary>
    static bool Holds(TermBody body, IShipFacts f, bool wasOn, float margin) => body switch
    {
        FactTerm fact => f.Fact(fact.Cond, fact.Param),
        CompareTerm c => Compares(c, f, wasOn ? margin : 0f),
        _ => throw new ArgumentOutOfRangeException(nameof(body), body, null),
    };

    /// <summary>Spec 2323, 418: a missing value on either side makes the comparison false.</summary>
    static bool Compares(CompareTerm c, IShipFacts f, float margin)
    {
        if (f.Value(c.Left) is not { } left) return false;
        double? right = c.Right is { } r ? f.Value(r) : c.Number;
        if (right is not { } rv) return false;
        double m = c.Op == Op.Equal || !ShipAiRegistry.IsThreshold(UnitOf(c.Left.Value)) ? 0
            : UnitOf(c.Left.Value) == Unit.Percent ? margin * 100.0 : margin * Math.Abs(rv);
        return c.Op switch
        {
            Op.Less => left < rv + m,
            Op.Greater => left > rv - m,
            Op.Equal => Math.Round(left) == Math.Round(rv),
            _ => throw new ArgumentOutOfRangeException(nameof(c), c.Op, null),
        };
    }

    /// <summary>
    /// Spec 416, 2320: the terms of a row joined by AND and OR, AND first: "A AND B OR C AND D" = (A and B) or (C and D).
    /// A bracket is evaluated the same way and counts as one item. Every term is evaluated, so each one updates its memory.
    /// memory: the terms' old memory (or null), start: its first index. The new memory goes to output from start on.
    /// </summary>
    static bool RowHolds(TacticRow row, IShipFacts f, IReadOnlyList<TermMemory>? memory, int start, double now, Hysteresis h, TermMemory[] output)
    {
        // One (any, group) pair per open bracket level: any = an OR part before held, group = the AND part so far holds.
        var levels = new Stack<(bool Any, bool Group)>();
        bool any = false, group = true;
        for (int j = 0; j < row.Terms.Length; j++)
        {
            var term = row.Terms[j];
            TermMemory? old = memory is not null && start + j < memory.Count ? memory[start + j] : null;
            bool threshold = IsThreshold(term.Body);
            bool raw = Holds(term.Body, f, old?.On == true && threshold, h.Margin);
            TermMemory next;
            if (old is not { } o) next = new TermMemory(raw, now);
            else if (raw == o.On) next = o;
            // Spec 418: a threshold term keeps a new value at least MinHoldSeconds.
            else if (threshold && now - o.Since < h.MinHoldSeconds) next = o;
            else next = new TermMemory(raw, now);
            output[start + j] = next;
            if (j > 0 && term.Or)
            {
                any |= group;
                group = true;
            }
            for (int k = 0; k < term.Open; k++)
            {
                levels.Push((any, group));
                (any, group) = (false, true);
            }
            group &= next.On != term.Not;
            for (int k = 0; k < term.Close; k++)
            {
                bool inner = any | group;
                (any, group) = levels.Pop();
                group &= inner;
            }
        }
        if (levels.Count != 0) throw new InvalidOperationException($"row has {levels.Count} unclosed brackets");
        return any | group;
    }

    /// <summary>Spec 2320: why the brackets of the terms do not balance, or null when they do.</summary>
    public static string? BracketError(IReadOnlyList<Term> terms)
    {
        int depth = 0;
        for (int j = 0; j < terms.Count; j++)
        {
            var t = terms[j];
            if (t.Open < 0 || t.Close < 0) return $"term {j + 1} has a negative bracket count";
            depth += t.Open - t.Close;
            if (depth < 0) return $"term {j + 1} closes a bracket that is not open";
        }
        return depth == 0 ? null : $"{depth} brackets are not closed";
    }

    /// <summary>Spec 2320: the bracket depth of each term, counting the brackets that it opens. Top level is 0.</summary>
    public static int[] Depths(IReadOnlyList<Term> terms)
    {
        var depths = new int[terms.Count];
        int depth = 0;
        for (int j = 0; j < terms.Count; j++)
        {
            depth += terms[j].Open;
            depths[j] = depth;
            depth -= terms[j].Close;
        }
        return depths;
    }

    /// <summary>Evaluation without term memory and without hysteresis.</summary>
    public static Decision Evaluate(IReadOnlyList<TacticRow> rows, IShipFacts facts, Func<TacticRow, bool> canRun) =>
        Evaluate(rows, facts, canRun, null, 0, Hysteresis.None);

    /// <summary>
    /// Spec 403. Rows run top to bottom. A row matches when its conditions hold and canRun says its action can run now
    /// (for example, a target action needs a target). The first matching row of each kind wins: one target row, one move row,
    /// one facing row, one trade row, one build row, and per weapon the first matching weapons row of its group (WeaponAct). canRun is called only
    /// for rows whose conditions hold. memory is the Memory of the last decision for the same rows, or null; a memory of another
    /// length is ignored.
    /// </summary>
    public static Decision Evaluate(IReadOnlyList<TacticRow> rows, IShipFacts facts, Func<TacticRow, bool> canRun,
        IReadOnlyList<TermMemory>? memory, double now, Hysteresis h)
    {
        int termCount = rows.Sum(r => r.Terms.Length);
        if (memory is not null && memory.Count != termCount) memory = null;
        var output = new TermMemory[termCount];
        var matched = new bool[rows.Count];
        int target = -1, move = -1, facing = -1, trade = -1, build = -1, start = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            matched[i] = RowHolds(row, facts, memory, start, now, h, output) && canRun(row);
            start += row.Terms.Length;
            if (!matched[i]) continue;
            switch (KindOf(row.Act))
            {
                case ActKind.Target when target < 0: target = i; break;
                case ActKind.Move when move < 0: move = i; break;
                case ActKind.Facing when facing < 0: facing = i; break;
                case ActKind.Trade when trade < 0: trade = i; break;
                case ActKind.Build when build < 0: build = i; break;
            }
        }
        return new Decision(target, move, facing, matched, output, trade, build);
    }

    /// <summary>Spec 2326: the flags after the matched flag rows ran, in row order. flags: bit n-1 = flag n.</summary>
    public static int ApplyFlags(IReadOnlyList<TacticRow> rows, IReadOnlyList<bool> matched, int flags)
    {
        for (int i = 0; i < rows.Count && i < matched.Count; i++)
        {
            if (!matched[i]) continue;
            int bit = 1 << (rows[i].ActParam - 1);
            flags = rows[i].Act switch
            {
                Act.SetFlag => flags | bit,
                Act.ClearFlag => flags & ~bit,
                _ => flags,
            };
        }
        return flags;
    }

    /// <summary>
    /// Spec 404, 414. The weapons action for one weapon: the first matched weapons row whose group is 0 (all weapons)
    /// or one of the weapon's part control groups. FireDisruptors applies only to disruptor weapons; other weapons skip it.
    /// FireAtWill when none matches.
    /// </summary>
    public static Act WeaponAct(IReadOnlyList<TacticRow> rows, IReadOnlyList<bool> matched, IReadOnlySet<int> groups, bool disruptor = false)
    {
        for (int i = 0; i < rows.Count && i < matched.Count; i++)
        {
            var row = rows[i];
            if (!matched[i] || KindOf(row.Act) != ActKind.Weapons || (row.Act == Act.FireDisruptors && !disruptor)) continue;
            if (row.ActParam == 0 || groups.Contains(row.ActParam)) return row.Act;
        }
        return Act.FireAtWill;
    }

    /// <summary>Weapons actions that give weapons an explicit part target on the target ship.</summary>
    public static bool AimsAtParts(Act act) => act is Act.FocusTarget or Act.TargetWeapons or Act.TargetEngines or Act.FireDisruptors;

    // ---- Text form (spec 405, 417, 2320)

    /// <summary>"HC2:" then rows joined by ';', each "terms->Action[=parameter]". Empty text = no tactics.</summary>
    public static string Encode(IReadOnlyList<TacticRow> rows)
    {
        if (rows.Count == 0) return "";
        var sb = new StringBuilder(Prefix);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i > 0) sb.Append(';');
            for (int j = 0; j < r.Terms.Length; j++)
            {
                var t = r.Terms[j];
                if (j > 0) sb.Append(t.Or ? '|' : '&');
                sb.Append('(', t.Open);
                if (t.Not) sb.Append('!');
                sb.Append(TermText(t.Body));
                sb.Append(')', t.Close);
            }
            sb.Append(Arrow).Append(r.Act).Append(ParamText(ParamOf(r.Act), r.ActParam));
        }
        return sb.ToString();
    }

    /// <summary>A term body as the text form writes it, also for the editor's tooltips.</summary>
    public static string TermText(TermBody body) => body switch
    {
        FactTerm f => f.Cond + ParamText(ParamOf(f.Cond), f.Param),
        CompareTerm c => OperandText(c.Left) + OpChar(c.Op) + (c.Right is { } r ? OperandText(r) : c.Number.ToString(CultureInfo.InvariantCulture)),
        _ => throw new ArgumentOutOfRangeException(nameof(body), body, null),
    };

    public static char OpChar(Op op) => op switch { Op.Less => '<', Op.Greater => '>', Op.Equal => '=', _ => throw new ArgumentOutOfRangeException(nameof(op), op, null) };

    public static string OperandText(Operand o) => ShipAiRegistry.Of(o.Value).Arg switch
    {
        ArgKind.None => o.Value.ToString(),
        ArgKind.Resource => $"{o.Value}[{o.Resource}]",
        ArgKind.Design => $"{o.Value}[{o.Slot.ToString(CultureInfo.InvariantCulture)}]",
        ArgKind.DesignResource => $"{o.Value}[{o.Slot.ToString(CultureInfo.InvariantCulture)},{o.Resource}]",
        _ => throw new ArgumentOutOfRangeException(nameof(o), o.Value, null),
    };

    /// <summary>"=name" for an enum parameter, "=n" for a number parameter, "" without one.</summary>
    static string ParamText(ParamKind kind, int value) => kind switch
    {
        ParamKind.None => "",
        _ when ShipAiRegistry.EnumOf(kind) is { } type => "=" + Enum.GetName(type, value),
        _ => "=" + value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Parses Encode text. Throws FormatException on any error (spec 405); an old "HC1:" text is an error.</summary>
    public static List<TacticRow> Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return [];
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException($"tactics text must start with {Prefix}");
        var rows = new List<TacticRow>();
        foreach (string part in text[Prefix.Length..].Split(';'))
        {
            int arrow = part.LastIndexOf(Arrow, StringComparison.Ordinal);
            if (arrow < 0) throw new FormatException($"tactic row '{part}' has no '{Arrow}'");
            string actText = part[(arrow + Arrow.Length)..];
            int eq = actText.IndexOf('=');
            var act = ParseName<Act>(eq < 0 ? actText : actText[..eq]);
            int actParam = ParseParam(ParamOf(act), act.ToString(), eq < 0 ? null : actText[(eq + 1)..]);
            rows.Add(new TacticRow(ParseTerms(part[..arrow]), act, actParam));
        }
        if (rows.Count > MaxRows) throw new FormatException($"tactics have {rows.Count} rows; at most {MaxRows}");
        return rows;
    }

    static Term[] ParseTerms(string side)
    {
        var terms = new List<Term>();
        int pos = 0;
        bool or = false;
        while (true)
        {
            int end = side.IndexOfAny(['&', '|'], pos);
            string s = end < 0 ? side[pos..] : side[pos..end];
            string inner = s.TrimStart('(');
            int open = s.Length - inner.Length;
            string body = inner.TrimEnd(')');
            int close = inner.Length - body.Length;
            bool not = body.StartsWith('!');
            terms.Add(new Term(ParseBody(not ? body[1..] : body), not, or, open, close));
            if (end < 0) break;
            or = side[end] == '|';
            pos = end + 1;
        }
        if (BracketError(terms) is { } error) throw new FormatException($"conditions '{side}': {error}");
        return terms.ToArray();
    }

    /// <summary>A name followed by '[' or an operator: a value with a comparison when the name is a value, else a fact.</summary>
    static TermBody ParseBody(string s)
    {
        int nameEnd = 0;
        while (nameEnd < s.Length && char.IsLetterOrDigit(s[nameEnd])) nameEnd++;
        string name = s[..nameEnd];
        if (IsName<Value>(name))
        {
            var (left, rest) = ParseOperand(s);
            if (rest.Length < 2) throw new FormatException($"'{s}' needs <, > or = and a number or a value");
            var op = rest[0] switch { '<' => Op.Less, '>' => Op.Greater, '=' => Op.Equal, _ => throw new FormatException($"'{s}': unknown comparison '{rest[0]}'") };
            string rhs = rest[1..];
            var unit = UnitOf(left.Value);
            if (char.IsDigit(rhs[0]))
            {
                if (!int.TryParse(rhs, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) throw new FormatException($"'{s}': '{rhs}' is not a whole number");
                var (min, max) = ShipAiRegistry.RangeOf(unit);
                if (n < min || n > max) throw new FormatException($"'{s}': {n} is outside {min} to {max}");
                return new CompareTerm(left, op, null, n);
            }
            var (right, tail) = ParseOperand(rhs);
            if (tail.Length > 0) throw new FormatException($"'{s}': unexpected '{tail}'");
            if (UnitOf(right.Value) != unit) throw new FormatException($"'{s}': {left.Value} and {right.Value} have different units");
            return new CompareTerm(left, op, right, 0);
        }
        var cond = ParseName<Cond>(name);
        string rest2 = s[nameEnd..];
        if (rest2.Length > 0 && rest2[0] != '=') throw new FormatException($"'{s}': {name} is a fact; it takes '=parameter' only");
        return new FactTerm(cond, ParseParam(ParamOf(cond), name, rest2.Length == 0 ? null : rest2[1..]));
    }

    /// <summary>A value name and its arguments in square brackets; returns the rest of the text.</summary>
    static (Operand Operand, string Tail) ParseOperand(string s)
    {
        int nameEnd = 0;
        while (nameEnd < s.Length && char.IsLetterOrDigit(s[nameEnd])) nameEnd++;
        var value = ParseName<Value>(s[..nameEnd]);
        var kind = ShipAiRegistry.Of(value).Arg;
        string[] args = [];
        string rest = s[nameEnd..];
        if (rest.StartsWith('['))
        {
            int close = rest.IndexOf(']');
            if (close < 0) throw new FormatException($"'{s}': '[' without ']'");
            args = rest[1..close].Split(',');
            rest = rest[(close + 1)..];
        }
        int Slot(string a) => int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= Autonomy.BuildModel.MaxDesigns
            ? n : throw new FormatException($"'{s}': design slot '{a}' is not 1 to {Autonomy.BuildModel.MaxDesigns}");
        string Res(string a) => a.Length > 0 && a.All(c => char.IsLetterOrDigit(c) || c is '_' or '.') ? a : throw new FormatException($"'{s}': '{a}' is not a resource ID");
        var operand = (kind, args.Length) switch
        {
            (ArgKind.None, 0) => new Operand(value),
            (ArgKind.Resource, 1) => new Operand(value, Resource: Res(args[0])),
            (ArgKind.Design, 1) => new Operand(value, Slot(args[0])),
            (ArgKind.DesignResource, 2) => new Operand(value, Slot(args[0]), Res(args[1])),
            _ => throw new FormatException($"'{s}': {value} takes {kind switch { ArgKind.None => "no arguments", ArgKind.Resource => "[resource]", ArgKind.Design => "[slot]", _ => "[slot,resource]" }}"),
        };
        return (operand, rest);
    }

    static bool IsName<T>(string name) where T : struct, Enum =>
        name.Length > 0 && !char.IsDigit(name[0]) && Enum.TryParse(name, ignoreCase: false, out T v) && Enum.IsDefined(v);

    static T ParseName<T>(string name) where T : struct, Enum =>
        IsName<T>(name) ? Enum.Parse<T>(name) : throw new FormatException($"unknown {typeof(T).Name} '{name}'");

    static int ParseParam(ParamKind kind, string owner, string? text)
    {
        if (kind == ParamKind.None)
            return text is null ? 0 : throw new FormatException($"{owner} takes no parameter");
        if (text is null) throw new FormatException($"{owner} needs a parameter");
        int value;
        if (ShipAiRegistry.EnumOf(kind) is { } type)
        {
            if (text.Length == 0 || char.IsDigit(text[0]) || !Enum.TryParse(type, text, ignoreCase: false, out object? e) || !Enum.IsDefined(type, e))
                throw new FormatException($"{owner}: '{text}' is not one of {string.Join(", ", Enum.GetNames(type))}");
            value = (int)e;
        }
        else if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            throw new FormatException($"{owner} needs a whole number, not '{text}'");
        var (min, max) = ShipAiRegistry.RangeOf(kind);
        if (value < min || value > max) throw new FormatException($"{owner}={text} is outside {min} to {max}");
        return value;
    }

    // ---- Presets (spec 406, 427, 1076 to 1080, 1083)

    /// <summary>
    /// Spec 406: the engage range of the combat presets, edge to edge. About twice the longest weapon range (missile launcher 700,
    /// railgun 444, most guns 150 to 380), so a sniper still opens fire at its full range, and below the start ship's radar (1807).
    /// </summary>
    public const int EngageRangeDefault = 1500;

    /// <summary>Spec 1083: the default of the Autonomy tunable EnemyClearance and the flee distance of the Looter preset.</summary>
    public const int EnemyClearanceDefault = EngageRangeDefault;

    /// <summary>Spec 1083: the Looter waits while another ship has this share of its hull or less, so the crews keep the loot for repair.</summary>
    public const int RepairedPercent = 90;

    static TacticRow Row(Act act, params Term[] terms) => new(terms, act);

    static TacticRow Row(Act act, int param, params Term[] terms) => new(terms, act, param);

    static readonly Term Always = Term.Fact(Cond.Always);

    static Term Near(int range) => Term.Less(Value.NearestEnemy, range);

    static readonly Term Repaired = Term.Greater(Value.OthersHealth, RepairedPercent);

    static readonly Term CanSell = Term.Fact(Cond.CargoToSell);

    static readonly Term AtStation = Term.Fact(Cond.StationInRange);

    /// <summary>Spec 406. Starting points for the editor. Each one is a plain list of rows that the player can change.</summary>
    public static IReadOnlyList<TacticRow> PresetRows(Preset preset) => preset switch
    {
        // Spec 406: combat presets engage only enemies within EngageRange of the ship, so a far enemy on the team radar does not
        // pull the ship across the system.
        Preset.Aggressive =>
        [
            Row(Act.RetreatRepair, Term.Less(Value.Health, 20)),
            Row(Act.TargetWeakest, Near(EngageRangeDefault)),
            Row(Act.CloseIn, Near(EngageRangeDefault)),
            Row(Act.FocusTarget, 0, Always),
            // Without an enemy, a second ship stays with the fleet. The first ship has no leader, so the row never runs there.
            Row(Act.FollowLeader, Always),
        ],
        Preset.Defensive =>
        [
            Row(Act.RetreatToAlly, Term.Less(Value.Health, 40)),
            Row(Act.TargetAllyAttacker, Term.Fact(Cond.AllyUnderAttack)),
            Row(Act.TargetNearest, Near(EngageRangeDefault)),
            Row(Act.FollowLeader, Term.Greater(Value.NearestEnemy, EngageRangeDefault)),
        ],
        Preset.Sniper =>
        [
            Row(Act.RetreatRepair, Term.Less(Value.Health, 30)),
            Row(Act.TargetNearest, Near(EngageRangeDefault)),
            Row(Act.Kite, Near(EngageRangeDefault)),
            Row(Act.TargetWeapons, 0, Always),
            Row(Act.FollowLeader, Always),
        ],
        Preset.Escort =>
        [
            Row(Act.RetreatToAlly, Term.Less(Value.Health, 25)),
            Row(Act.TargetAllyAttacker, Term.Fact(Cond.AllyUnderAttack)),
            Row(Act.TargetMyAttacker, Term.Fact(Cond.TargetedByEnemy)),
            Row(Act.Guard, (int)GuardWho.Flagship, Always),
        ],
        Preset.VaultGuard =>
        [
            Row(Act.RetreatRepair, Term.Less(Value.Health, 20)),
            Row(Act.TargetVaultAttacker, Always),
            Row(Act.TargetNearest, Near(1200)),
            Row(Act.Guard, (int)GuardWho.VaultShip, Always),
        ],
        // Spec 1076 to 1080: autonomous ship scenarios. Spec 1083: the Looter collects and sells only while every other ship is
        // repaired, and keeps away from enemies.
        Preset.Looter =>
        [
            Row(Act.ReturnToFleet, Near(EnemyClearanceDefault)),
            Row(Act.Sell, AtStation, CanSell),
            Row(Act.GoToStation, (int)StationPick.Nearest, Repaired, Term.Greater(Value.Cargo, 95)),
            Row(Act.CollectLoot, Repaired, Term.Less(Value.NearestLoot, 3000)),
            Row(Act.GoToStation, (int)StationPick.Nearest, Repaired, CanSell),
            Row(Act.ReturnToFleet, Always),
        ],
        Preset.Miner =>
        [
            Row(Act.GoToStation, (int)StationPick.Nearest, Term.Greater(Value.Cargo, 95)),
            Row(Act.Sell, AtStation, CanSell),
            Row(Act.CollectLoot, Term.Less(Value.NearestLoot, 600)),
            Row(Act.Mine, Term.Less(Value.NearestAsteroid, 20000)),
            Row(Act.ReturnToFleet, Always),
        ],
        // Buys the buy list where it is cheapest, then unloads all cargo into the owner's first crewed ship.
        Preset.SupplyRunner =>
        [
            Row(Act.ReturnToFleet, Term.Greater(Value.Cargo, 90)),
            Row(Act.GoToStation, (int)StationPick.Cheapest, Always),
            Row(Act.Unload, Term.Greater(Value.NearestFleet, 300) with { Not = true }),
            Row(Act.Buy, AtStation),
        ],
        // Spec 1079: like the Looter, it waits with the fleet until every other ship is repaired, so the crews keep the materials.
        Preset.Salvager =>
        [
            Row(Act.GoToStation, (int)StationPick.Nearest, Repaired, Term.Greater(Value.Cargo, 95)),
            Row(Act.Sell, AtStation, CanSell),
            Row(Act.CollectLoot, Repaired, Term.Less(Value.NearestLoot, 3000)),
            Row(Act.Salvage, Repaired, Term.Less(Value.NearestWreck, 3000)),
            Row(Act.ReturnToFleet, Always),
        ],
        Preset.EscortDrone =>
        [
            Row(Act.ReturnToFleet, Term.Less(Value.Health, 40)),
            Row(Act.TargetAllyAttacker, Term.Fact(Cond.AllyUnderAttack)),
            Row(Act.TargetNearest, Near(EngageRangeDefault)),
            Row(Act.Guard, (int)GuardWho.Flagship, Always),
        ],
        // Spec 427: no friendly fire, no ramming, a V behind the first ship.
        Preset.Formation =>
        [
            Row(Act.HoldFire, 0, Term.Fact(Cond.AllyInLineOfFire)),
            Row(Act.RetreatToAlly, Term.Less(Value.Health, 25)),
            Row(Act.TargetNearest, Near(EngageRangeDefault)),
            Row(Act.KeepSpacing, 150, Always),
            Row(Act.HoldFormation, 300, Always),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    /// <summary>Spec 410: the card button state for the selected own ships, of which autoOn have Auto on.</summary>
    public static CardState CardStateOf(int autoOn, int ships)
    {
        if (ships <= 0 || autoOn < 0 || autoOn > ships) throw new ArgumentOutOfRangeException(nameof(autoOn), $"{autoOn} of {ships} ships");
        return autoOn == 0 ? CardState.Off : autoOn == ships ? CardState.On : CardState.Mixed;
    }

    /// <summary>
    /// Spec 410: the preset whose rows a ship has, for the card tooltip. A built-in preset first, then a personal preset by its name, in
    /// the order of the editor's list. Both null: the ship has its own rows, or none.
    /// </summary>
    public static (Preset? BuiltIn, string? Personal) PresetOf(string text, IEnumerable<(string Name, string Text)> personal)
    {
        if (text.Length == 0) return (null, null);
        foreach (var p in Enum.GetValues<Preset>())
            if (Encode(PresetRows(p)) == text) return (p, null);
        foreach (var (name, rows) in personal)
            if (rows == text) return (null, name);
        return (null, null);
    }

    // ---- Edits (spec 419, 420, 2321)

    /// <summary>A new body for a term: Not, the connector and the brackets stay.</summary>
    public static TacticRow WithBody(TacticRow row, int j, TermBody body) => WithTerm(row, j, row.Terms[j] with { Body = body });

    /// <summary>
    /// Spec 419: the body that a pick in the condition list gives. A fact keeps its parameter when the kind matches. A value keeps the
    /// comparison and the number when the unit matches, else starts at "&lt; the unit's default"; its arguments start at slot 1 and
    /// the given resource.
    /// </summary>
    public static TermBody Picked(TermBody old, Cond cond) =>
        new FactTerm(cond, old is FactTerm f && ParamOf(f.Cond) == ParamOf(cond) ? f.Param : ShipAiRegistry.Default(ParamOf(cond)));

    public static TermBody Picked(TermBody old, Value value, string resource)
    {
        var def = ShipAiRegistry.Of(value);
        var operand = def.Arg switch
        {
            ArgKind.None => new Operand(value),
            ArgKind.Resource => new Operand(value, Resource: resource),
            ArgKind.Design => new Operand(value, 1),
            ArgKind.DesignResource => new Operand(value, 1, resource),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
        return old is CompareTerm c && UnitOf(c.Left.Value) == def.Unit
            ? c with { Left = operand, Right = c.Right is { } r && UnitOf(r.Value) == def.Unit ? r : null }
            : new CompareTerm(operand, Op.Less, null, ShipAiRegistry.Default(def.Unit));
    }

    /// <summary>Spec 419: the comparison button turns &lt; into &gt;, &gt; into =, = into &lt;.</summary>
    public static Op NextOp(Op op) => op switch { Op.Less => Op.Greater, Op.Greater => Op.Equal, Op.Equal => Op.Less, _ => throw new ArgumentOutOfRangeException(nameof(op), op, null) };

    public static TacticRow WithAct(TacticRow row, Act act) =>
        row with { Act = act, ActParam = ParamOf(act) == ParamOf(row.Act) ? row.ActParam : ShipAiRegistry.Default(ParamOf(act)) };

    /// <summary>The row with term j replaced.</summary>
    public static TacticRow WithTerm(TacticRow row, int j, Term term)
    {
        var terms = (Term[])row.Terms.Clone();
        terms[j] = term;
        return row with { Terms = terms };
    }

    /// <summary>Spec 419: "+ AND" on the THEN line adds an Always term joined by AND at the end, outside every bracket.</summary>
    public static TacticRow AddTerm(TacticRow row) => row with { Terms = [.. row.Terms, Always] };

    /// <summary>Spec 2321: a new Always term joined by AND right after term j, inside the same brackets as j.</summary>
    public static TacticRow InsertTerm(TacticRow row, int j)
    {
        var terms = row.Terms.ToList();
        terms.Insert(j + 1, Always with { Close = terms[j].Close });
        terms[j] = terms[j] with { Close = 0 };
        return row with { Terms = [.. terms] };
    }

    /// <summary>Spec 2321: a new bracket around term j alone. "+" on that term then adds terms inside it.</summary>
    public static TacticRow Bracket(TacticRow row, int j) =>
        WithTerm(row, j, row.Terms[j] with { Open = row.Terms[j].Open + 1, Close = row.Terms[j].Close + 1 });

    /// <summary>
    /// Spec 2321: removes the innermost bracket that opens at term j, and its closing bracket. The terms stay. Brackets of one kind
    /// next to each other are the same text, so any one of the closing brackets at the matching term can go.
    /// </summary>
    public static TacticRow Unbracket(TacticRow row, int j)
    {
        if (row.Terms[j].Open == 0) throw new ArgumentException($"term {j + 1} opens no bracket", nameof(j));
        var depths = Depths(row.Terms);
        int level = depths[j];
        int k = j;
        while (depths[k] - row.Terms[k].Close >= level) k++;
        var terms = (Term[])row.Terms.Clone();
        terms[j] = terms[j] with { Open = terms[j].Open - 1 };
        terms[k] = terms[k] with { Close = terms[k].Close - 1 };
        return row with { Terms = terms };
    }

    /// <summary>
    /// Removes term j. The last term stays, so a row always has a condition. Brackets around only this term go with it; its other
    /// opening brackets move to the next term and its other closing brackets to the term before it.
    /// </summary>
    public static TacticRow RemoveTerm(TacticRow row, int j)
    {
        if (row.Terms.Length <= 1) return row;
        var t = row.Terms[j];
        int alone = Math.Min(t.Open, t.Close);
        var terms = row.Terms.ToList();
        if (t.Open > alone) terms[j + 1] = terms[j + 1] with { Open = terms[j + 1].Open + t.Open - alone };
        if (t.Close > alone) terms[j - 1] = terms[j - 1] with { Close = terms[j - 1].Close + t.Close - alone };
        terms.RemoveAt(j);
        return row with { Terms = [.. terms] };
    }

    // ---- Personal presets (spec 422): a local file, one "name TAB text" line per preset.

    public const int MaxPresetName = 32;

    /// <summary>A preset name without tabs and line breaks, trimmed and cut to MaxPresetName. Empty when nothing is left.</summary>
    public static string CleanPresetName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(char.IsControl(c) ? ' ' : c);
        string clean = sb.ToString().Trim();
        return clean.Length > MaxPresetName ? clean[..MaxPresetName].TrimEnd() : clean;
    }

    public static string FormatPresets(IEnumerable<(string Name, string Text)> presets) =>
        string.Join("\n", presets.Select(p => CleanPresetName(p.Name) + "\t" + p.Text));

    /// <summary>Reads the preset file. A line without a name, without a tab or with bad tactics text is skipped and counted.</summary>
    public static (List<(string Name, string Text)> Presets, int Skipped) ParsePresets(string file)
    {
        var presets = new List<(string Name, string Text)>();
        int skipped = 0;
        foreach (string raw in file.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            int tab = line.IndexOf('\t');
            string name = tab < 0 ? "" : CleanPresetName(line[..tab]);
            string text = tab < 0 ? "" : line[(tab + 1)..].Trim();
            try
            {
                if (name.Length == 0 || text.Length == 0) throw new FormatException("no name or no rows");
                Decode(text);
                presets.Add((name, text));
            }
            catch (FormatException) { skipped++; }
        }
        return (presets, skipped);
    }

    /// <summary>Stable hash of a text (string.GetHashCode differs between processes), for the MP integrity hash and autotests.</summary>
    public static int StableHash(string text)
    {
        int h = 17;
        foreach (char c in text) h = unchecked(h * 31 + c);
        return h;
    }
}

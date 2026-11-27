using System.Globalization;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>Spec 2581: how a breakdown tooltip writes a factor (tunable Ux.BreakdownFactors).</summary>
public enum FactorStyle { Percent, Multiplier }

/// <summary>Spec 2581: the color of a tooltip line. Good and Bad follow what the change means for the players.</summary>
public enum Tone { Plain, Good, Bad, Gray }

/// <summary>One line of a breakdown tooltip: the text key of the step, its argument and its value text.</summary>
public readonly record struct BreakdownLine(string Key, string? Arg, string Value, Tone Tone);

/// <summary>A player action and the change of the total that it gives.</summary>
public readonly record struct BreakdownHint(string Key, string Change, Tone Tone);

/// <summary>What a breakdown tooltip shows, before its keys become text.</summary>
public sealed record BreakdownView(IReadOnlyList<BreakdownLine> Lines, string Total, IReadOnlyList<BreakdownHint> Hints);

/// <summary>
/// Spec 2581: the lines of a breakdown tooltip from the steps of the number. Pure, so unit tests check that the total line is the
/// number that the game uses. Steps that change nothing and round steps do not show.
/// </summary>
public static class BreakdownTextModel
{
    public const string SubtotalKey = "Subtotal";

    public static BreakdownView View(Breakdown b, FactorStyle style, bool higherIsGood)
    {
        var lines = new List<BreakdownLine>(b.Terms.Count + 1);
        bool factorShown = false;
        for (int i = 0; i < b.Terms.Count; i++)
        {
            var t = b.Terms[i];
            // A base of 0 (a sum of options) says nothing; its addends follow.
            if (t.Op == TermOp.Round || (t.Op == TermOp.Base && t.Value == 0)) continue;
            double before = b.Before(i);
            double change = b.Before(i + 1) - before;
            // A step whose change the value text cannot show (a factor of 1, an addend of 0, or less than the last digit) does not show.
            if (t.Op != TermOp.Base && (t.Op == TermOp.Times ? Math.Abs(t.Value - 1) < 0.0005 : Math.Abs(change) < Epsilon(b.Unit))) continue;
            var tone = ToneOf(change, higherIsGood);
            switch (t.Op)
            {
                case TermOp.Base:
                    lines.Add(new(t.Key, t.Arg, Value(b.Unit, t.Value), Tone.Plain));
                    break;
                case TermOp.Plus:
                    if (factorShown) lines.Add(new(SubtotalKey, null, Value(b.Unit, before), Tone.Gray));
                    factorShown = false;
                    lines.Add(new(t.Key, t.Arg, Signed(b.Unit, t.Value), tone));
                    break;
                case TermOp.Times:
                    factorShown = true;
                    lines.Add(new(t.Key, t.Arg, Factor(style, t.Value), tone));
                    break;
                case TermOp.Clamp:
                    lines.Add(change < 0 ? new(t.Key + "Max", Value(b.Unit, t.Upper), Signed(b.Unit, change), tone)
                        : new(t.Key + "Min", Value(b.Unit, t.Value), Signed(b.Unit, change), tone));
                    break;
            }
        }
        var hints = new List<BreakdownHint>();
        for (int i = 0; i < b.Terms.Count; i++)
        {
            if (b.Terms[i].Hint is not { } key) continue;
            double change = b.WithHint(i) - b.Total;
            if (Math.Abs(change) < Epsilon(b.Unit)) continue;
            hints.Add(new(key, Signed(b.Unit, change) + Suffix(b.Unit), ToneOf(change, higherIsGood)));
        }
        return new(lines, Total(b.Unit, b.Total), hints);
    }

    /// <summary>The total text of a value: the value with its unit.</summary>
    public static string Total(BreakdownUnit unit, double v) => Value(unit, v) + Suffix(unit);

    static Tone ToneOf(double change, bool higherIsGood) =>
        change == 0 ? Tone.Plain : (change > 0) == higherIsGood ? Tone.Good : Tone.Bad;

    /// <summary>The smallest change that the value text shows.</summary>
    static double Epsilon(BreakdownUnit unit) => unit switch
    {
        BreakdownUnit.HeatPerMinute => 0.005,
        BreakdownUnit.Chance => 0.005,
        BreakdownUnit.Credits => 0.5,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    public static string Value(BreakdownUnit unit, double v) => unit switch
    {
        BreakdownUnit.HeatPerMinute => v.ToString("0.00", CultureInfo.InvariantCulture),
        BreakdownUnit.Chance => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%",
        BreakdownUnit.Credits => v.ToString("N0", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    public static string Signed(BreakdownUnit unit, double v) => (v < 0 ? "-" : "+") + Value(unit, Math.Abs(v));

    static string Suffix(BreakdownUnit unit) => unit == BreakdownUnit.HeatPerMinute ? "/min" : "";

    /// <summary>Spec 2581: x1.20 is "+20%" in Percent style and "x1.20" in Multiplier style.</summary>
    public static string Factor(FactorStyle style, double f) => style switch
    {
        FactorStyle.Percent => (f < 1 ? "-" : "+") + Math.Abs((f - 1) * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%",
        FactorStyle.Multiplier => "x" + f.ToString("0.00", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
    };
}

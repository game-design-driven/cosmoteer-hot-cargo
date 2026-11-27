namespace HotCargo.Core;

/// <summary>What a breakdown counts. The UI formats the total, the base and each addend in this unit.</summary>
public enum BreakdownUnit { HeatPerMinute, Chance, Credits, Factor }

/// <summary>Spec 2580: how one step changes the total, in the order of the steps.</summary>
public enum TermOp
{
    /// <summary>The start value. Only the first step.</summary>
    Base,
    Plus,
    Times,
    /// <summary>Keeps the total in Value..Upper.</summary>
    Clamp,
    /// <summary>Rounds the total to a multiple of Value, halves away from zero.</summary>
    Round,
}

/// <summary>
/// Spec 2580: one step of a number. Key names its text (section HotCargoBreakdown); Arg fills the {0} of that text.
/// Hint names a player action that changes the step, and Alt is the step's value after it (null: the step goes away).
/// </summary>
public readonly record struct Term(TermOp Op, string Key, double Value, string? Arg = null, string? Hint = null, double? Alt = null, double Upper = 0)
{
    /// <summary>The value of a step that changes nothing: 1 for a factor, 0 for the others.</summary>
    public double Neutral => Op == TermOp.Times ? 1 : 0;
}

/// <summary>
/// Spec 2580: a number as the ordered list of its steps. The steps are the only way to change the total, so the total that the game
/// uses and the steps that a tooltip shows come from one computation and cannot drift apart.
/// Limit: every computation stores its steps, also in sim ticks (one small list each). Revisit if a profile shows these allocations.
/// </summary>
public sealed class Breakdown(BreakdownUnit unit)
{
    readonly List<Term> _terms = [];

    public BreakdownUnit Unit => unit;
    public IReadOnlyList<Term> Terms => _terms;
    public double Total { get; private set; }

    public Breakdown Base(string key, double value, string? arg = null)
    {
        if (_terms.Count > 0) throw new InvalidOperationException($"base '{key}' after {_terms.Count} steps");
        return Add(new Term(TermOp.Base, key, value, arg));
    }

    public Breakdown Plus(string key, double value, string? arg = null, string? hint = null, double? alt = null) =>
        Add(new Term(TermOp.Plus, key, value, arg, hint, alt));

    public Breakdown Times(string key, double factor, string? arg = null, string? hint = null, double? alt = null) =>
        Add(new Term(TermOp.Times, key, factor, arg, hint, alt));

    public Breakdown Clamp(string key, double min, double max)
    {
        if (min > max) throw new ArgumentOutOfRangeException(nameof(min), $"clamp {min}..{max}");
        return Add(new Term(TermOp.Clamp, key, min, Upper: max));
    }

    public Breakdown Round(string key, double step)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), $"round step {step}");
        return Add(new Term(TermOp.Round, key, step));
    }

    Breakdown Add(Term t)
    {
        if (double.IsNaN(t.Value) || (t.Alt is { } a && double.IsNaN(a))) throw new ArgumentOutOfRangeException(nameof(t), $"step '{t.Key}' is NaN");
        _terms.Add(t);
        Total = Apply(Total, t, t.Value);
        return this;
    }

    static double Apply(double total, Term t, double value) => t.Op switch
    {
        TermOp.Base => value,
        TermOp.Plus => total + value,
        TermOp.Times => total * value,
        TermOp.Clamp => Math.Clamp(total, value, t.Upper),
        TermOp.Round => Math.Round(total / value, MidpointRounding.AwayFromZero) * value,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t.Op, null),
    };

    /// <summary>The total again from the steps, with step <paramref name="index"/> at <paramref name="value"/>, or as it is for -1.</summary>
    public double Fold(int index = -1, double value = 0)
    {
        double total = 0;
        for (int i = 0; i < _terms.Count; i++) total = Apply(total, _terms[i], i == index ? value : _terms[i].Value);
        return total;
    }

    /// <summary>The total after the hint of step <paramref name="index"/>: that step at its Alt, or neutral without one.</summary>
    public double WithHint(int index)
    {
        var t = _terms[index];
        if (t.Hint is null) throw new InvalidOperationException($"step '{t.Key}' has no hint");
        return Fold(index, t.Alt ?? t.Neutral);
    }

    /// <summary>The total before step <paramref name="index"/>: the subtotal that a tooltip shows above a change of kind.</summary>
    public double Before(int index)
    {
        double total = 0;
        for (int i = 0; i < index; i++) total = Apply(total, _terms[i], _terms[i].Value);
        return total;
    }
}

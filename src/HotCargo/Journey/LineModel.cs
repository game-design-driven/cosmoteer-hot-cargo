namespace HotCargo.Journey;

/// <summary>What the players hear about one choice (spec 2561).</summary>
public enum LineNotice { None, FirstWarning, LastWarning, Closed, Reopened }

/// <summary>The warning state of one choice of the players' system: the last warning given, the grace end, and whether it is closed.</summary>
public readonly record struct ChoiceState(int Warned, double GraceUntil, bool Closed)
{
    public static readonly ChoiceState Fresh = new(0, -1, false);
}

/// <summary>
/// Spec 2557 to 2563: the corporate lockdown line. It follows the players' jumps and their heat. Every system of a band behind the
/// furthest band that the players entered is closed. Of the choices of the players' system (its neighbors that are not behind), more close
/// as heat rises: all stay open at heat 0, and exactly two (or all, when there are fewer) at heat 100. The Buyer never closes.
/// No game types; the unit tests cover it.
/// </summary>
public static class LineModel
{
    public const float MaxHeat = 100f;

    /// <summary>Spec 2557: a system of a band before the furthest band that the players entered is behind them, so closed.</summary>
    public static bool Behind(int band, int furthestBand) => band < furthestBand;

    /// <summary>Spec 2558: choices that may close: all but two (none when there are two or fewer).</summary>
    public static int Closable(int choices) => Math.Max(0, choices - 2);

    /// <summary>
    /// Spec 2558: the heat at which the choice of this close rank closes (rank 0 first). The ranks spread evenly up to heat 100, where all
    /// closable choices are closed. Null when the rank never closes.
    /// </summary>
    public static float? Threshold(int rank, int choices) =>
        rank < Closable(choices) ? MaxHeat * (rank + 1) / Closable(choices) : null;

    /// <summary>Game seconds until heat reaches the threshold at this heat rate, or null when it does not rise.</summary>
    public static double? SecondsTo(float threshold, float heat, float heatPerSecond) =>
        heat >= threshold ? 0 : heatPerSecond <= 0f ? null : (threshold - heat) / (double)heatPerSecond;

    /// <summary>
    /// Spec 2561: one tick of a choice. Warnings come when the time until heat reaches its threshold (at the current time heat rate) passes
    /// <paramref name="firstWarning"/> and <paramref name="lastWarning"/> seconds. A choice never closes without the last warning: when heat
    /// jumps over the threshold first (a mission, an event), the last warning comes then, and the choice closes after
    /// <paramref name="lastWarning"/> seconds if heat stays at or above the threshold. Heat below the threshold reopens it.
    /// </summary>
    public static (ChoiceState State, LineNotice Notice) Tick(ChoiceState s, float? threshold, float heat, float heatPerSecond, double now,
        double firstWarning, double lastWarning)
    {
        if (threshold is not { } level)
            return (ChoiceState.Fresh, s.Closed ? LineNotice.Reopened : LineNotice.None);
        var notice = LineNotice.None;
        if (heat >= level)
        {
            if (s.Warned < 2) { s = new ChoiceState(2, now + lastWarning, false); notice = LineNotice.LastWarning; }
            bool closed = !(s.GraceUntil > now);
            if (closed && !s.Closed) notice = LineNotice.Closed;
            return (s with { Closed = closed }, notice);
        }
        if (s.Closed) { s = ChoiceState.Fresh; notice = LineNotice.Reopened; }
        double? t = SecondsTo(level, heat, heatPerSecond);
        if (t is not { } seconds || seconds > firstWarning) return (s with { Warned = 0, GraceUntil = -1 }, notice);
        if (seconds <= lastWarning && s.Warned < 2) return (s with { Warned = 2, GraceUntil = -1 }, LineNotice.LastWarning);
        if (s.Warned < 1) return (s with { Warned = 1 }, LineNotice.FirstWarning);
        return (s, notice);
    }
}

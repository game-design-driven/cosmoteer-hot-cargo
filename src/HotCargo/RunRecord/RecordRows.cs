using System.Globalization;
using Cosmoteer.Localization;

namespace HotCargo.RunRecord;

/// <summary>Spec 1225: the "PAST RUNS" lines of the career log tab, as XML for labels. UI only.</summary>
public static class RecordRows
{
    /// <summary>Entry rows in the open tab. For autotests.</summary>
    internal static int Shown;

    public static List<string> Lines(IReadOnlyList<RunEntry> runs)
    {
        var lines = new List<string> { Text("Header") };
        if (Recorder.LoadError is { } error) lines.Add(Format("ErrorFmt", Halfling.StringTools.EscapeForXml(error)));
        Shown = runs.Count;
        if (runs.Count == 0)
        {
            lines.Add(Text("Empty"));
            return lines;
        }
        lines.Add(RunRecordModel.Best(runs) is { } best ? Format("BestFmt", best.Score.ToString("N0", CultureInfo.CurrentCulture)) : Text("NoBest"));
        lines.AddRange(RunRecordModel.Rows(runs).Select(Row));
        return lines;
    }

    static string Row(RunEntry e) =>
        Format(e.Won ? "RowWonFmt" : "RowLostFmt", e.Ended.LocalDateTime.ToString("d MMM", CultureInfo.CurrentCulture),
            Cell(e, RunColumn.Score), e.Sector, e.Sectors, Cell(e, RunColumn.Time), Cell(e, RunColumn.PeakHeat), e.VaultsLost);

    /// <summary>The score steps of a won run with saved parts, or null (spec 2590).</summary>
    static Core.Breakdown? ScoreSteps(RunEntry e) =>
        e.Won && e.Parts is { Count: > 0 } parts ? Core.Breakdown.FromParts(Core.BreakdownUnit.Credits, parts) : null;

    static string Date(RunEntry e) => e.Ended.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>Spec 2591: the tooltip of a score in the Past Runs table: its parts in the breakdown style, or why it has none.</summary>
    public static string ScoreTip(RunEntry e) => ScoreSteps(e) is { } b
        ? Ux.BreakdownText.Tooltip(Format("ScoreTipFmt", Cell(e, RunColumn.Score)), b, higherIsGood: true)
        : Text(e.Won ? "ScoreNoParts" : "ScoreLost");

    /// <summary>Spec 2591: the score parts of the selected run under the Past Runs table, or "" without a selection.</summary>
    public static string ScoreDetail(RunEntry? e) => e switch
    {
        null => "",
        _ when ScoreSteps(e) is { } b => Format("DetailFmt", Date(e)) + Ux.BreakdownText.Steps(b, higherIsGood: true),
        { Won: true } => Format("DetailNoPartsFmt", Date(e)),
        _ => Format("DetailLostFmt", Date(e)),
    };

    /// <summary>Spec 1225, 1229: the text of one value of an entry, the same in the career log block and in the Past Runs table.</summary>
    public static string Cell(RunEntry e, RunColumn column)
    {
        var time = TimeSpan.FromSeconds(e.Seconds);
        return column switch
        {
            RunColumn.Date => e.Ended.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
            RunColumn.Result => Text(e.Won ? "Won" : "Lost"),
            RunColumn.Score => e.Score.ToString("N0", CultureInfo.CurrentCulture),
            RunColumn.Delivered => e.Delivered.ToString(CultureInfo.CurrentCulture),
            RunColumn.Time => $"{(int)time.TotalHours}:{time.Minutes:00}",
            RunColumn.PeakHeat => e.PeakHeat.ToString("0", CultureInfo.InvariantCulture),
            RunColumn.VaultsLost => e.VaultsLost.ToString(CultureInfo.CurrentCulture),
            RunColumn.Players => e.Players.ToString(CultureInfo.CurrentCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };
    }

    internal static string Text(string key) => Strings.GetText("HotCargoRunRecord/" + key);
    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoRunRecord/" + key, args);
}

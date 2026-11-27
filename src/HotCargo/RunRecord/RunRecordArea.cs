using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.RunRecord;

/// <summary>Entry point of the RunRecord area (spec 1220 to 1226): past runs of this machine. Mod.Boot calls Register once.</summary>
public static class RunRecordArea
{
    public static void Register()
    {
        Run.Hooks.Tick += (run, _, _) => run.State.RunRecord.PeakHeat = Math.Max(run.State.RunRecord.PeakHeat, run.State.Heat);
        Run.Hooks.Frame += run => Errors.Guard("run record", () => Recorder.Update(run));
        AddAutotest();
    }

    static RunEntry? ThisRun()
    {
        string id = Recorder.Entry(Autotest.RequireRun()).Id;
        return (Recorder.Runs ?? Recorder.Load())?.FirstOrDefault(r => r.Id == id);
    }

    static void AddAutotest()
    {
        // rr_clear: deletes the record file of this test slot and resets the recorder. Test slots only: the slot has its own save folder.
        Autotest.Commands["rr_clear"] = _ =>
        {
            if (File.Exists(Recorder.FilePath)) File.Delete(Recorder.FilePath);
            Recorder.ResetForTest();
        };
        Autotest.Fields["rr_entries"] = () => Recorder.Load()?.Count ?? -1;
        Autotest.Fields["rr_peakheat_state"] = () => Autotest.RequireRun().State.RunRecord.PeakHeat;
        // The entry of the current run in the file: present, won, score, peak heat, lost vaults, delivered units and sectors.
        Autotest.Fields["rr_has"] = () => ThisRun() is null ? 0 : 1;
        Autotest.Fields["rr_won"] = () => ThisRun()?.Won == true ? 1 : 0;
        Autotest.Fields["rr_score"] = () => ThisRun()?.Score ?? -1;
        Autotest.Fields["rr_peakheat"] = () => ThisRun()?.PeakHeat ?? -1;
        Autotest.Fields["rr_vaultslost"] = () => ThisRun()?.VaultsLost ?? -1;
        Autotest.Fields["rr_delivered"] = () => ThisRun()?.Delivered ?? -1;
        Autotest.Fields["rr_sectors"] = () => ThisRun()?.Sectors ?? -1;
        Autotest.Fields["rr_tab_rows"] = () => RecordRows.Shown;
        // Spec 2590: |the sum of this run's saved score parts − its score|, and the count of parts (money, held units, debt).
        Autotest.Fields["rr_parts_gap"] = () => ThisRun() is { Parts: { } parts } e ? Math.Abs(parts.Sum(p => p.Value) - e.Score) : -1;
        Autotest.Fields["rr_parts"] = () => ThisRun()?.Parts?.Count ?? 0;
        AddPastRunsAutotest();
    }

    static PastRunsDialog Window() => PastRunsDialog.Open ?? throw new InvalidOperationException("the Past Runs window is not open");

    /// <summary>Spec 1227 to 1233: the Past Runs window. These commands work on the main menu, before a career starts.</summary>
    static void AddPastRunsAutotest()
    {
        // pr_sample: writes three sample runs to this test slot's record file (scores 120,000 won, 340,000 won, 0 lost; newest last).
        // The won runs have score parts (spec 2590), with the keys of the score code: 340,000 = 160,000 money + 2 units x 120,000 - a
        // 30,000 debt x 2. Each score is the total of its parts.
        Autotest.Commands["pr_sample"] = _ =>
        {
            var day = new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);
            var best = new Core.Breakdown(Core.BreakdownUnit.Credits).Base("ScoreMoney", 160_000).Plus("ScoreHeld", 240_000, "2 x 120,000")
                .Plus("ScoreDebt", -60_000, "30,000");
            var other = new Core.Breakdown(Core.BreakdownUnit.Credits).Base("ScoreMoney", 120_000).Plus("ScoreHeld", 0, "0 x 90,000");
            List<RunEntry> runs =
            [
                new("3", day.AddDays(2), false, 0, 0, 1500, 4, 6, 100f, 3, 2),
                new("2", day.AddDays(1), true, (long)best.Total, 6, 5400, 10, 10, 95f, 1, 1, best.SumParts()),
                new("1", day, true, (long)other.Total, 4, 4200, 8, 8, 80f, 0, 1, other.SumParts()),
            ];
            Directory.CreateDirectory(Path.GetDirectoryName(Recorder.FilePath)!);
            File.WriteAllText(Recorder.FilePath, RunRecordModel.Write(runs));
            Recorder.ResetForTest();
        };
        // pr_badfile: writes a record file that does not read.
        Autotest.Commands["pr_badfile"] = _ =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Recorder.FilePath)!);
            File.WriteAllText(Recorder.FilePath, "{ not json");
            Recorder.ResetForTest();
        };
        // pr_click: clicks the main menu button like a player (the button's own click event).
        Autotest.Commands["pr_click"] = _ =>
        {
            var button = PastRunsButton.Shown is { } b ? b : throw new InvalidOperationException("no Past Runs button on the main menu");
            button.ClickController.InvokeClicked();
            Autotest.Wait(1);
        };
        // pr_sort <column>: clicks the header of a RunColumn.
        // pr_hoverscore <row>: opens the score tooltip of a table row as a hover does (spec 2591). Then: wait 1, expect hovertip_ok == 1.
        Autotest.Commands["pr_hoverscore"] = arg =>
        {
            int row = int.Parse(arg.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            Ux.BreakdownAutotest.Hover(Window().ScoreButton(row), $"score of row {row}", Core.BreakdownUnit.Credits, () => Window().Shown[row].Score);
        };
        // pr_pickscore <row>: clicks the score of a table row like a player; it fails when the player cannot click it.
        Autotest.Commands["pr_pickscore"] = arg =>
        {
            var button = Window().ScoreButton(int.Parse(arg.Trim(), System.Globalization.CultureInfo.InvariantCulture));
            if (!button.IsActive || !button.IsInputActive) throw new InvalidOperationException("the player cannot click that score");
            button.ClickController.InvokeClicked();
        };
        // 1 when the score parts under the table belong to the selected run and end with its score as the total.
        Autotest.Fields["pr_detail_ok"] = () =>
        {
            var w = Window();
            var picked = w.Selected ?? throw new InvalidOperationException("no run is selected");
            bool ok = w.DetailText.Contains(Ux.BreakdownText.TotalLine(Core.BreakdownUnit.Credits, picked.Score), StringComparison.Ordinal);
            Log.Info($"run record: detail ok={ok} for run {picked.Id}:\n{w.DetailText}");
            return ok ? 1 : 0;
        };
        Autotest.Commands["pr_sort"] = arg => Window().SortBy(Enum.Parse<RunColumn>(arg.Trim()));
        Autotest.Commands["pr_close"] = _ => { Window().Close(); Autotest.Wait(1); };
        Autotest.Fields["pr_button"] = () => PastRunsButton.Shown is null ? 0 : 1;
        Autotest.Fields["pr_open"] = () => PastRunsDialog.Open is null ? 0 : 1;
        Autotest.Fields["pr_rows"] = () => Window().Shown.Count;
        // The score of the first table row, and the ID of the first row (the sample IDs are numbers).
        Autotest.Fields["pr_first_score"] = () => Window().Shown[0].Score;
        Autotest.Fields["pr_first_id"] = () => double.Parse(Window().Shown[0].Id, System.Globalization.CultureInfo.InvariantCulture);
        Autotest.Fields["pr_best_row"] = () => Window().BestRow;
        Autotest.Fields["pr_descending"] = () => Window().Descending ? 1 : 0;
        Autotest.Fields["pr_error"] = () => Recorder.LoadError is null ? 0 : 1;
    }
}

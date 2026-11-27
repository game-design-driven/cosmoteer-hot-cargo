using HotCargo.Core;
using HotCargo.Debt;
using HotCargo.Journey;
using HotCargo.RunRecord;
using Xunit;

namespace HotCargo.Tests;

/// <summary>Spec 7, 1411, 2590: the score is its breakdown, and the saved parts add up to it.</summary>
public class ScoreModelTests
{
    /// <summary>The score as Buyer.OnDelivered builds it: money and held units, then the debt step of the Debt area.</summary>
    static Breakdown Score(long money, int held, long price, long debt) =>
        ScoreModel.Terms(money, held, price).Plus("ScoreDebt", -DebtModel.ScoreCut(debt, 2f), debt.ToString());

    [Fact]
    public void The_score_is_money_plus_held_units_minus_the_debt_cut()
    {
        Assert.Equal(160_000 + 2 * 120_000 - 2 * 30_000, Score(160_000, 2, 120_000, 30_000).Total);
        Assert.Equal(100_000, ScoreModel.Terms(100_000, 0, 50_000).Total);
        Assert.Throws<ArgumentOutOfRangeException>(() => ScoreModel.Terms(0, -1, 1));
    }

    [Fact]
    public void Saved_parts_give_the_same_score_and_steps()
    {
        var b = Score(160_000, 2, 120_000, 30_000);
        var parts = b.SumParts();
        Assert.Equal((long)b.Total, parts.Sum(p => p.Value));
        var again = Breakdown.FromParts(BreakdownUnit.Credits, parts);
        Assert.Equal(b.Total, again.Total);
        Assert.Equal(b.Terms.Select(t => (t.Key, t.Arg, t.Value)), again.Terms.Select(t => (t.Key, t.Arg, t.Value)));
        Assert.Throws<InvalidOperationException>(() => new Breakdown(BreakdownUnit.Credits).Base("A", 1).Times("B", 2).SumParts());
    }

    [Fact]
    public void A_record_entry_keeps_its_parts_and_rejects_parts_that_do_not_add_up()
    {
        var b = Score(160_000, 2, 120_000, 30_000);
        var day = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        var entry = new RunEntry("1", day, true, (long)b.Total, 6, 5400, 10, 10, 95f, 1, 1, b.SumParts());
        var read = RunRecordModel.Read(RunRecordModel.Write([entry]));
        Assert.Equal(entry, read[0]);
        // An entry with equal parts changes nothing, so the recorder does not write again.
        Assert.Null(RunRecordModel.Upsert(read, entry with { Parts = b.SumParts() }, 200));
        Assert.Throws<InvalidDataException>(() => RunRecordModel.Read(RunRecordModel.Write([entry with { Score = 1 }])));
    }
}

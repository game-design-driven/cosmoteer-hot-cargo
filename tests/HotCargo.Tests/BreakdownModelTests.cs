using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.MultiChoice;
using HotCargo.Tech;
using HotCargo.Ux;
using Xunit;

namespace HotCargo.Tests;

/// <summary>Spec 2580, 2581: the steps make the total, and the tooltip view shows that total.</summary>
public class BreakdownModelTests
{
    static Breakdown Heat() => new Breakdown(BreakdownUnit.HeatPerMinute).Base("HeatBase", 1.14)
        .Times("HeatTech", 1.2, "8", "HintSellOne", 1.1)
        .Times("ZoneAlly", 1.5, "Monolith", "HintLeaveAlly")
        .Times("Debt", 1.0, hint: "HintPayDebt")
        .Plus("Trail", 0.25, "2");

    [Fact]
    public void The_steps_fold_to_the_total()
    {
        var b = Heat();
        Assert.Equal(1.14 * 1.2 * 1.5 + 0.25, b.Total, 9);
        Assert.Equal(b.Total, b.Fold(), 12);
        Assert.Equal(1.14 * 1.2, b.Before(2), 9);
    }

    [Fact]
    public void A_hint_changes_one_step()
    {
        var b = Heat();
        Assert.Equal(1.14 * 1.1 * 1.5 + 0.25, b.WithHint(1), 9); // sell one: the table value of 7 units
        Assert.Equal(1.14 * 1.2 + 0.25, b.WithHint(2), 9); // leave: the factor goes away
        Assert.Equal(b.Total, b.WithHint(3), 9); // no debt: paying changes nothing
        Assert.Throws<InvalidOperationException>(() => b.WithHint(0));
    }

    [Fact]
    public void Clamp_and_round_are_steps_too()
    {
        var b = new Breakdown(BreakdownUnit.Chance).Base("A", 0.9).Plus("B", 0.3).Clamp("Clamp", 0, 1);
        Assert.Equal(1, b.Total, 9);
        var r = new Breakdown(BreakdownUnit.Credits).Base("A", 103_150).Round("Round", 100);
        Assert.Equal(103_200, r.Total); // halves away from zero
        Assert.Equal(r.Total, r.Fold());
    }

    [Fact]
    public void Bad_steps_crash()
    {
        Assert.Throws<InvalidOperationException>(() => new Breakdown(BreakdownUnit.Chance).Plus("A", 1).Base("B", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Breakdown(BreakdownUnit.Chance).Base("A", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Breakdown(BreakdownUnit.Chance).Clamp("A", 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Breakdown(BreakdownUnit.Credits).Round("A", 0));
    }

    [Fact]
    public void The_view_total_is_the_number()
    {
        var b = Heat();
        var view = BreakdownTextModel.View(b, FactorStyle.Percent, higherIsGood: false);
        Assert.Equal(BreakdownTextModel.Value(BreakdownUnit.HeatPerMinute, b.Total) + "/min", view.Total);
        // The debt factor of 1 does not show; a subtotal shows before the trail.
        Assert.DoesNotContain(view.Lines, l => l.Key == "Debt");
        Assert.Equal(["HeatBase", "HeatTech", "ZoneAlly", BreakdownTextModel.SubtotalKey, "Trail"], view.Lines.Select(l => l.Key));
        Assert.Equal("+50%", view.Lines[2].Value);
        Assert.Equal(Tone.Bad, view.Lines[2].Tone); // more heat is bad
        Assert.Equal(["HintSellOne", "HintLeaveAlly"], view.Hints.Select(h => h.Key));
        Assert.Equal(Tone.Good, view.Hints[1].Tone);
        Assert.StartsWith("-", view.Hints[1].Change);
    }

    [Fact]
    public void Factor_style_is_percent_or_multiplier()
    {
        Assert.Equal("+20%", BreakdownTextModel.Factor(FactorStyle.Percent, 1.2));
        Assert.Equal("-20%", BreakdownTextModel.Factor(FactorStyle.Percent, 0.8));
        Assert.Equal("+0.5%", BreakdownTextModel.Factor(FactorStyle.Percent, 1.005));
        Assert.Equal("x1.20", BreakdownTextModel.Factor(FactorStyle.Multiplier, 1.2));
    }

    [Fact]
    public void A_factor_breakdown_shows_factors_in_the_style()
    {
        var b = new Breakdown(BreakdownUnit.Factor).Base("Standing", 0.8, "Friendly").Times("HeatMarkup", 1.2, "80", "HintCoolDown");
        var view = BreakdownTextModel.View(b, FactorStyle.Percent, higherIsGood: false);
        Assert.Equal("-20%", view.Lines[0].Value);
        Assert.Equal("-4%", view.Total); // 0.8 × 1.2 = 0.96
        Assert.Equal("-20%", view.Hints[0].Change); // the factor with no heat
        Assert.Equal("x0.96", BreakdownTextModel.View(b, FactorStyle.Multiplier, false).Total);
        Assert.Throws<ArgumentException>(() => BreakdownTextModel.Signed(BreakdownUnit.Factor, 0.1));
    }

    // One test for each number: the game value is the breakdown total, and the total is the formula of its spec requirement.

    [Fact]
    public void Surrender_chance_is_its_breakdown()
    {
        var s = new DiplomacyModel.SurrenderInputs(0.6f, 0.6f, 0.5f, 0.2f, 2, true, false, 40f);
        var b = DiplomacyModel.SurrenderTerms(s, 0.2f, 0.3f, 0.25f, 0.1f, 0.1f, 0.04f, 0.04f, 0.3f);
        Assert.Equal(b.Total, b.Fold(), 12);
        Assert.Equal((float)b.Total, DiplomacyModel.SurrenderChance(s, 0.2f, 0.3f, 0.25f, 0.1f, 0.1f, 0.04f, 0.04f, 0.3f));
        // Spec 567.
        Assert.Equal(0.2 + 0.3 * 0.6 + 0.25 * 0.6 + 0.1 * 0.5 + 0.1 * 0.2 - 0.04 * 2 - 0.04, b.Total, 5);
        // The hunter heat step is 0 for a ship that is not a hunter, so it does not show.
        Assert.DoesNotContain(BreakdownTextModel.View(b, FactorStyle.Percent, true).Lines, l => l.Key == "HunterHeat");
    }

    [Fact]
    public void Bluff_chance_is_its_breakdown()
    {
        var b = DiplomacyModel.BluffTerms(4, 8, 0.5f, 0.6f, 0.2f, 1.2f);
        Assert.Equal((float)b.Total, DiplomacyModel.BluffChance(4, 8, 0.5f, 0.6f, 0.2f, 1.2f));
        Assert.Equal(0.6 + 0.2 * 4 / 8.0 - 1.2 * 0.5, b.Total, 5); // spec 565
    }

    [Fact]
    public void Pay_off_chance_is_its_breakdown()
    {
        var b = MultiChoiceModel.CombineTerms([("PayTech", 0.3f, "3"), ("PayMoney", 0.4f, "x")], 0.5f, "Crowd", 0.9f, "1");
        Assert.Equal((0.3 + 0.4) * 0.5, b.Total, 5); // spec 594: crowd × the rows, at most the cap
        Assert.Equal(0.9, MultiChoiceModel.CombineTerms([("PayTech", 0.6f, null), ("PayMoney", 0.8f, null)], 1f, "Crowd", 0.9f).Total, 5);
        Assert.Throws<ArgumentException>(() => MultiChoiceModel.CombineTerms([], 0.5f, null, 0.9f));
        Assert.DoesNotContain(BreakdownTextModel.View(b, FactorStyle.Percent, true).Lines, l => l.Key == "OfferNone");
    }

    [Fact]
    public void Tech_value_is_its_breakdown()
    {
        var b = TechModel.ValueTerms(100_000, 3, 0.25f, 0.1f, 0.05f, 1f, 5f, 3600, 100);
        Assert.Equal(b.Total, b.Fold(), 12);
        Assert.Equal((long)b.Total, TechModel.Value(100_000, 0.25f, 0.1f, 0.05f, 1f, 5f, 3600, 100));
        Assert.Equal(40_000, b.Total); // spec 19: 100,000 × (0.25 + 0.1 × 1 h + 0.05 × 100 / 100)
        // Spec 683: the difficulty factor and the round to 100 follow (TechTunablesExtensions.TechValueTerms).
        var hard = TechModel.ValueTerms(114_600, 1, 1f, 0, 0, 1f, 5f, 0, 0).Times("TechDifficulty", 0.9f).Round("Round", 100);
        Assert.Equal(103_100, hard.Total); // 103,140
    }
}

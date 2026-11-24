using HotCargo.Diplomacy;
using Xunit;

namespace HotCargo.Tests;

public class DiplomacyModelTests
{
    [Fact]
    public void Leverage_is_log4_of_the_strength_ratio_clamped()
    {
        Assert.Equal(0f, DiplomacyModel.Leverage(10, 10), 4);
        Assert.Equal(0.5f, DiplomacyModel.Leverage(20, 10), 4);
        Assert.Equal(1f, DiplomacyModel.Leverage(100, 10));
        Assert.Equal(-1f, DiplomacyModel.Leverage(1, 100));
        Assert.Equal(1f, DiplomacyModel.Leverage(5, 0));
        Assert.Equal(-1f, DiplomacyModel.Leverage(0, 5));
        Assert.Equal(0f, DiplomacyModel.Leverage(0, 0));
    }

    [Fact]
    public void Concentration_joins_vaults_within_the_pile_distance()
    {
        var spread = new List<TechPile> { new(0, 0, 2), new(50, 0, 2), new(100, 0, 2) };
        Assert.Equal(2f / 8f, DiplomacyModel.Concentration(spread, 8, 5f), 4);
        var close = new List<TechPile> { new(0, 0, 2), new(3, 0, 2), new(6, 0, 2) };
        Assert.Equal(6f / 8f, DiplomacyModel.Concentration(close, 8, 5f), 4); // a chain of close vaults is one pile
        Assert.Equal(0f, DiplomacyModel.Concentration([], 8, 5f));
    }

    [Fact]
    public void Other_hunters_lower_the_pay_off_and_stop_it_above_the_limit()
    {
        Assert.Equal(1f, DiplomacyModel.Crowd(0, 4));
        Assert.Equal(0.6f, DiplomacyModel.Crowd(2, 4), 4);
        Assert.Equal(0f, DiplomacyModel.Crowd(5, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyModel.Crowd(-1, 4));
    }

    [Fact]
    public void Bluff_gives_a_share_of_the_stock_but_never_all_of_it()
    {
        Assert.Equal(2, DiplomacyModel.BluffUnits(8, 0.25f));
        Assert.Equal(1, DiplomacyModel.BluffUnits(2, 0.25f));
        Assert.Equal(2, DiplomacyModel.BluffUnits(3, 1f));
        Assert.Equal(0, DiplomacyModel.BluffUnits(1, 0.25f));
        Assert.Equal(0, DiplomacyModel.BluffUnits(0, 0.25f));
    }

    [Fact]
    public void Bluff_works_better_with_a_big_share_and_spread_tech()
    {
        float small = DiplomacyModel.BluffChance(1, 8, 0f, 0.6f, 0.2f, 1.2f);
        float big = DiplomacyModel.BluffChance(6, 8, 0f, 0.6f, 0.2f, 1.2f);
        float bigVisible = DiplomacyModel.BluffChance(6, 8, 0.25f, 0.6f, 0.2f, 1.2f);
        Assert.True(big > small);
        Assert.Equal(big - 0.3f, bigVisible, 4);
    }

    [Fact]
    public void Hire_chance_is_the_share_of_the_sure_price_between_the_floor_and_the_sure_price()
    {
        Assert.Equal(0f, DiplomacyModel.HireChance(1_000, 1_000, 0.6f, 1f, 0f, wanted: true, 1f, 0.3f, 0.4f));
        Assert.Equal(1f, DiplomacyModel.HireChance(1_000, 1_000, 0.6f, 0f, 0f, false, 1f, 0.3f, 0.4f));
        Assert.Equal(0.6f, DiplomacyModel.HireChance(600, 1_000, 0.6f, 0f, 0f, false, 1f, 0.3f, 0.4f), 4);
        Assert.Equal(0f, DiplomacyModel.HireChance(590, 1_000, 0.6f, 0f, 0f, false, 1f, 0.3f, 0.4f));
        // More than the sure price buys nothing more.
        Assert.Equal(1f, DiplomacyModel.HireChance(5_000, 1_000, 0.6f, 0f, 0f, false, 1f, 0.3f, 0.4f));
        // Heat 80 keeps 1 - 0.4 x 0.8 = 0.68; trust and reputation multiply.
        Assert.Equal(0.68f * 0.5f * 1.3f, DiplomacyModel.HireChance(1_000, 1_000, 0.6f, 1f, 80f, false, 0.5f, 0.3f, 0.4f), 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyModel.HireChance(1_000, 1_000, 0.6f, 0f, 0f, false, 1.5f, 0.3f, 0.4f));
    }

    [Fact]
    public void Hire_trust_falls_with_each_paid_hire_and_each_lost_ship_and_the_slider_starts_at_the_floor()
    {
        Assert.Equal(1f, DiplomacyModel.HireTrust(0, 0f, 0.15f, 0.5f));
        Assert.Equal(1f / 1.15f, DiplomacyModel.HireTrust(1, 0f, 0.15f, 0.5f), 4);
        Assert.Equal(1f / 1.6f / 1.5f, DiplomacyModel.HireTrust(4, 1f, 0.15f, 0.5f), 4);
        Assert.Equal(6, DiplomacyModel.HireMinStep(0.6f));
        Assert.Equal(7, DiplomacyModel.HireMinStep(0.65f));
        Assert.Equal(10, DiplomacyModel.HireMinStep(1f));
    }

    [Fact]
    public void Surrender_and_betrayal_chances_follow_their_inputs_and_stay_in_range()
    {
        var weak = new DiplomacyModel.SurrenderInputs(1f, 0.8f, 0f, 0f, 0, false, false, 0f);
        var strong = new DiplomacyModel.SurrenderInputs(-1f, 0f, 0f, 0f, 3, true, true, 100f);
        Assert.Equal(0.93f, DiplomacyModel.SurrenderChance(weak, 0.1f, 0.35f, 0.6f, 0.15f, 0.1f, 0.1f, 0.3f, 0.3f), 4);
        Assert.Equal(0f, DiplomacyModel.SurrenderChance(strong, 0.1f, 0.35f, 0.6f, 0.15f, 0.1f, 0.1f, 0.3f, 0.3f));
        float calm = DiplomacyModel.BetrayChance(0f, 0, 0f, false, 0.1f, 0.3f, 0.1f, -0.15f, -0.05f);
        float hot = DiplomacyModel.BetrayChance(100f, 3, 0f, false, 0.1f, 0.3f, 0.1f, -0.15f, -0.05f);
        Assert.True(hot > calm);
        Assert.False(DiplomacyModel.Succeeds(0f, 0f));
        Assert.True(DiplomacyModel.Succeeds(0.999f, 1f));
    }
}

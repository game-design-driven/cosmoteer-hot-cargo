using HotCargo.Diplomacy;
using Xunit;

namespace HotCargo.Tests;

public class RiskModelTests
{
    static readonly StanceThresholds Defaults = new(DemandAt: 2f, FleeAt: 0.6f, SurrenderAt: 0.3f);

    [Fact]
    public void Strength_drops_with_damage_and_lost_crew()
    {
        Assert.Equal(100f, RiskModel.Strength(100f, 1f, 1f, 0.5f), 3);
        Assert.Equal(50f, RiskModel.Strength(100f, 0.5f, 1f, 0.5f), 3);
        Assert.Equal(37.5f, RiskModel.Strength(100f, 0.5f, 0.5f, 0.5f), 3);
        Assert.Equal(0f, RiskModel.Strength(0f, 1f, 1f, 0.5f), 3);
    }

    [Fact]
    public void Odds_follow_the_strength_ratio_and_the_range_edge()
    {
        Assert.Equal(2f, RiskModel.Odds(200f, 100f, 1f), 3);
        Assert.Equal(float.PositiveInfinity, RiskModel.Odds(10f, 0f, 1f));
        Assert.Equal(0f, RiskModel.Odds(0f, 10f, 1f));
        Assert.True(RiskModel.RangeFactor(2000f, 1000f, 0.5f) > 1f);
        Assert.True(RiskModel.RangeFactor(500f, 1000f, 0.5f) < 1f);
        Assert.Equal(2f, RiskModel.RangeFactor(100000f, 10f, 0.5f), 3);
        Assert.Equal(1f, RiskModel.RangeFactor(0f, 1000f, 0.5f), 3);
    }

    [Fact]
    public void Bravery_spreads_around_the_group_base_and_stays_in_range()
    {
        Assert.Equal(0.5f, RiskModel.Bravery(0.5f, 0.25f, 0.5f), 3);
        Assert.Equal(0.25f, RiskModel.Bravery(0.5f, 0.25f, 0f), 3);
        Assert.Equal(0.75f, RiskModel.Bravery(0.5f, 0.25f, 1f), 3);
        Assert.Equal(1f, RiskModel.Bravery(0.9f, 0.25f, 1f), 3);
        Assert.Equal(0f, RiskModel.Bravery(0.1f, 0.25f, 0f), 3);
    }

    [Fact]
    public void Stance_follows_the_confidence()
    {
        Assert.Equal(Stance.Demand, RiskModel.Decide(2.5f, Defaults, proud: false));
        Assert.Equal(Stance.Fight, RiskModel.Decide(1f, Defaults, proud: false));
        Assert.Equal(Stance.Flee, RiskModel.Decide(0.5f, Defaults, proud: false));
        Assert.Equal(Stance.Surrender, RiskModel.Decide(0.2f, Defaults, proud: false));
        // A corporate hunter's pride: it flees where others surrender.
        Assert.Equal(Stance.Flee, RiskModel.Decide(0.2f, Defaults, proud: true));
    }

    [Fact]
    public void Playtest_bug_a_ship_at_half_strength_never_demands_from_six_ships()
    {
        // One ship at half health against six ships like it, at every bravery and range edge.
        float own = RiskModel.Strength(100f, 0.5f, 1f, 0.5f);
        float players = 6 * RiskModel.Strength(100f, 1f, 1f, 0.5f);
        for (float bravery = 0f; bravery <= 1f; bravery += 0.1f)
        {
            float odds = RiskModel.Odds(own, players, RiskModel.RangeFactor(1000000f, 1f, 0.5f));
            Assert.NotEqual(Stance.Demand, RiskModel.Decide(RiskModel.Confidence(odds, bravery), Defaults, proud: false));
        }
    }

    [Fact]
    public void A_much_stronger_ship_demands_and_a_beaten_one_surrenders()
    {
        Assert.Equal(Stance.Demand, RiskModel.Decide(RiskModel.Confidence(RiskModel.Odds(600f, 100f, 1f), 0.5f), Defaults, proud: false));
        Assert.Equal(Stance.Surrender, RiskModel.Decide(RiskModel.Confidence(RiskModel.Odds(50f, 600f, 1f), 0.5f), Defaults, proud: false));
    }

    [Fact]
    public void Allies_line_carries_the_counted_number_and_is_silent_without_allies()
    {
        Assert.Contains("6", RiskModel.AlliesLine("{0}", 6));
        Assert.Null(RiskModel.AlliesLine("{0}", 0));
    }
}

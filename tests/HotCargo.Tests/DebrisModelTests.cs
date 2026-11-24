using HotCargo.Debris;
using Xunit;

namespace HotCargo.Tests;

public class DebrisModelTests
{
    [Theory]
    // Spec 1832, at the defaults: base cut 0.6, component cut 0.85 from a base price of 1,000. Vanilla prices: steel 25, gold 500, processor 2,500.
    [InlineData(25, 0.6f)]
    [InlineData(500, 0.6f)]
    [InlineData(999, 0.6f)]
    [InlineData(1000, 0.85f)]
    [InlineData(2500, 0.85f)]
    public void Cheap_resources_get_the_base_cut_and_components_the_mild_cut(int buyPrice, float expected) =>
        Assert.Equal(expected, DebrisModel.SalvageFactor(buyPrice, 0.6f, 0.85f, 1000));

    [Fact]
    public void A_piece_waits_then_glows_for_the_last_glow_seconds_then_burns()
    {
        Assert.Equal(DebrisPhase.Waiting, DebrisModel.Phase(0, 240, 60));
        Assert.Equal(DebrisPhase.Waiting, DebrisModel.Phase(179.9, 240, 60));
        Assert.Equal(DebrisPhase.Glowing, DebrisModel.Phase(180, 240, 60));
        Assert.Equal(DebrisPhase.Glowing, DebrisModel.Phase(239.9, 240, 60));
        Assert.Equal(DebrisPhase.Burning, DebrisModel.Phase(240, 240, 60));
        Assert.Equal(DebrisPhase.Burning, DebrisModel.Phase(1e6, 240, 60));
    }

    [Fact]
    public void No_glow_time_means_no_glow()
    {
        Assert.Equal(DebrisPhase.Waiting, DebrisModel.Phase(239, 240, 0));
        Assert.Equal(DebrisPhase.Burning, DebrisModel.Phase(240, 240, 0));
        Assert.Equal(0f, DebrisModel.GlowLevel(239, 240, 0));
    }

    [Fact]
    public void Glow_level_rises_from_0_to_1_inside_the_glow_only()
    {
        Assert.Equal(0f, DebrisModel.GlowLevel(100, 240, 60));
        Assert.Equal(0f, DebrisModel.GlowLevel(180, 240, 60));
        Assert.Equal(0.5f, DebrisModel.GlowLevel(210, 240, 60), 5);
        Assert.True(DebrisModel.GlowLevel(239.9, 240, 60) > 0.99f);
        Assert.Equal(0f, DebrisModel.GlowLevel(240, 240, 60));
    }

    [Fact]
    public void Check_ticks_is_at_least_one()
    {
        Assert.Equal(60, DebrisModel.CheckTicks(2, 1.0 / 30));
        Assert.Equal(1, DebrisModel.CheckTicks(0.001, 1.0 / 30));
        Assert.Equal(1, DebrisModel.CheckTicks(2, 0));
    }
    [Fact]
    public void Scaled_quantity_keeps_the_mean_and_rounds_the_fraction_by_the_roll()
    {
        // One ore unit at 1.3: one more unit when the roll is below 0.3.
        Assert.Equal(2, DebrisModel.ScaleQuantity(1, 1.3f, 0.29f));
        Assert.Equal(1, DebrisModel.ScaleQuantity(1, 1.3f, 0.31f));
        Assert.Equal(12, DebrisModel.ScaleQuantity(10, 1.2f, 0.99f));
        Assert.Equal(0, DebrisModel.ScaleQuantity(0, 1.3f, 0f));
        double mean = Enumerable.Range(0, 1000).Average(i => DebrisModel.ScaleQuantity(3, 1.3f, (i + 0.5f) / 1000f));
        Assert.Equal(3.9, mean, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => DebrisModel.ScaleQuantity(1, -1f, 0f));
    }

    [Fact]
    public void Ore_factor_goes_from_the_mining_factor_to_the_laser_factor_by_the_salvage_share()
    {
        Assert.Equal(1.2f, DebrisModel.ByLaserShare(0f, 1.2f, 1.3f), 4);
        Assert.Equal(1.3f, DebrisModel.ByLaserShare(1f, 1.2f, 1.3f), 4);
        Assert.Equal(1.25f, DebrisModel.ByLaserShare(0.5f, 1.2f, 1.3f), 4);
        Assert.Equal(1.3f, DebrisModel.ByLaserShare(2f, 1.2f, 1.3f), 4);
    }

    [Theory]
    // Spec 1834: a laser break drops a sure 90 % of the laser units; the weapon units go on to the chance roll.
    [InlineData(10, 1f, 9, 0)]
    [InlineData(10, 0f, 0, 10)]
    [InlineData(10, 0.5f, 4, 5)]
    [InlineData(0, 1f, 0, 0)]
    public void A_laser_break_drops_a_sure_share_of_its_laser_units(int quantity, float salvageShare, int sure, int rolled) =>
        Assert.Equal((sure, rolled), DebrisModel.LaserSplit(quantity, salvageShare, 0.9f, 0.99f, 0.99f));

    [Fact]
    public void The_sure_share_rounds_to_the_right_mean()
    {
        // 1 unit at 90 %: the rounding roll makes it drop in 9 of 10 places.
        double mean = Enumerable.Range(0, 1000).Average(i => DebrisModel.LaserSplit(1, 1f, 0.9f, 0f, (i + 0.5f) / 1000f).Sure);
        Assert.Equal(0.9, mean, 2);
    }
}

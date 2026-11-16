using HotCargo.Debris;
using Xunit;

namespace HotCargo.Tests;

public class DebrisModelTests
{
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
        Assert.Equal(1.2f, DebrisModel.OreFactor(0f, 1.2f, 1.3f), 4);
        Assert.Equal(1.3f, DebrisModel.OreFactor(1f, 1.2f, 1.3f), 4);
        Assert.Equal(1.25f, DebrisModel.OreFactor(0.5f, 1.2f, 1.3f), 4);
        Assert.Equal(1.3f, DebrisModel.OreFactor(2f, 1.2f, 1.3f), 4);
    }
}

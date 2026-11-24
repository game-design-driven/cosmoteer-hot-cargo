using HotCargo.Core;
using Xunit;

namespace HotCargo.Tests;

/// <summary>Spec 1817: the shared factor of paid heat cuts.</summary>
public class HeatBuyModelTests
{
    [Fact]
    public void Each_recent_buy_weakens_the_next_one()
    {
        float[] factors = [.. Enumerable.Range(0, 4).Select(n => HeatModel.BuyFactor(n, heat: 80f, falloff: 0.5f, fullHeat: 60f))];
        Assert.Equal([1f, 2f / 3f, 0.5f, 0.4f], factors, (a, b) => MathF.Abs(a - b) < 1e-4f);
    }

    [Fact]
    public void A_cold_trail_cuts_less_and_a_hot_one_fully() =>
        Assert.Equal((0.5f, 1f), (HeatModel.BuyFactor(0f, 30f, 0.5f, 60f), HeatModel.BuyFactor(0f, 95f, 0.5f, 60f)));

    [Fact]
    public void The_cuts_close_below_the_threshold_from_the_same_factor()
    {
        Assert.True(HeatModel.BuyOpen(HeatModel.BuyFactor(2f, 30f, 0.5f, 60f), 0.25f));
        Assert.False(HeatModel.BuyOpen(HeatModel.BuyFactor(3f, 30f, 0.5f, 60f), 0.25f));
    }

    [Fact]
    public void Recent_buys_halve_every_half_life() =>
        Assert.Equal(1.5f, HeatModel.BuyDecay(3f, seconds: 20 * 60, halfLifeMinutes: 20f), 4);

    [Fact]
    public void Bad_input_fails_loudly() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HeatModel.BuyFactor(-1f, 50f, 0.5f, 60f));
}

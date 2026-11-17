using HotCargo.Events;
using Xunit;

namespace HotCargo.Tests;

public class OffBooksModelTests
{
    static float Price(float heat, int hunters, float rep) => OffBooksModel.PriceShare(heat, hunters, rep, 0.85f, -0.15f, -0.05f, 0.1f, 0.5f, 0.95f);

    [Fact]
    public void Price_falls_with_heat_and_hunters_and_rises_with_standing()
    {
        Assert.Equal(0.85f, Price(0f, 0, 0f), 3);
        Assert.Equal(0.70f, Price(100f, 0, 0f), 3);
        Assert.Equal(0.75f, Price(0f, 2, 0f), 3);
        Assert.Equal(0.95f, Price(0f, 0, 1f), 3);
    }

    [Fact]
    public void Price_stays_between_its_bounds() => Assert.Equal(0.5f, Price(100f, 10, -1f), 3);

    [Fact]
    public void Risk_per_crate_rises_with_heat_falls_with_standing_and_never_goes_below_zero()
    {
        Assert.Equal(0.14f, OffBooksModel.RiskPerCrate(100f, 0f, 0.04f, 0.1f, 0.04f), 3);
        Assert.Equal(0.0f, OffBooksModel.RiskPerCrate(0f, 1f, 0.04f, 0.1f, 0.1f), 3);
    }

    [Fact]
    public void Sting_chance_grows_with_crates_and_corporate_ships_up_to_the_cap()
    {
        Assert.Equal(0.3f, OffBooksModel.StingChance(0.1f, 3, 0, 0.15f, 0.9f), 3);
        Assert.Equal(0.6f, OffBooksModel.StingChance(0.1f, 3, 2, 0.15f, 0.9f), 3);
        Assert.Equal(0.9f, OffBooksModel.StingChance(0.5f, 3, 5, 0.15f, 0.9f), 3);
    }
}

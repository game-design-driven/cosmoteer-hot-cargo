using HotCargo.Storyteller;
using Xunit;

namespace HotCargo.Tests;

public class StorytellerModelTests
{
    static readonly PressureCurve Curve = new(AdaptMin: -30f, AdaptMax: 60f, PressureMin: 0.85f, PressureMax: 1.25f);

    [Fact]
    public void The_curve_meets_its_points_and_stays_in_the_band()
    {
        Assert.Equal(0.85f, StorytellerModel.Pressure(-30f, 1f, Curve), 4);
        Assert.Equal(1f, StorytellerModel.Pressure(0f, 1f, Curve), 4);
        Assert.Equal(1.25f, StorytellerModel.Pressure(60f, 1f, Curve), 4);
        Assert.Equal(1.125f, StorytellerModel.Pressure(30f, 1f, Curve), 4);
        Assert.Equal(1.25f, StorytellerModel.Pressure(500f, 1f, Curve), 4);
        Assert.Equal(0.85f, StorytellerModel.Pressure(-500f, 1f, Curve), 4);
    }

    [Fact]
    public void Impact_moves_the_pressure_toward_one()
    {
        Assert.Equal(1.125f, StorytellerModel.Pressure(60f, 0.5f, Curve), 4);
        Assert.Equal(0.925f, StorytellerModel.Pressure(-30f, 0.5f, Curve), 4);
        Assert.Equal(1f, StorytellerModel.Pressure(60f, 0f, Curve), 4);
    }

    [Fact]
    public void Time_raises_the_minutes_but_not_above_zero_in_sector_one()
    {
        Assert.Equal(10f, StorytellerModel.Next(0f, 10, 0, 0, sectorOne: false, 1f, 20f, 1f, Curve), 4);
        Assert.Equal(0f, StorytellerModel.Next(0f, 10, 0, 0, sectorOne: true, 1f, 20f, 1f, Curve), 4);
        Assert.Equal(-5f, StorytellerModel.Next(-15f, 10, 0, 0, sectorOne: true, 1f, 20f, 1f, Curve), 4);
        Assert.Equal(60f, StorytellerModel.Next(55f, 100, 0, 0, sectorOne: false, 1f, 20f, 1f, Curve), 4);
    }

    [Fact]
    public void Losses_take_minutes_at_once_and_stop_at_the_low_end()
    {
        Assert.Equal(-20f, StorytellerModel.Next(0f, 0, 1, 0, sectorOne: false, 1f, 20f, 1f, Curve), 4);
        Assert.Equal(25f, StorytellerModel.Next(30f, 0, 0, 5, sectorOne: false, 1f, 20f, 1f, Curve), 4);
        Assert.Equal(-30f, StorytellerModel.Next(-20f, 0, 3, 10, sectorOne: false, 1f, 20f, 1f, Curve), 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => StorytellerModel.Next(0f, -1, 0, 0, false, 1f, 20f, 1f, Curve));
    }

    [Fact]
    public void Fleet_and_pressure_together_stay_in_their_clamp()
    {
        Assert.Equal(1.4f, StorytellerModel.Combined(1.15f, 1.25f, 0.75f, 1.4f), 4);
        Assert.Equal(0.75f, StorytellerModel.Combined(0.85f, 0.85f, 0.75f, 1.4f), 4);
        Assert.Equal(1.1f, StorytellerModel.Combined(1.1f, 1f, 0.75f, 1.4f), 4);
    }
}

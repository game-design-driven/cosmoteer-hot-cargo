using HotCargo.Autonomy;
using HotCargo.ShipAi;
using Xunit;

namespace HotCargo.Tests;

public class BuildModelTests
{
    [Fact]
    public void Design_slots_round_trip_in_the_cargo_text_and_text_without_build_has_empty_slots()
    {
        var cargo = CargoSettings.Default with { Designs = [null, new BuildDesign("Drone", "QUJD"), null] };
        string text = AutonomyModel.Encode(cargo);
        Assert.EndsWith("|build=,Drone:QUJD", text);
        Assert.Equal(cargo, AutonomyModel.Decode(text, _ => true));
        Assert.All(AutonomyModel.Decode("HCC1:loot=v0|sell=|mine=|buy=", _ => true).Designs, Assert.Null);
        Assert.Equal("", AutonomyModel.Encode(CargoSettings.Default));
    }

    [Theory]
    [InlineData("Drone:QUJD,a:QUJD,b:QUJD,c:QUJD")] // too many slots
    [InlineData("Drone")]                           // no data
    [InlineData("Drone:not base64!")]
    [InlineData(" Drone:QUJD")]                     // the name is not clean
    public void Bad_design_text_is_refused(string build) =>
        Assert.Throws<FormatException>(() => BuildModel.Decode(build));

    [Fact]
    public void Build_and_refit_are_trade_rows()
    {
        Assert.Equal(ActKind.Trade, ShipAiModel.KindOf(Act.BuildShip));
        Assert.Equal(ActKind.Trade, ShipAiModel.KindOf(Act.RefitShip));
    }

    [Fact]
    public void Affords_needs_every_resource_type_of_the_cost()
    {
        var cost = new Dictionary<string, int> { ["steel"] = 40, ["coil"] = 10 };
        Assert.True(BuildModel.Affords(cost, t => t == "steel" ? 40 : 10));
        Assert.False(BuildModel.Affords(cost, t => t == "steel" ? 40 : 9));
    }
}

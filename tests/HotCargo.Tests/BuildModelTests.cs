using HotCargo.Autonomy;
using HotCargo.ShipAi;
using Xunit;

namespace HotCargo.Tests;

public class BuildModelTests
{
    static readonly ShipFacts Calm = new(1f, null, new HashSet<PartKind>(), float.PositiveInfinity, false, false, null, null, new HashSet<ShipClass>(), 0, false, false);

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
    public void Fewer_of_design_reads_slot_x_count_and_refuses_other_text()
    {
        var rows = ShipAiModel.Decode("HC1:CanAffordDesign=1&FewerOfDesign=1x3>BuildShip=1");
        Assert.Equal(BuildModel.Pack(1, 3), rows[0].Terms[1].Param);
        Assert.Equal("HC1:CanAffordDesign=1&FewerOfDesign=1x3>BuildShip=1", ShipAiModel.Encode(rows));
        Assert.Throws<FormatException>(() => ShipAiModel.Decode("HC1:FewerOfDesign=103>BuildShip=1"));
        Assert.Throws<FormatException>(() => ShipAiModel.Decode("HC1:FewerOfDesign=4x1>BuildShip=1"));
        Assert.Throws<FormatException>(() => ShipAiModel.Decode("HC1:Always>RefitShip=4"));
    }

    [Fact]
    public void A_row_that_names_an_empty_slot_is_an_error()
    {
        var rows = ShipAiModel.Decode("HC1:CanAffordRefit=2>RefitShip=2");
        Assert.Null(ShipAiModel.DesignError(rows, slot => slot == 2));
        Assert.NotNull(ShipAiModel.DesignError(rows, slot => slot == 1));
        Assert.NotNull(ShipAiModel.DesignError(ShipAiModel.Decode("HC1:FewerOfDesign=3x1>Sell"), slot => slot != 3));
    }

    [Fact]
    public void Design_conditions_read_the_fact_of_their_slot()
    {
        var f = Calm with { Affordable = [false, true, false], AffordableRefit = [true, false, false], DesignShips = [0, 2, 0] };
        Assert.True(ShipAiModel.Holds(new Term(Cond.CanAffordDesign, 2), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.CanAffordDesign, 1), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.CanAffordRefit, 1), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.FewerOfDesign, BuildModel.Pack(2, 3)), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.FewerOfDesign, BuildModel.Pack(2, 2)), f));
        // No facts for a design condition is a bug, not a false condition.
        Assert.Throws<InvalidOperationException>(() => ShipAiModel.Holds(new Term(Cond.CanAffordDesign, 1), Calm));
    }

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

using HotCargo.Thieves;
using Xunit;

namespace HotCargo.Tests;

public class ThievesModelTests
{
    [Fact]
    public void Storage_with_cargo_ranks_first_and_command_and_reactors_never()
    {
        Assert.Equal(RobRank.FullStorage, ThievesModel.Rank(storage: true, holdsResources: true, command: false, reactor: false));
        Assert.Equal(RobRank.Storage, ThievesModel.Rank(true, false, false, false));
        Assert.Equal(RobRank.Other, ThievesModel.Rank(false, false, false, false));
        Assert.Equal(RobRank.Never, ThievesModel.Rank(false, false, command: true, reactor: false));
        Assert.Equal(RobRank.Never, ThievesModel.Rank(false, false, false, reactor: true));
    }

    [Fact]
    public void Pick_takes_the_lowest_rank_before_the_nearest_part()
    {
        var parts = new[] { (RobRank.Other, 10f), (RobRank.FullStorage, 500f), (RobRank.Storage, 20f) };
        Assert.Equal(1, ThievesModel.Pick(parts, current: -1, switchFactor: 2f));
    }

    [Fact]
    public void Pick_keeps_the_current_part_unless_another_of_its_rank_is_much_nearer()
    {
        var parts = new[] { (RobRank.Storage, 100f), (RobRank.Storage, 60f) };
        Assert.Equal(0, ThievesModel.Pick(parts, current: 0, switchFactor: 2f));
        Assert.Equal(1, ThievesModel.Pick(new[] { (RobRank.Storage, 100f), (RobRank.Storage, 40f) }, current: 0, switchFactor: 2f));
    }

    [Fact]
    public void Pick_is_none_when_only_protected_parts_are_left() =>
        Assert.Equal(-1, ThievesModel.Pick(new[] { (RobRank.Never, 1f) }, -1, 2f));

    [Fact]
    public void Loot_value_counts_only_player_drops_within_the_radius()
    {
        var loose = new[]
        {
            new Loose(FromPlayer: true, Distance: 100f, Units: 10, UnitValue: 25),
            new Loose(true, 900f, 4, 100),
            new Loose(false, 50f, 100, 25),
            new Loose(true, 2000f, 100, 25),
        };
        Assert.Equal(10 * 25 + 4 * 100, ThievesModel.LootValue(loose, radius: 1000f));
    }

    [Fact]
    public void Threshold_is_the_share_of_the_tier_value_rounded_up() =>
        Assert.Equal(3001, ThievesModel.Threshold(0.05f, 60_020));
}

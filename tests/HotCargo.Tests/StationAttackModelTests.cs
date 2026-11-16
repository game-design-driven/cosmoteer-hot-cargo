using System.Numerics;
using HotCargo.Events;
using Xunit;

namespace HotCargo.Tests;

public class StationAttackModelTests
{
    // A raider at the origin with radius 10; the station 500 to its right.
    static readonly Vector2 Raider = new(0, 0), Station = new(500, 0);

    static bool InTheWay(Vector2 player, float radius = 10f) => StationAttackModel.InTheWay(Raider, 10f, Station, player, radius, 150f);

    [Fact]
    public void A_player_within_range_of_the_raider_hull_is_in_the_way() => Assert.True(InTheWay(new Vector2(0, -160)));

    [Fact]
    public void A_player_on_the_line_of_fire_is_in_the_way_beyond_the_range() => Assert.True(InTheWay(new Vector2(400, 5)));

    [Fact]
    public void A_player_off_the_line_and_out_of_range_is_not_in_the_way() => Assert.False(InTheWay(new Vector2(400, 60)));

    [Fact]
    public void A_player_behind_the_station_is_not_on_the_line_of_fire() => Assert.False(InTheWay(new Vector2(700, 0)));

    [Fact]
    public void The_attack_ends_repelled_when_no_raider_is_left() =>
        Assert.Equal(AttackEnd.Repelled, StationAttackModel.End(0, stationExists: true, 0.1f, 0.8f, seconds: 999, maxSeconds: 240));

    [Fact]
    public void The_station_falls_below_the_fall_health_or_when_it_is_gone()
    {
        Assert.Equal(AttackEnd.Fell, StationAttackModel.End(2, true, 0.79f, 0.8f, 10, 240));
        Assert.Equal(AttackEnd.Fell, StationAttackModel.End(2, false, 1f, 0.8f, 10, 240));
    }

    [Fact]
    public void The_station_holds_when_the_time_is_over_and_the_attack_goes_on_before()
    {
        Assert.Equal(AttackEnd.Held, StationAttackModel.End(2, true, 0.9f, 0.8f, 240, 240));
        Assert.Equal(AttackEnd.None, StationAttackModel.End(2, true, 0.9f, 0.8f, 239, 240));
    }

    [Fact]
    public void A_rebuild_step_rebuilds_per_debris_part_and_never_more_than_is_destroyed()
    {
        Assert.Equal(6, StationAttackModel.RebuildCount(3, 2f, 50));
        Assert.Equal(4, StationAttackModel.RebuildCount(3, 2f, 4));
        Assert.Equal(2, StationAttackModel.RebuildCount(3, 0.5f, 50));
        Assert.Equal(0, StationAttackModel.RebuildCount(0, 2f, 50));
    }
}

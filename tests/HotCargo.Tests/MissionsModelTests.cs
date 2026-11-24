using System.Numerics;
using HotCargo.Missions;
using Xunit;

namespace HotCargo.Tests;

public class ConvoyRouteTests
{
    static readonly Vector2[] SunAtCenter = [Vector2.Zero];

    [Fact]
    public void Destination_keeps_the_preferred_direction_when_it_fits()
    {
        var dest = MissionModel.ConvoyDestination(new(8000, 0), new(0, 1), 9000, 14000, 6000, SunAtCenter);
        Assert.NotNull(dest);
        Assert.Equal(new Vector2(8000, 9000), dest.Value, new Vec(1f));
    }

    [Fact]
    public void Destination_turns_until_the_route_is_inside_the_reach_and_clear_of_suns()
    {
        var start = new Vector2(11000, 0);
        // Straight at the sun first: the route crosses it and the destination lies outside the reach.
        var dest = MissionModel.ConvoyDestination(start, new(-1, 0), 9000, 13500, 6000, SunAtCenter);
        Assert.NotNull(dest);
        Assert.Equal(9000, Vector2.Distance(start, dest.Value), 1);
        Assert.True(dest.Value.Length() <= 13500);
        Assert.True(MissionModel.SegmentDistance(start, dest.Value, Vector2.Zero) >= 6000);
    }

    [Fact]
    public void Destination_is_null_when_no_direction_fits() =>
        Assert.Null(MissionModel.ConvoyDestination(new(8000, 0), new(1, 0), 30000, 14000, 6000, SunAtCenter));

    [Fact]
    public void Route_progress_is_the_covered_share_and_never_negative()
    {
        Assert.Equal(0.25f, MissionModel.RouteProgress(8000, 6000), 4);
        Assert.Equal(0f, MissionModel.RouteProgress(8000, 9000));
    }

    [Fact]
    public void Meet_spot_is_the_gap_from_the_player_hull_on_the_escort_side()
    {
        // Player radius 100, escort radius 50, gap 300: the escort stops 450 from the player center, toward where it is now.
        var spot = MissionModel.MeetSpot(new(1000, 0), 100, 2000, new(5000, 0), 50, 300, 50, new(5000, 0), 10000);
        Assert.Equal(new Vector2(1450, 0), spot, new Vec(0.01f));
    }

    [Fact]
    public void Meet_spot_comes_closer_when_the_player_comms_reach_is_short()
    {
        // Comms 300 from the center, hull radius 100, margin 50: the gap shrinks from 300 to 150.
        var spot = MissionModel.MeetSpot(new(0, 0), 100, 300, new(5000, 0), 50, 300, 50, new(5000, 0), 10000);
        Assert.Equal(new Vector2(300, 0), spot, new Vec(0.01f));
    }

    [Fact]
    public void Meet_spot_stays_within_the_meet_range_of_the_start()
    {
        var spot = MissionModel.MeetSpot(new(-9000, 0), 100, 2000, new(0, 0), 50, 300, 50, new(0, 0), 2500);
        Assert.Equal(new Vector2(-2500, 0), spot, new Vec(0.01f));
    }

    [Fact]
    public void Route_clear_checks_every_sun()
    {
        Assert.True(MissionModel.RouteClear(new(-5000, 5000), new(5000, 5000), 4000, SunAtCenter));
        Assert.False(MissionModel.RouteClear(new(-5000, 5000), new(5000, 5000), 6000, SunAtCenter));
    }

    sealed class Vec(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector2 v) => 0;
    }
}

public class EscortGuardTests
{
    static EscortGuardEvent Step(ref double exposed, ref bool raided, double seconds, bool guarded, bool live) =>
        MissionModel.GuardStep(ref exposed, ref raided, seconds, guarded, live, raidSeconds: 60f, lostSeconds: 150f);

    [Fact]
    public void A_live_escort_alone_draws_one_raid_then_never_a_loss()
    {
        double exposed = 0;
        bool raided = false;
        Assert.Equal(EscortGuardEvent.None, Step(ref exposed, ref raided, 59, guarded: false, live: true));
        Assert.Equal(EscortGuardEvent.Raid, Step(ref exposed, ref raided, 1, guarded: false, live: true));
        Assert.Equal(EscortGuardEvent.None, Step(ref exposed, ref raided, 200, guarded: false, live: true));
    }

    [Fact]
    public void An_escort_in_stasis_is_lost_at_the_lost_time_and_raided_only_when_live_again()
    {
        double exposed = 0;
        bool raided = false;
        Assert.Equal(EscortGuardEvent.None, Step(ref exposed, ref raided, 100, guarded: false, live: false));
        Assert.Equal(EscortGuardEvent.Raid, Step(ref exposed, ref raided, 1, guarded: false, live: true));
        Assert.Equal(EscortGuardEvent.Lost, Step(ref exposed, ref raided, 49, guarded: false, live: false));
    }

    [Fact]
    public void A_guard_resets_the_clock_and_allows_a_new_raid()
    {
        double exposed = 0;
        bool raided = false;
        Step(ref exposed, ref raided, 60, guarded: false, live: true);
        Assert.Equal(EscortGuardEvent.None, Step(ref exposed, ref raided, 1, guarded: true, live: true));
        Assert.Equal(0, exposed);
        Assert.False(raided);
        Assert.Equal(EscortGuardEvent.Raid, Step(ref exposed, ref raided, 60, guarded: false, live: true));
    }
}


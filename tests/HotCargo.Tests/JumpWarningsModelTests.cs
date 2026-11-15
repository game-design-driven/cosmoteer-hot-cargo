using HotCargo.Ux;
using Xunit;

namespace HotCargo.Tests;

public class JumpWarningsModelTests
{
    static readonly JumpFacts Ok = new(HasDrive: true, CanCommand: true, Cost: 30, Capacity: 40, Fuel: 40, Ordered: true, Ready: false);

    [Fact]
    public void A_ready_ship_jumps() => Assert.Equal(StayReason.None, JumpWarningsModel.Reason(Ok with { Ready = true }));

    [Fact]
    public void A_cost_above_what_the_drives_hold_is_too_big_not_a_fuel_problem()
    {
        // Playtest: the mothership with one drive showed NEED FUEL, but no amount of fuel fits in its drive.
        Assert.Equal(StayReason.TooBig, JumpWarningsModel.Reason(Ok with { Cost = 120, Capacity = 80, Fuel = 10 }));
        Assert.Equal(StayReason.NeedFuel, JumpWarningsModel.Reason(Ok with { Cost = 80, Capacity = 80, Fuel = 79 }));
    }

    [Fact]
    public void The_reason_is_the_first_one_the_player_must_fix()
    {
        var all = Ok with { HasDrive = false, CanCommand = false, Cost = 99, Fuel = 0, Ordered = false };
        Assert.Equal(StayReason.NoDrive, JumpWarningsModel.Reason(all));
        Assert.Equal(StayReason.NoControl, JumpWarningsModel.Reason(all with { HasDrive = true }));
        Assert.Equal(StayReason.TooBig, JumpWarningsModel.Reason(all with { HasDrive = true, CanCommand = true }));
        Assert.Equal(StayReason.NeedFuel, JumpWarningsModel.Reason(all with { HasDrive = true, CanCommand = true, Cost = 40 }));
        Assert.Equal(StayReason.NotOrdered, JumpWarningsModel.Reason(Ok with { Ordered = false }));
        Assert.Equal(StayReason.SpinningUp, JumpWarningsModel.Reason(Ok));
    }

    [Fact]
    public void No_drive_is_a_warning_only_once_the_fleet_has_a_drive()
    {
        Assert.Equal(StayReason.None, JumpWarningsModel.Standing(false, true, null, 0, fleetHasDrive: false));
        Assert.Equal(StayReason.NoDrive, JumpWarningsModel.Standing(false, true, null, 0, fleetHasDrive: true));
    }

    [Fact]
    public void Too_big_before_a_plan_compares_the_cheapest_jump_from_here()
    {
        Assert.Equal(StayReason.TooBig, JumpWarningsModel.Standing(true, true, 81, 80, true));
        Assert.Equal(StayReason.None, JumpWarningsModel.Standing(true, true, 80, 80, true));
        // No reachable system: nothing to compare, so no warning.
        Assert.Equal(StayReason.None, JumpWarningsModel.Standing(true, true, null, 0, true));
        Assert.Equal(StayReason.NoControl, JumpWarningsModel.Standing(true, false, 81, 80, true));
    }

    [Fact]
    public void A_ship_joins_once_and_a_new_sim_only_takes_the_baseline()
    {
        var watch = new JoinWatch<string>();
        object sim = new(), next = new();
        Assert.Empty(watch.Joined(sim, ["a", "b"]));
        Assert.Equal(["c"], watch.Joined(sim, ["a", "b", "c"]));
        // A ship that leaves and comes back in the same sim is not told again.
        Assert.Empty(watch.Joined(sim, ["a"]));
        Assert.Empty(watch.Joined(sim, ["a", "c"]));
        // A jump arrival or a load replaces the sim.
        Assert.Empty(watch.Joined(next, ["a", "d"]));
        Assert.Equal(["e"], watch.Joined(next, ["a", "d", "e"]));
    }

    [Fact]
    public void With_no_ship_here_the_players_go_back_to_the_biggest_waiting_fleet()
    {
        // Playtest: one captured ship jumped and died; the mothership and three more waited in Vintecco.
        (string, int)[] waiting = [("Arden", 1), ("Vintecco", 4), ("Kesh", 4)];
        Assert.Equal("Vintecco", JumpWarningsModel.RejoinTarget(0, false, waiting));
        Assert.Null(JumpWarningsModel.RejoinTarget(1, false, waiting));
        // During a jump the ships are in limbo, so "no ship here" is not certain yet.
        Assert.Null(JumpWarningsModel.RejoinTarget(0, true, waiting));
        Assert.Null(JumpWarningsModel.RejoinTarget(0, false, [("Arden", 0)]));
    }
}

using HotCargo.Ai;
using Xunit;

namespace HotCargo.Tests;

public class CounterKiteModelTests
{
    [Fact]
    public void Outranged_only_when_the_kiter_reaches_and_the_ship_does_not()
    {
        Assert.True(CounterKiteModel.Outranged(edge: 1200f, ownRange: 800f, kiterRange: 1500f));
        Assert.False(CounterKiteModel.Outranged(edge: 700f, ownRange: 800f, kiterRange: 1500f));
        Assert.False(CounterKiteModel.Outranged(edge: 1600f, ownRange: 800f, kiterRange: 1500f));
        Assert.False(CounterKiteModel.Outranged(edge: 60f, ownRange: 0f, kiterRange: 260f));
    }

    [Fact]
    public void A_weapon_without_a_range_limit_reaches_the_radar_radius()
    {
        Assert.Equal(700f, CounterKiteModel.Reach(700f, 400f));
        Assert.Equal(2000f, CounterKiteModel.Reach(float.PositiveInfinity, 2000f));
        Assert.Throws<ArgumentOutOfRangeException>(() => CounterKiteModel.Reach(700f, float.PositiveInfinity));
    }

    [Fact]
    public void Kited_needs_the_whole_window_and_the_loss()
    {
        Assert.True(CounterKiteModel.Kited(20, 0.03f, 20f, 0.03f));
        Assert.False(CounterKiteModel.Kited(19.9, 0.5f, 20f, 0.03f));
        Assert.False(CounterKiteModel.Kited(30, 0.02f, 20f, 0.03f));
    }

    [Fact]
    public void The_answer_follows_traits_then_build()
    {
        var brawl = TraitSet.None.With(Trait.Brawl);
        var callAndTeam = TraitSet.None.With(Trait.Call).With(Trait.Team);
        Assert.Equal(KiteAnswer.CloseIn, CounterKiteModel.Pick(brawl.With(Trait.Call), true, agility: 0.6f, closeInAgility: 0.5f));
        // A slow brawler does not close in: it has no other answer, so it breaks off.
        Assert.Equal(KiteAnswer.BreakOff, CounterKiteModel.Pick(brawl, true, 0.2f, 0.5f));
        Assert.Equal(KiteAnswer.Call, CounterKiteModel.Pick(callAndTeam, true, 0.9f, 0.5f));
        Assert.Equal(KiteAnswer.Team, CounterKiteModel.Pick(TraitSet.None.With(Trait.Team), true, 0.9f, 0.5f));
        Assert.Equal(KiteAnswer.BreakOff, CounterKiteModel.Pick(TraitSet.None.With(Trait.Cover), true, 0.9f, 0.5f));
        Assert.Equal(KiteAnswer.CloseIn, CounterKiteModel.Pick(TraitSet.None, false, 0.5f, 0.5f));
        Assert.Equal(KiteAnswer.BreakOff, CounterKiteModel.Pick(callAndTeam, false, 0.4f, 0.5f));
    }

    [Fact]
    public void A_close_in_fails_only_after_its_time_without_enough_gain()
    {
        Assert.False(CounterKiteModel.CloseInFailed(10, 1000f, 1000f, 15f, 0.2f));
        Assert.True(CounterKiteModel.CloseInFailed(15, 1000f, 850f, 15f, 0.2f));
        Assert.False(CounterKiteModel.CloseInFailed(15, 1000f, 800f, 15f, 0.2f));
    }

    [Fact]
    public void The_anvil_takes_the_far_side_of_the_nebula_then_of_the_others()
    {
        // Nebula to the west of the kiter: the attacker sits east (angle 0), slots spread around it.
        Assert.Equal(0f, CounterKiteModel.AnvilAngle((0f, 0f), (-1000f, 0f), (0f, 500f), 0, 2, 45f)!.Value, 4);
        Assert.Equal(MathF.PI / 4f, CounterKiteModel.AnvilAngle((0f, 0f), (-1000f, 0f), null, 1, 2, 45f)!.Value, 4);
        // No nebula: opposite the other attackers (they are north, so south).
        Assert.Equal(-MathF.PI / 2f, CounterKiteModel.AnvilAngle((0f, 0f), null, (0f, 500f), 0, 2, 45f)!.Value, 4);
        Assert.Null(CounterKiteModel.AnvilAngle((0f, 0f), null, null, 0, 1, 45f));
    }
}

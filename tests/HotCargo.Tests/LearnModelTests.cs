using HotCargo.Ai;
using Xunit;

namespace HotCargo.Tests;

public class LearnModelTests
{
    [Fact]
    public void A_part_focus_needs_enough_loss_and_one_kind_with_the_share()
    {
        Assert.Equal(FocusKind.Thrusters, LearnModel.Focus([100f, 300f, 50f], 4000f, 0.5f, 0.05f));
        // Spread loss: no kind has half.
        Assert.Null(LearnModel.Focus([200f, 200f, 100f], 4000f, 0.5f, 0.05f));
        // Too little loss in all (below 5 % of 4000).
        Assert.Null(LearnModel.Focus([150f, 0f, 0f], 4000f, 0.5f, 0.05f));
        Assert.Throws<ArgumentException>(() => LearnModel.Focus([1f, 2f], 100f, 0.5f, 0.05f));
    }

    [Fact]
    public void A_newer_report_adds_habits_and_replaces_the_focus()
    {
        var old = new Habits(Kite: true, Cloud: false, Focus: FocusKind.Weapons);
        Assert.Equal(new Habits(true, true, FocusKind.Weapons), old.Merge(new Habits(false, true, null)));
        Assert.Equal(new Habits(true, false, FocusKind.Core), old.Merge(new Habits(false, false, FocusKind.Core)));
        Assert.False(default(Habits).Any);
    }

    [Fact]
    public void A_report_fades_after_its_jumps()
    {
        Assert.False(LearnModel.Faded(1, 2));
        Assert.True(LearnModel.Faded(2, 2));
    }

    [Fact]
    public void The_edge_is_the_first_point_outside()
    {
        Assert.Equal(600f, LearnModel.EdgeDistance(d => d < 500f, 2000f, 200f));
        Assert.Null(LearnModel.EdgeDistance(_ => true, 1000f, 200f));
    }
}

using HotCargo.Cut;
using Xunit;

namespace HotCargo.Tests;

public class EditEndModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_case_has_an_expected_outcome(bool pulls)
    {
        // A new enum value without an outcome throws here, before the game test runs.
        foreach (var c in EditEndModel.Cases(pulls)) EditEndModel.Expected(c);
    }

    [Fact]
    public void Only_blueprint_mode_has_pending_orders_and_only_a_weld_pulls()
    {
        Assert.DoesNotContain(EditEndModel.Cases(pulls: true), c => c.Mode == EditMode.Live && c.Stage == EditStage.Pending);
        Assert.DoesNotContain(EditEndModel.Cases(pulls: false), c => c.Stage == EditStage.Pull);
        Assert.Contains(EditEndModel.Cases(pulls: true), c => c.Mode == EditMode.Blueprint && c.Stage == EditStage.Pull);
    }

    [Theory]
    [InlineData(EditMode.Live)]
    [InlineData(EditMode.Blueprint)]
    public void Every_running_stage_ends_with_a_button_in_both_modes(EditMode mode)
    {
        // Issue #393: in blueprint mode, a running weld had no button that ended it.
        var running = EditEndModel.Cases(pulls: true).Where(c => c.Mode == mode && c.Stage != EditStage.Pending).Select(c => c.Stage).Distinct();
        foreach (var stage in running)
            Assert.Contains(EditEndModel.Cases(pulls: true), c => c.Mode == mode && c.Stage == stage
                && c.Control is EditControl.CancelAll or EditControl.DiscardChanges && EditEndModel.Expected(c) == EditOutcome.Ends);
    }
}

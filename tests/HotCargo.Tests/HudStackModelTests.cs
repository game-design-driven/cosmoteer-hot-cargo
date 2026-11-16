using HotCargo.Ux;
using Xunit;

namespace HotCargo.Tests;

public class HudStackModelTests
{
    static readonly HudBox Screen = new(0, 0, 1280, 720);

    [Fact]
    public void Without_panels_the_widget_sits_a_gap_above_the_bottom()
    {
        Assert.Equal(720 - 8 - 36, HudStackModel.StackTop(Screen, 15, 260, 36, 8, []));
    }

    [Fact]
    public void The_widget_goes_above_every_panel_in_its_column()
    {
        // The ships card at the bottom and the tool buttons on it, as with a ship selected.
        HudBox card = new(10, 610, 350, 100), tools = new(15, 556, 230, 44);
        float top = HudStackModel.StackTop(Screen, 15, 260, 36, 8, [card, tools]);
        Assert.Equal(556 - 8 - 36, top);
        Assert.True(HudStackModel.Visible(new HudBox(15, top, 260, 36), Screen, [card, tools]));
    }

    [Fact]
    public void A_panel_outside_the_column_or_above_the_widget_does_not_move_it()
    {
        HudBox minimap = new(1100, 540, 170, 170), topPanel = new(10, 40, 290, 120);
        Assert.Equal(720 - 8 - 36, HudStackModel.StackTop(Screen, 15, 260, 36, 8, [minimap, topPanel]));
    }

    [Fact]
    public void Visible_is_false_off_the_screen_or_under_a_panel()
    {
        // The playtest bug: the switch lost its bottom anchor and sat above the top edge.
        Assert.False(HudStackModel.Visible(new HudBox(255, -231, 260, 36), Screen, []));
        Assert.False(HudStackModel.Visible(new HudBox(15, 600, 260, 36), Screen, [new HudBox(10, 610, 350, 100)]));
        // Touching edges is not an overlap.
        Assert.True(HudStackModel.Visible(new HudBox(15, 574, 260, 36), Screen, [new HudBox(10, 610, 350, 100)]));
    }
}

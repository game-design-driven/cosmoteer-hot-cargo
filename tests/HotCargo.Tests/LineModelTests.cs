using HotCargo.Journey;
using Xunit;

namespace HotCargo.Tests;

public class LineModelTests
{
    const double First = 300, Last = 60;

    [Fact]
    public void Behind_the_furthest_band_is_closed()
    {
        Assert.True(LineModel.Behind(1, 2));
        Assert.False(LineModel.Behind(2, 2));
        Assert.False(LineModel.Behind(3, 2));
    }

    [Fact]
    public void All_choices_stay_open_at_no_heat_and_two_at_full_heat()
    {
        // 5 choices: 3 may close, at heat 33.3, 66.7 and 100.
        Assert.Equal(3, LineModel.Closable(5));
        Assert.Equal(100f / 3, LineModel.Threshold(0, 5)!.Value, 3);
        Assert.Equal(200f / 3, LineModel.Threshold(1, 5)!.Value, 3);
        Assert.Equal(100f, LineModel.Threshold(2, 5)!.Value, 3);
        Assert.Null(LineModel.Threshold(3, 5));
        // Two or fewer choices never close.
        Assert.Equal(0, LineModel.Closable(2));
        Assert.Null(LineModel.Threshold(0, 2));
        Assert.Null(LineModel.Threshold(0, 1));
        // Every closable rank closes at or below heat 100, so heat 100 leaves exactly two.
        for (int choices = 3; choices <= 8; choices++)
            Assert.Equal(choices - 2, Enumerable.Range(0, choices).Count(r => LineModel.Threshold(r, choices) is <= LineModel.MaxHeat));
    }

    [Fact]
    public void Rising_heat_gives_the_first_warning_then_the_last_then_closes()
    {
        // Threshold 50, heat rises 0.1 a second: 500 s away.
        var s = ChoiceState.Fresh;
        LineNotice n;
        (s, n) = LineModel.Tick(s, 50f, 0f, 0.1f, 0, First, Last);
        Assert.Equal(LineNotice.None, n);
        (s, n) = LineModel.Tick(s, 50f, 21f, 0.1f, 210, First, Last); // 290 s left
        Assert.Equal(LineNotice.FirstWarning, n);
        (s, n) = LineModel.Tick(s, 50f, 22f, 0.1f, 220, First, Last);
        Assert.Equal(LineNotice.None, n);
        (s, n) = LineModel.Tick(s, 50f, 45f, 0.1f, 450, First, Last); // 50 s left
        Assert.Equal(LineNotice.LastWarning, n);
        (s, n) = LineModel.Tick(s, 50f, 50f, 0.1f, 500, First, Last);
        Assert.Equal(LineNotice.Closed, n);
        Assert.True(s.Closed);
        (s, n) = LineModel.Tick(s, 50f, 60f, 0.1f, 600, First, Last);
        Assert.Equal(LineNotice.None, n);
    }

    [Fact]
    public void A_sudden_heat_jump_gives_the_last_warning_and_a_grace_before_it_closes()
    {
        var s = ChoiceState.Fresh;
        LineNotice n;
        (s, n) = LineModel.Tick(s, 50f, 70f, 0.1f, 100, First, Last);
        Assert.Equal(LineNotice.LastWarning, n);
        Assert.False(s.Closed);
        (s, n) = LineModel.Tick(s, 50f, 72f, 0.1f, 159, First, Last);
        Assert.False(s.Closed);
        (s, n) = LineModel.Tick(s, 50f, 72f, 0.1f, 160, First, Last);
        Assert.Equal(LineNotice.Closed, n);
    }

    [Fact]
    public void Heat_below_the_threshold_reopens_a_choice_and_warnings_start_again()
    {
        var s = new ChoiceState(2, -1, true);
        LineNotice n;
        (s, n) = LineModel.Tick(s, 50f, 10f, 0.1f, 0, First, Last); // 400 s away
        Assert.Equal(LineNotice.Reopened, n);
        Assert.Equal(ChoiceState.Fresh, s);
        // A choice that cannot close (no threshold) reopens too.
        (_, n) = LineModel.Tick(new ChoiceState(2, -1, true), null, 90f, 0.1f, 0, First, Last);
        Assert.Equal(LineNotice.Reopened, n);
        // No heat rise: no warning.
        (_, n) = LineModel.Tick(ChoiceState.Fresh, 50f, 49f, 0f, 0, First, Last);
        Assert.Equal(LineNotice.None, n);
    }
}

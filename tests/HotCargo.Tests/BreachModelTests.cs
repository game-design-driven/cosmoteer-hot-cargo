using HotCargo.Breach;
using Xunit;

namespace HotCargo.Tests;

public class BreachModelTests
{
    [Theory]
    [InlineData(0, -1, 5, 5, true)]   // walkable above: the top edge of the breach cell
    [InlineData(0, 1, 5, 6, true)]    // walkable below: the top edge of the walkable cell
    [InlineData(-1, 0, 5, 5, false)]  // walkable left: the left edge of the breach cell
    [InlineData(1, 0, 6, 5, false)]   // walkable right: the left edge of the walkable cell
    public void The_door_lies_on_the_edge_between_the_two_cells(int dx, int dy, int x, int y, bool horizontal) =>
        Assert.Equal(((x, y), horizontal), BreachModel.Door((5, 5), (5 + dx, 5 + dy)));

    [Fact]
    public void Cells_that_are_not_side_neighbors_are_an_error() =>
        Assert.Throws<ArgumentException>(() => BreachModel.Door((5, 5), (6, 6)));
}

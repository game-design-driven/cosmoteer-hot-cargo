using HotCargo.Cut;
using HotCargo.Weld;
using Xunit;

namespace HotCargo.Tests;

public class WeldModelTests
{
    [Theory]
    [InlineData(0, 2, 3, 2, 3)]
    [InlineData(1, 2, 3, -4, 2)]  // the unit square [2,3]x[3,4] turned: x from -4 to -3, y from 2 to 3
    [InlineData(2, 2, 3, -3, -4)]
    [InlineData(3, 2, 3, 3, -3)]
    public void A_tile_turns_around_the_grid_origin(int turns, int x, int y, int px, int py) =>
        Assert.Equal(new Cell(px, py), WeldModel.Place(new Cell(x, y), new Placement(turns, 0, 0)));

    [Fact]
    public void Four_quarter_turns_bring_a_tile_back()
    {
        var c = new Cell(5, -2);
        var p = new Placement(1, 0, 0);
        var turned = WeldModel.Place(WeldModel.Place(WeldModel.Place(WeldModel.Place(c, p), p), p), p);
        Assert.Equal(c, turned);
    }

    [Fact]
    public void A_rectangle_turns_with_its_tiles()
    {
        var p = new Placement(1, 10, 20);
        var rect = WeldModel.Place((2, 3, 2, 1), p);
        var tiles = new[] { new Cell(2, 3), new Cell(3, 3) }.Select(c => WeldModel.Place(c, p)).ToList();
        Assert.Equal((tiles.Min(t => t.X), tiles.Min(t => t.Y), 1, 2), rect);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_part_that_cannot_turn_keeps_rotation_0_in_a_turned_piece(int turns)
    {
        // Issue #347: the merge expected a turned structure part, but the game builds a part that cannot turn at rotation 0.
        Assert.Equal(0, WeldModel.PartRotation(0, new Placement(turns, 0, 0), rotateable: false));
        Assert.Equal((1 + turns) & 3, WeldModel.PartRotation(1, new Placement(turns, 0, 0), rotateable: true));
    }

    [Theory]
    [InlineData(0.1f, 2.4f, -3.6f, 0, 2, -4)]
    [InlineData(1.5f, 0f, 0f, 1, 0, 0)]
    [InlineData(-1.6f, 0f, 0f, 3, 0, 0)]
    [InlineData(3.2f, 0f, 0f, 2, 0, 0)]
    public void A_place_snaps_to_the_nearest_tile_and_quarter_turn(float angle, float x, float y, int turns, int px, int py) =>
        Assert.Equal(new Placement(turns, px, py), WeldModel.Snap(angle, x, y));

    /// <summary>Ship parts are letters, target parts are digits.</summary>
    static (CutGrid Grid, HashSet<Cell> Ship) Both(string[] rows, Func<Edge, bool>? contact = null)
    {
        var parts = new Dictionary<Cell, int>();
        var ship = new HashSet<Cell>();
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char ch = rows[y][x];
                if (ch == '.') continue;
                parts[new Cell(x, y)] = ch;
                if (char.IsLetter(ch)) ship.Add(new Cell(x, y));
            }
        return (new CutGrid(parts, contact ?? (_ => true)), ship);
    }

    [Fact]
    public void A_target_that_touches_the_ship_has_joints()
    {
        var (grid, ship) = Both(["AB1", "CD2"]);
        var target = new[] { new Cell(2, 0), new Cell(2, 1) };
        var check = WeldModel.Check(ship, target, grid, p => char.IsDigit((char)p));
        Assert.Equal(WeldVerdict.Ok, check.Verdict);
        Assert.Equal([new Edge(1, 0, false), new Edge(1, 1, false)], check.Joints);
    }

    [Fact]
    public void A_target_on_a_ship_tile_is_covered() =>
        Assert.Equal(WeldVerdict.Covered, WeldModel.Check(new HashSet<Cell> { new(0, 0) }, [new Cell(0, 0)], Both(["A"]).Grid, _ => false).Verdict);

    [Fact]
    public void A_target_in_space_or_without_contact_does_not_weld()
    {
        var (grid, ship) = Both(["A.1"]);
        Assert.Equal(WeldVerdict.NoContact, WeldModel.Check(ship, [new Cell(2, 0)], grid, p => p == '1').Verdict);
        var (grid2, ship2) = Both(["A1"], _ => false);
        Assert.Equal(WeldVerdict.NoContact, WeldModel.Check(ship2, [new Cell(1, 0)], grid2, p => p == '1').Verdict);
    }

    [Fact]
    public void The_pull_moves_at_most_at_its_speed_and_stops_at_the_place()
    {
        var (vx, vy) = WeldModel.PullVelocity(30f, 40f, 4f, 0.1f);
        Assert.Equal(2.4f, vx, 3);
        Assert.Equal(3.2f, vy, 3);
        var (sx, _) = WeldModel.PullVelocity(0.1f, 0f, 4f, 0.1f);
        Assert.Equal(1f, sx, 3);
    }

    [Fact]
    public void The_turn_takes_the_short_way()
    {
        Assert.True(WeldModel.TurnRate(3f * MathF.PI / 2f, 1f, 0.1f) < 0);
        Assert.True(WeldModel.Arrived(0.01f, 0f, 2f * MathF.PI));
    }
}

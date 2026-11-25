using HotCargo.Cut;
using Xunit;

namespace HotCargo.Tests;

public class CutModelTests
{
    /// <summary>A grid from rows of letters: each letter is one part, '.' is empty.</summary>
    static CutGrid Grid(Func<Edge, bool>? contact, params string[] rows)
    {
        var parts = new Dictionary<Cell, int>();
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] != '.') parts[new Cell(x, y)] = rows[y][x];
        return new CutGrid(parts, contact ?? (_ => true));
    }

    static List<Edge> Line(params (int X, int Y)[] corners) => CutModel.Edges(corners.Select(c => new Corner(c.X, c.Y)).ToList());

    [Theory]
    [InlineData(2, 1, 3, 1, 2, 0, true)]  // horizontal: between the tile above and the tile below
    [InlineData(3, 1, 2, 1, 2, 0, true)]
    [InlineData(2, 1, 2, 2, 1, 1, false)] // vertical: between the tile on the left and the tile on the right
    [InlineData(2, 2, 2, 1, 1, 1, false)]
    public void An_edge_along_two_corners_lies_between_the_tiles_on_its_sides(int ax, int ay, int bx, int by, int x, int y, bool down) =>
        Assert.Equal(new Edge(x, y, down), Edge.Along(new Corner(ax, ay), new Corner(bx, by)));

    [Fact]
    public void A_straight_cut_across_the_ship_is_clean_and_names_its_work()
    {
        var grid = Grid(null, "ABCD", "EFGH");
        var check = CutModel.Check(grid, Line((2, 0), (2, 1), (2, 2)));
        Assert.Equal(CutVerdict.Ok, check.Verdict);
        Assert.Equal([new Edge(1, 0, false), new Edge(1, 1, false)], check.Work);
        Assert.NotEqual(check.Pieces['A'], check.Pieces['C']);
        Assert.Equal(check.Pieces['A'], check.Pieces['F']);
    }

    [Fact]
    public void Edges_in_space_need_no_work_but_do_not_spoil_the_cut()
    {
        var grid = Grid(null, "ABCD", "EFGH");
        var check = CutModel.Check(grid, Line((2, -1), (2, 0), (2, 1), (2, 2), (2, 3)));
        Assert.Equal(CutVerdict.Ok, check.Verdict);
        Assert.Equal(2, check.Work.Count);
    }

    [Fact]
    public void The_line_stops_at_an_edge_inside_a_two_tile_part()
    {
        var grid = Grid(null, "AAB", "CDE");
        var line = new List<Corner> { new(1, 0) };
        var blocked = CutModel.Extend(line, new Corner(1, 2), grid.Blocked);
        Assert.Equal(new Edge(0, 0, false), blocked);
        Assert.Equal([new Corner(1, 0)], line);
    }

    [Fact]
    public void Dragging_back_over_the_last_edge_removes_it()
    {
        var line = new List<Corner> { new(0, 0) };
        Assert.Null(CutModel.Extend(line, new Corner(3, 0), _ => false));
        Assert.Null(CutModel.Extend(line, new Corner(1, 0), _ => false));
        Assert.Equal([new Corner(0, 0), new Corner(1, 0)], line);
    }

    [Fact]
    public void A_line_that_stops_halfway_cuts_off_nothing()
    {
        var grid = Grid(null, "ABCD", "EFGH");
        Assert.Equal(CutVerdict.OnePiece, CutModel.Check(grid, Line((2, 0), (2, 1))).Verdict);
    }

    [Fact]
    public void A_line_in_space_cuts_off_nothing()
    {
        var grid = Grid(null, "AB");
        Assert.Equal(CutVerdict.OnePiece, CutModel.Check(grid, Line((0, 3), (1, 3))).Verdict);
    }

    [Fact]
    public void A_line_with_turns_cuts_off_a_corner()
    {
        // An L around the top right tile D: down, then right, out into space.
        var grid = Grid(null, "ABCD", "EFGH");
        var check = CutModel.Check(grid, Line((3, -1), (3, 0), (3, 1), (4, 1), (5, 1)));
        Assert.Equal(CutVerdict.Ok, check.Verdict);
        Assert.Equal([new Edge(2, 0, false), new Edge(3, 0, true)], check.Work);
        Assert.NotEqual(check.Pieces['D'], check.Pieces['C']);
        Assert.Equal(check.Pieces['C'], check.Pieces['H']);
    }

    [Fact]
    public void A_closed_loop_cuts_out_an_inner_piece()
    {
        var grid = Grid(null, "ABC", "DEF", "GHI");
        var check = CutModel.Check(grid, Line((1, 1), (2, 1), (2, 2), (1, 2), (1, 1)));
        Assert.Equal(CutVerdict.Ok, check.Verdict);
        Assert.Equal(4, check.Work.Count);
        Assert.Equal(2, check.Pieces.Values.Distinct().Count());
        Assert.NotEqual(check.Pieces['E'], check.Pieces['A']);
    }

    [Fact]
    public void Two_crossing_lines_make_four_pieces()
    {
        var grid = Grid(null, "ABCD", "EFGH", "IJKL", "MNOP");
        var edges = Line((2, 0), (2, 1), (2, 2), (2, 3), (2, 4)).Concat(Line((0, 2), (1, 2), (2, 2), (3, 2), (4, 2))).ToList();
        var check = CutModel.Check(grid, edges);
        Assert.Equal(CutVerdict.Ok, check.Verdict);
        Assert.Equal(4, check.Pieces.Values.Distinct().Count());
    }

    [Fact]
    public void A_loose_end_inside_one_piece_gets_no_work()
    {
        var grid = Grid(null, "ABCD", "EFGH");
        var edges = Line((2, 0), (2, 1), (2, 2)).Append(new Edge(0, 0, false)).ToList();
        var check = CutModel.Check(grid, edges);
        Assert.Equal(CutVerdict.Ok, check.Verdict);
        Assert.DoesNotContain(new Edge(0, 0, false), check.Work);
        Assert.Equal(2, check.Work.Count);
    }

    [Fact]
    public void Parts_that_are_not_in_contact_need_no_cut_between_them()
    {
        // B and C touch but are not in contact (a wedge side), so the cut only needs the lower edge.
        var noContact = new Edge(1, 0, false);
        var grid = Grid(e => e != noContact, "ABCD", "EFGH");
        var check = CutModel.Check(grid, Line((2, 1), (2, 2)));
        Assert.Equal(CutVerdict.Ok, check.Verdict);
        Assert.Equal([new Edge(1, 1, false)], check.Work);
    }

    [Fact]
    public void The_piece_with_the_strongest_control_room_stays_even_when_smaller()
    {
        var grid = Grid(null, "ABCD", "EFGH");
        var pieces = CutModel.Check(grid, Line((1, 0), (1, 1), (1, 2))).Pieces;
        int stay = CutModel.StayPiece(pieces, p => p == 'A' ? 3 : 0, _ => 1);
        Assert.Equal(pieces['A'], stay);
        // Without a control room, the larger piece stays.
        Assert.Equal(pieces['B'], CutModel.StayPiece(pieces, _ => 0, _ => 1));
    }

    [Fact]
    public void The_crew_stand_on_the_side_that_stays()
    {
        var grid = Grid(null, "ABCD", "EFGH");
        var check = CutModel.Check(grid, Line((1, 0), (1, 1), (1, 2)));
        int stay = check.Pieces['D'];
        Assert.Equal(new Cell(1, 0), CutModel.WorkCell(grid, check.Pieces, stay, new Edge(0, 0, false)));
    }

    [Fact]
    public void The_design_loses_the_far_side_with_its_unbuilt_parts()
    {
        // The design has a part X at (4, 0) that is not built: it hangs on the far side and goes too.
        var design = Grid(null, "ABCDX", "EFGH.");
        var cut = Line((2, 0), (2, 1), (2, 2)).ToHashSet();
        var keep = new HashSet<Cell> { new(0, 0), new(1, 1) };
        Assert.Equal(new HashSet<int> { 'C', 'D', 'G', 'H', 'X' }, CutModel.Trim(design, cut, keep));
    }
}

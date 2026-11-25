using System.Globalization;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Cut;

/// <summary>Test commands of the cut (spec 2472). "me" is the acting player's first ship at cut_mark.</summary>
static class CutAutotest
{
    static ObjectID? _me;
    static int _partsAtMark;
    static CutVerdict? _verdict;

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;
    static Ship Me() => _me is { } id ? Sim().Ships[id] ?? throw new InvalidOperationException("the cut ship is gone") : throw new InvalidOperationException("cut_mark first");
    static CutOrder? Order() => Autotest.RequireRun().State.Cut.Orders.FirstOrDefault(o => o.Ship == Me().UniqueID);
    static CutGrid Grid() => CutShip.Grid(Me().Parts).Grid;

    /// <summary>The straight lines across the ship, first every column line, then every row line, each from space to space.</summary>
    static IEnumerable<(Corner From, Corner To)> StraightLines(CutGrid grid)
    {
        var cells = grid.Parts.Keys.ToList();
        int left = cells.Min(c => c.X), right = cells.Max(c => c.X) + 1, top = cells.Min(c => c.Y), bottom = cells.Max(c => c.Y) + 1;
        for (int x = left + 1; x < right; x++) yield return (new(x, top - 1), new(x, bottom + 1));
        for (int y = top + 1; y < bottom; y++) yield return (new(left - 1, y), new(right + 1, y));
    }

    /// <summary>A straight line as the drag tool draws it: it stops at a blocked edge and returns that edge.</summary>
    static (List<Corner> Line, Edge? Blocked) Drag(CutGrid grid, (Corner From, Corner To) ends)
    {
        var line = new List<Corner> { ends.From };
        return (line, CutModel.Extend(line, ends.To, grid.Blocked));
    }

    /// <summary>The straight clean cut with the most edges that need work, so the tests and the video cut through the middle.</summary>
    static List<Corner> CleanLine(CutGrid grid) =>
        StraightLines(grid).Select(ends => Drag(grid, ends))
            .Where(d => d.Blocked is null)
            .Select(d => (d.Line, Check: CutModel.Check(grid, CutModel.Edges(d.Line))))
            .Where(l => l.Check.Verdict == CutVerdict.Ok)
            .MaxBy(l => l.Check.Work.Count).Line
        ?? throw new InvalidOperationException("no straight clean cut line on the cut ship");

    /// <summary>A straight line that runs into a part of two or more tiles, as the drag tool leaves it, with the blocked edge.</summary>
    static (List<Corner> Line, Edge? Blocked) BlockedLine(CutGrid grid) =>
        StraightLines(grid).Select(ends => Drag(grid, ends)).FirstOrDefault(d => d.Blocked is not null && d.Line.Count >= 2) is { Line: not null } d
            ? d : throw new InvalidOperationException("no straight line of the cut ship runs into a part of two or more tiles");

    /// <summary>The clean line up to half of its work edges.</summary>
    static List<Corner> HalfLine(CutGrid grid)
    {
        var clean = CleanLine(grid);
        var edges = CutModel.Edges(clean);
        var work = edges.Where(grid.NeedsWork).ToList();
        if (work.Count < 2) throw new InvalidOperationException("the clean line has fewer than 2 work edges");
        return clean.Take(edges.IndexOf(work[work.Count / 2 - 1]) + 2).ToList();
    }

    /// <summary>An edge that needs work and lies off the clean line, so both its sides are in one piece.</summary>
    static Edge LooseEdge(CutGrid grid, List<Edge> clean) =>
        grid.Parts.Keys.OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => new Edge(c.X, c.Y, false))
            .First(e => grid.NeedsWork(e) && !clean.Contains(e));

    internal static void Register()
    {
        Autotest.Commands["cut_mark"] = _ =>
        {
            _me = (Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("the acting player has no ship")).UniqueID;
            _partsAtMark = Me().Parts.Count;
        };
        // cut_clean: the clean cut of "me" becomes an order through the MP input, as a release of the drag does.
        Autotest.Commands["cut_clean"] = _ =>
        {
            var edges = CutModel.Edges(CleanLine(Grid()));
            Log.Info($"AUTOTEST cut: clean line of {edges.Count} edges from {edges[0]} to {edges[^1]}");
            Autotest.RequireRun().Game.NetManager.EnqueueInput(new CutOrderInput(Me(), edges));
        };
        // cut_try blocked|half|loose: checks a bad line as the drag tool does at the release and keeps the verdict (cut_verdict).
        // blocked: the line runs into a 2x1 part, so the drag stops at the part and the line it has does not go across.
        Autotest.Commands["cut_try"] = arg =>
        {
            var grid = Grid();
            List<Edge> edges = arg.Trim() switch
            {
                "blocked" => CutModel.Edges(BlockedLine(grid).Line),
                "half" => CutModel.Edges(HalfLine(grid)),
                "loose" => [.. CutModel.Edges(CleanLine(grid)), LooseEdge(grid, CutModel.Edges(CleanLine(grid)))],
                var k => throw new InvalidOperationException($"unknown bad line '{k}' (blocked, half or loose)"),
            };
            _verdict = CutArea.Refusal(Me(), edges, out _);
            Log.Info($"AUTOTEST cut: {arg.Trim()} line of {edges.Count} edges, verdict {_verdict?.ToString() ?? "Ok"}");
        };
        // cut_interrupt: every crew member leaves the cut jobs, as when a higher priority job takes them (the tick puts the jobs back).
        Autotest.Commands["cut_interrupt"] = _ =>
        {
            foreach (var e in Order()?.Edges ?? [])
                if (e.Job is { } job && job.Ship == Me()) Me().Jobs.RemoveJob(job, unassignCrew: true);
            Log.Info($"AUTOTEST cut: crew left the cut jobs at {Order()?.Edges.Sum(e => e.Work):0.0} s of work");
        };

        // Screenshots, UI only. cut_frame <size>: the camera on the parts of "me", this many world units across.
        Autotest.Commands["cut_frame"] = arg =>
        {
            var ship = Me();
            var view = Sim().Camera.ViewController;
            view.ClearFocus(clearFramesOfReferenceAndRotation: true);
            view.FRLocation = ship.TransformShapeToWorld((Rect)(ship.Parts.BoundingRect ?? throw new InvalidOperationException("the cut ship has no parts"))).Center;
            Autotest.FitCamera(Sim(), float.Parse(arg, CultureInfo.InvariantCulture));
        };
        // cut_frame_both <margin>: the camera on "me" and the newest cut-off piece together, with this margin in world units.
        Autotest.Commands["cut_frame_both"] = arg =>
        {
            var piece = CutArea.LastPiece is { } id ? Sim().Ships[id] ?? throw new InvalidOperationException("the cut-off piece is gone")
                : throw new InvalidOperationException("no split yet");
            Vector2 a = Me().WorldCenter, b = piece.WorldCenter;
            var view = Sim().Camera.ViewController;
            view.ClearFocus(clearFramesOfReferenceAndRotation: true);
            view.FRLocation = (a + b) * 0.5f;
            Autotest.FitCamera(Sim(), a.DistanceTo(b) + float.Parse(arg, CultureInfo.InvariantCulture));
        };
        // cut_follow <size>: cut_frame before the split, cut_frame_both after it (video frames that keep both pieces in view).
        Autotest.Commands["cut_follow"] = arg =>
            Autotest.Commands[CutArea.LastPiece is { } id && Sim().Ships[id] is not null ? "cut_frame_both" : "cut_frame"](arg);
        // cut_tool: selects "me", opens build mode and picks the Cut tool, like a click on its button.
        Autotest.Commands["cut_tool"] = _ =>
        {
            Sim().PlayerInput.SelectSingleShip(Me());
            Autotest.RequireRun().Game.Gui.ShipGui.BuildButton.IsSelected = true;
            CutTool.Select();
        };
        // cut_drag clean|blocked|half: puts a line into the Cut tool as a drag leaves it. cut_release: the release of the drag.
        Autotest.Commands["cut_drag"] = arg =>
        {
            var grid = Grid();
            var (line, blocked) = arg.Trim() switch
            {
                "clean" => (CleanLine(grid), (Edge?)null),
                "blocked" => BlockedLine(grid),
                "half" => (HalfLine(grid), null),
                var k => throw new InvalidOperationException($"unknown line '{k}' (clean, blocked or half)"),
            };
            CutToolState.RequireCurrent().ShowLine(line, blocked);
        };
        Autotest.Commands["cut_release"] = _ => CutToolState.RequireCurrent().Release(Me());

        Autotest.Fields["cut_verdict"] = () => (double)(_verdict ?? CutVerdict.Ok);
        Autotest.Fields["cut_orders"] = () => Autotest.RequireRun().State.Cut.Orders.Count;
        Autotest.Fields["cut_edges_left"] = () => Order()?.Edges.Count(e => !e.Done) ?? 0;
        Autotest.Fields["cut_work"] = () => Order()?.Edges.Sum(e => e.Work) ?? 0;
        Autotest.Fields["cut_crew"] = () => Order()?.Edges.Sum(e => e.Job?.AssignedCrew.Count ?? 0) ?? 0;
        Autotest.Fields["cut_splits"] = () => Autotest.RequireRun().State.Cut.Splits;
        Autotest.Fields["cut_ships"] = () => Sim().Ships.Count;
        // Parts of the piece that the newest split cut off (0 when it is gone).
        Autotest.Fields["cut_piece_parts"] = () => CutArea.LastPiece is { } id && Sim().Ships[id] is { } piece ? piece.Parts.Count : 0;
        // cut_set <field> <value>: sets one Cut tunable for this run (a slower cut for the video).
        Autotest.Commands["cut_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Cut, arg);
        // Parts that the ship lost since cut_mark: 0 until the split (no partial separation, spec 2465).
        Autotest.Fields["cut_parts_lost"] = () => _partsAtMark - Me().Parts.Count;
        // Design parts with no built part: 0 after the design trim when nothing is damaged (spec 2466).
        Autotest.Fields["cut_design_extra"] = () => Me().BlueprintParts.Count - Me().Parts.Count;
    }
}

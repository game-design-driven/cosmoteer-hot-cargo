using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
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

    /// <summary>The straight lines across the ship, first every column line, then every row line, each from space to space.</summary>
    static IEnumerable<List<Corner>> StraightLines(CutGrid grid)
    {
        var cells = grid.Parts.Keys.ToList();
        int left = cells.Min(c => c.X), right = cells.Max(c => c.X) + 1, top = cells.Min(c => c.Y), bottom = cells.Max(c => c.Y) + 1;
        for (int x = left + 1; x < right; x++) yield return [new(x, top - 1), new(x, bottom + 1)];
        for (int y = top + 1; y < bottom; y++) yield return [new(left - 1, y), new(right + 1, y)];
    }

    /// <summary>The straight clean cut with the most edges that need work, so the test and the video cut through the middle.</summary>
    static List<Edge> CleanLine(CutGrid grid) =>
        StraightLines(grid).Select(ends =>
            {
                var line = new List<Corner> { ends[0] };
                return CutModel.Extend(line, ends[1], grid.Blocked) is null ? CutModel.Check(grid, CutModel.Edges(line)) is { Verdict: CutVerdict.Ok } c
                    ? (Edges: CutModel.Edges(line), c.Work.Count) : (null, 0) : (null, 0);
            })
            .Where(l => l.Edges is not null).MaxBy(l => l.Count).Edges
        ?? throw new InvalidOperationException("no straight clean cut line on the cut ship");

    internal static void Register()
    {
        Autotest.Commands["cut_mark"] = _ =>
        {
            _me = (Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("the acting player has no ship")).UniqueID;
            _partsAtMark = Me().Parts.Count;
        };
        // cut_clean: the first straight clean cut of "me" becomes an order through the MP input, as a release of the drag does.
        Autotest.Commands["cut_clean"] = _ =>
        {
            var edges = CleanLine(CutShip.Grid(Me().Parts).Grid);
            Log.Info($"AUTOTEST cut: clean line of {edges.Count} edges from {edges[0]} to {edges[^1]}");
            Autotest.RequireRun().Game.NetManager.EnqueueInput(new CutOrderInput(Me(), edges));
        };
        // cut_try blocked|half|loose: checks a bad line as the drag tool does at the release and keeps the verdict (cut_verdict).
        // blocked: the line runs into a 2x1 part, so the drag stops at the part and the line it has does not go across.
        Autotest.Commands["cut_try"] = arg =>
        {
            var grid = CutShip.Grid(Me().Parts).Grid;
            List<Edge> edges = arg.Trim() switch
            {
                "blocked" => BlockedLine(grid),
                "half" => HalfLine(grid),
                "loose" => [.. CleanLine(grid), LooseEdge(grid, CleanLine(grid))],
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
        Autotest.Fields["cut_verdict"] = () => (double)(_verdict ?? CutVerdict.Ok);
        Autotest.Fields["cut_orders"] = () => Autotest.RequireRun().State.Cut.Orders.Count;
        Autotest.Fields["cut_edges_left"] = () => Order()?.Edges.Count(e => !e.Done) ?? 0;
        Autotest.Fields["cut_work"] = () => Order()?.Edges.Sum(e => e.Work) ?? 0;
        Autotest.Fields["cut_crew"] = () => Order()?.Edges.Sum(e => e.Job?.AssignedCrew.Count ?? 0) ?? 0;
        Autotest.Fields["cut_splits"] = () => Autotest.RequireRun().State.Cut.Splits;
        Autotest.Fields["cut_ships"] = () => Sim().Ships.Count;
        // Parts that the ship lost since cut_mark: 0 until the split (no partial separation, spec 2465).
        Autotest.Fields["cut_parts_lost"] = () => _partsAtMark - Me().Parts.Count;
        // Design parts with no built part: 0 after the design trim when nothing is damaged (spec 2466).
        Autotest.Fields["cut_design_extra"] = () => Me().BlueprintParts.Count - Me().Parts.Count;
    }

    /// <summary>A clean line that stops after half of its work edges.</summary>
    static List<Edge> HalfLine(CutGrid grid)
    {
        var clean = CleanLine(grid);
        var work = clean.Where(grid.NeedsWork).ToList();
        if (work.Count < 2) throw new InvalidOperationException("the clean line has fewer than 2 work edges");
        return clean.Take(clean.IndexOf(work[work.Count / 2 - 1]) + 1).ToList();
    }

    /// <summary>A straight line that runs into a part of two or more tiles, cut at that part, as the drag tool leaves it.</summary>
    static List<Edge> BlockedLine(CutGrid grid)
    {
        foreach (var ends in StraightLines(grid))
        {
            var line = new List<Corner> { ends[0] };
            if (CutModel.Extend(line, ends[1], grid.Blocked) is not null && line.Count >= 2) return CutModel.Edges(line);
        }
        throw new InvalidOperationException("no straight line of the cut ship runs into a part of two or more tiles");
    }

    /// <summary>An edge that needs work and lies off the clean line, so both its sides are in one piece.</summary>
    static Edge LooseEdge(CutGrid grid, List<Edge> clean) =>
        grid.Parts.Keys.OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => new Edge(c.X, c.Y, false))
            .First(e => grid.NeedsWork(e) && !clean.Contains(e));
}

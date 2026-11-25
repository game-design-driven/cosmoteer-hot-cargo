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
    /// <summary>The cut tests' ship, for the weld tests.</summary>
    internal static ObjectID? MeId => _me;
    static int _partsAtMark;
    static CutVerdict? _verdict;
    /// <summary>cut_blueprint on: test orders are made as in blueprint mode, so they wait for cut_apply (spec 2475).</summary>
    static bool _blueprint;
    static HashSet<ObjectID> _beforeBlast = [];
    static double _notedDist;

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;
    static Ship Me() => _me is { } id ? Sim().Ships[id] ?? throw new InvalidOperationException("the cut ship is gone") : throw new InvalidOperationException("cut_mark first");
    static CutOrder? Order() => Autotest.RequireRun().State.Cut.Orders.FirstOrDefault(o => o.Ship == Me().UniqueID);
    static Ship? Piece() => CutArea.LastPiece is { } id ? Sim().Ships[id] : null;
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

    /// <summary>An L line: down a column from space, then along a row out into space on the right. Of the L lines that cut off one
    /// piece, the one with the most work.</summary>
    static List<Corner> LLine(CutGrid grid)
    {
        var cells = grid.Parts.Keys.ToList();
        int left = cells.Min(c => c.X), right = cells.Max(c => c.X) + 1, top = cells.Min(c => c.Y), bottom = cells.Max(c => c.Y) + 1;
        var lines = new List<(List<Corner> Line, int Work)>();
        for (int x = left + 1; x < right; x++)
            for (int y = top + 1; y < bottom; y++)
            {
                var line = new List<Corner> { new(x, top - 1) };
                if (CutModel.Extend(line, new Corner(x, y), grid.Blocked) is not null || CutModel.Extend(line, new Corner(right + 1, y), grid.Blocked) is not null) continue;
                var check = CutModel.Check(grid, CutModel.Edges(line));
                if (check.Verdict == CutVerdict.Ok && check.Pieces.Values.Distinct().Count() == 2) lines.Add((line, check.Work.Count));
            }
        return lines.Count > 0 ? lines.MaxBy(l => l.Work).Line : throw new InvalidOperationException("no L cut line on the cut ship");
    }

    /// <summary>
    /// Spec 2462: the smallest closed rectangle loop of at least <paramref name="minArea"/> tiles that cuts out an inner block: every
    /// tile inside holds a part, every tile just outside it holds a part (the block is enclosed), and no edge of the loop is blocked.
    /// </summary>
    static List<Corner> LoopLine(CutGrid grid, int minArea)
    {
        var cells = grid.Parts.Keys.ToList();
        int left = cells.Min(c => c.X), right = cells.Max(c => c.X) + 1, top = cells.Min(c => c.Y), bottom = cells.Max(c => c.Y) + 1;
        var sizes = from w in Enumerable.Range(1, 4) from h in Enumerable.Range(1, 4) orderby w * h, w select (w, h);
        foreach (var (w, h) in sizes.Where(s => s.w * s.h >= minArea))
            for (int y = top + 1; y + h < bottom; y++)
                for (int x = left + 1; x + w < right; x++)
                {
                    bool Has(int cx, int cy) => grid.PartAt(new Cell(cx, cy)) is not null;
                    bool inside = Enumerable.Range(x, w).All(cx => Enumerable.Range(y, h).All(cy => Has(cx, cy)));
                    bool ring = Enumerable.Range(x - 1, w + 2).All(cx => Has(cx, y - 1) && Has(cx, y + h))
                        && Enumerable.Range(y, h).All(cy => Has(x - 1, cy) && Has(x + w, cy));
                    if (!inside || !ring) continue;
                    var line = new List<Corner> { new(x, y) };
                    if (CutModel.Extend(line, new Corner(x + w, y), grid.Blocked) is not null
                        || CutModel.Extend(line, new Corner(x + w, y + h), grid.Blocked) is not null
                        || CutModel.Extend(line, new Corner(x, y + h), grid.Blocked) is not null
                        || CutModel.Extend(line, new Corner(x, y), grid.Blocked) is not null) continue;
                    if (CutModel.Check(grid, CutModel.Edges(line)).Verdict == CutVerdict.Ok) return line;
                }
        throw new InvalidOperationException("no enclosed block to cut out of the cut ship");
    }

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

    static List<Corner> Shape(string kind) => kind switch
    {
        "l" => LLine(Grid()),
        "loop" => LoopLine(Grid(), 1),
        "bigloop" => LoopLine(Grid(), 4),
        _ => throw new InvalidOperationException($"unknown shape '{kind}' (l, loop or bigloop)"),
    };

    /// <summary>Selects "me", opens its build window in the mode of cut_blueprint and clicks a vanilla button of it.</summary>
    static void Buttons(string button)
    {
        var game = Autotest.RequireRun().Game;
        Sim().PlayerInput.SelectSingleShip(Me());
        game.Gui.ShipGui.BuildButton.IsSelected = true;
        var box = game.Gui.ShipGui.BuildToolbox;
        switch (button)
        {
            case "_constructBtn": box.OnConstructClicked(null, EventArgs.Empty); break;
            case "_discardBtn": box.OnDiscardBlueprintChangesClicked(null, EventArgs.Empty); break;
            case "_cancelAllBtn": box.OnCancelAllClicked(null, EventArgs.Empty); break;
            default: throw new InvalidOperationException($"unknown button {button}");
        }
    }

    internal static void Register()
    {
        Autotest.Commands["cut_mark"] = arg =>
        {
            // cut_mark biggest: the human player ship with the most parts (screenshots with several own ships).
            _me = (arg.Trim() == "biggest" ? Sim().Ships.HumanPlayerShips.OrderByDescending(x => x.Parts.Count).ThenBy(x => x.UniqueID).FirstOrDefault()
                : Autotest.ActingShip(Sim())) is { } ship ? ship.UniqueID : throw new InvalidOperationException("the acting player has no ship");
            _partsAtMark = Me().Parts.Count;
        };
        // cut_clean: the clean cut of "me" becomes an order through the MP input, as a release of the drag does.
        Autotest.Commands["cut_clean"] = _ =>
        {
            var edges = CutModel.Edges(CleanLine(Grid()));
            Log.Info($"AUTOTEST cut: clean line of {edges.Count} edges from {edges[0]} to {edges[^1]}");
            Autotest.RequireRun().Game.NetManager.EnqueueInput(new CutOrderInput(Me(), edges, _blueprint));
        };
        // cut_shape l|loop: the L line or the loop line of "me" becomes an order through the MP input.
        Autotest.Commands["cut_shape"] = arg =>
        {
            var line = Shape(arg.Trim());
            Log.Info($"AUTOTEST cut: {arg.Trim()} line through corners {string.Join(" ", line.Select(c => $"{c.X},{c.Y}"))}");
            Autotest.RequireRun().Game.NetManager.EnqueueInput(new CutOrderInput(Me(), CutModel.Edges(line), _blueprint));
        };
        // cut_fly <dx> <dy> <degrees>: test only, single player. "me" flies to its position plus the offset and turns to the angle
        // (a move order), so a test sees how a cut-out piece rides along.
        Autotest.Commands["cut_fly"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("cut_fly is single player only");
            var n = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            if (n.Length != 3) throw new InvalidOperationException("cut_fly needs: dx dy degrees");
            var ship = Me();
            ship.Commands.EnqueueCommand(new Cosmoteer.Ships.Commands.MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), ship.DetWorldCenter + new Vector2(n[0], n[1]),
                Direction.FromDegrees(n[2]), Cosmoteer.Ships.Commands.CollisionAvoidanceFlags.None, isTentative: false), append: false, confirmed: true);
        };
        // cut_line <x1> <y1> <x2> <y2>: the straight line between two corners of "me" becomes an order through the MP input.
        Autotest.Commands["cut_line"] = arg =>
        {
            var n = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            if (n.Length != 4) throw new InvalidOperationException("cut_line needs: x1 y1 x2 y2");
            var line = new List<Corner> { new(n[0], n[1]) };
            if (CutModel.Extend(line, new Corner(n[2], n[3]), Grid().Blocked) is { } b) throw new InvalidOperationException($"the line is blocked at {b}");
            Autotest.RequireRun().Game.NetManager.EnqueueInput(new CutOrderInput(Me(), CutModel.Edges(line), _blueprint));
        };
        // cut_twin: test only, single player. Builds an own ship of two halves, each with a control room, a reactor, crew quarters and
        // a middle column of corridors, next to the first own ship, fills it with crew, and marks it as "me". The halves meet along the corner line x = 3.
        Autotest.Commands["cut_twin"] = _ =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("cut_twin is single player only");
            var sim = Sim();
            var home = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("the acting player has no ship");
            var ship = new Ship(home.Rules, "Twin", home.Metadata.PlayerIndex, randSeed: 9002UL);
            // Corridors first: the automatic door rule adds doors only toward parts that the ship already has (ShipBuilder.Build).
            (string Part, int X, int Y)[] layout =
            [
                ("corridor", 2, 0), ("corridor", 2, 1), ("corridor", 2, 2), ("corridor", 2, 3), ("corridor", 2, 4), ("corridor", 2, 5),
                ("corridor", 3, 0), ("corridor", 3, 1), ("corridor", 3, 2), ("corridor", 3, 3), ("corridor", 3, 4), ("corridor", 3, 5),
                // Small quarters open on their long sides, so each faces its own half's corridor.
                ("crew_quarters_small", 1, 4), ("crew_quarters_small", 4, 4),
                // An airlock under each corridor: crew build and cut from outside (EVA), as vanilla construction does.
                ("airlock", 2, 6), ("airlock", 3, 6),
                ("control_room_small", 0, 0), ("control_room_small", 4, 0), ("reactor_small", 0, 2), ("reactor_small", 4, 2),
            ];
            foreach (var (part, x, y) in layout)
            {
                using var sui = ShipUpdateInfo.Alloc(ship);
                var rules = home.Rules.PartsByID[new Cosmoteer.Data.ID<Cosmoteer.Ships.Parts.PartRules>("cosmoteer." + part)];
                if (!sui.TryAddBlueprintPart(rules, new IntVector2(x, y), 0, flipX: false, mirrorPhysical: false, allowOverwrite: false, addDoors: true,
                        hireCrew: false, out var reasons, force: false))
                    throw new InvalidOperationException($"cut_twin: cannot add {part} at {x},{y}: {reasons}");
                sui.Apply();
            }
            ship.MatchPhysicalToBlueprints();
            ship.WorldCenter = Autotest.ClearSpotNear(sim, home.DetWorldCenter + new Vector2(60f, 0f), 60f, ship.WorldBoundingRadius);
            sim.Ships.Add(ship);
            ship.InitializeToMax();
            ship.Crew.CreateCrewsAtRandomParts(ship.Crew.TotalCrewCapacityWithConstruction);
            _me = ship.UniqueID;
            _partsAtMark = ship.Parts.Count;
            Log.Info($"AUTOTEST cut: twin ship {ship.UniqueID} with {ship.Parts.Count} parts and {ship.Crew.Assigned.Count} crew");
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
        // cut_blueprint on|off: later test orders come as from blueprint mode (pending) or live mode.
        Autotest.Commands["cut_blueprint"] = arg => _blueprint = arg.Trim() == "on";
        // cut_apply | cut_discard | cut_cancelall: the vanilla buttons "Make it so", "Discard changes" and "Cancel all" of "me",
        // clicked in its build window (spec 2475).
        Autotest.Commands["cut_apply"] = _ => Buttons("_constructBtn");
        Autotest.Commands["cut_discard"] = _ => Buttons("_discardBtn");
        Autotest.Commands["cut_cancelall"] = _ => Buttons("_cancelAllBtn");
        // cut_blast: test only, single player. Destroys the parts on the staying side of the clean line of "me", as explosions would,
        // so the game splits the ship by itself (spec 2476).
        Autotest.Commands["cut_blast"] = _ =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("cut_blast is single player only");
            var ship = Me();
            var layout = CutShip.Grid(ship.Parts);
            var check = CutModel.Check(layout.Grid, CutModel.Edges(CleanLine(layout.Grid)));
            int stay = CutModel.StayPiece(check.Pieces, i => layout.Parts[i].Rules.ContiguityOriginPriority, i => CutShip.Area(layout.Parts[i]));
            _beforeBlast = Sim().Ships.Select(x => x.UniqueID).ToHashSet();
            var hit = check.Work.Select(e => layout.Grid.PartAt(CutModel.WorkCell(layout.Grid, check.Pieces, stay, e))).OfType<int>().Distinct().Select(i => layout.Parts[i]).ToList();
            foreach (var part in hit) part.Health = 0;
            Log.Info($"AUTOTEST cut: blast destroyed {hit.Count} parts of {ship.Metadata.PlainShipName}");
        };
        // Ships that appeared since cut_blast, and how many of them are a human player's ship.
        Autotest.Fields["cut_blast_new"] = () => Sim().Ships.Count(x => !_beforeBlast.Contains(x.UniqueID));
        Autotest.Fields["cut_blast_own"] = () => Sim().Ships.Count(x => !_beforeBlast.Contains(x.UniqueID) && x.IsHumanPlayerShip);
        Autotest.Fields["cut_me_own"] = () => Me().IsHumanPlayerShip ? 1 : 0;
        // 1 when the newest cut-off piece is a derelict.
        Autotest.Fields["cut_piece_junk"] = () => Piece() is { IsJunk: true } ? 1 : 0;
        Autotest.Fields["cut_piece_beds"] = () => Piece() is { } p ? p.Crew.TotalCrewCapacityWithConstruction : 0;
        Autotest.Fields["cut_pending"] = () => Order() is { Pending: true } ? 1 : 0;
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
        // cut_select_piece: selects the newest cut-off piece, so the ships card shows it (screenshots).
        Autotest.Commands["cut_select_piece"] = _ => Sim().PlayerInput.SelectSingleShip(
            CutArea.LastPiece is { } pid && Sim().Ships[pid] is { } p ? p : throw new InvalidOperationException("no cut-off piece"));
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
                "l" or "loop" or "bigloop" => (Shape(arg.Trim()), null),
                var k => throw new InvalidOperationException($"unknown line '{k}' (clean, blocked, half, l or loop)"),
            };
            CutToolState.RequireCurrent().ShowLine(line, blocked);
        };
        // cut_hover: the Cut tool's cursor stands near the top corner of the clean line (screenshots).
        Autotest.Commands["cut_hover"] = _ =>
        {
            var c = CleanLine(Grid())[0];
            CutToolState.RequireCurrent().HoverAt = new Vector2(c.X + 0.15f, c.Y + 1.6f);
        };
        // cut_blueprints on|off: the build window's blueprint mode toggle (screenshots).
        Autotest.Commands["cut_blueprints"] = arg => Autotest.RequireRun().Game.Gui.ShipGui.BuildToolbox._blueprintsToggle.IsSelected = arg.Trim() == "on";
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
        // Own ships among "me" and the newest cut-off piece: a human player's ship that is not a derelict (spec 2465).
        Autotest.Fields["cut_own_pieces"] = () => new[] { (ObjectID?)Me().UniqueID, CutArea.LastPiece }
            .Count(id => id is { } i && Sim().Ships[i] is { } s && s.IsHumanPlayerShip && !s.IsJunk);
        // Distance between the centers of "me" and the newest cut-off piece, and their speed relative to each other (spec 2465).
        Autotest.Fields["cut_piece_dist"] = () => Piece() is { } p ? p.DetWorldCenter.DistanceTo(Me().DetWorldCenter) : -1;
        Autotest.Fields["cut_piece_relspeed"] = () => Piece() is { } p ? (p.Physics.Body.LinearVelocity - Me().Physics.Body.LinearVelocity).Length() : -1;
        // cut_note_dist: notes cut_piece_dist; cut_piece_drift is how far it moved since (a trapped piece stays put, spec 2465).
        Autotest.Commands["cut_note_dist"] = _ => _notedDist = Autotest.Fields["cut_piece_dist"]();
        Autotest.Fields["cut_piece_drift"] = () => Math.Abs(Autotest.Fields["cut_piece_dist"]() - _notedDist);
        // Crew of the newest cut-off piece.
        Autotest.Fields["cut_piece_crew"] = () => CutArea.LastPiece is { } pid && Sim().Ships[pid] is { } p ? p.Crew.Assigned.Count : 0;
        // cut_set <field> <value>: sets one Cut tunable for this run (a slower cut for the video).
        Autotest.Commands["cut_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Cut, arg);
        // Parts that the ship lost since cut_mark: 0 until the split (no partial separation, spec 2465).
        Autotest.Fields["cut_parts_lost"] = () => _partsAtMark - Me().Parts.Count;
        // Design parts with no built part: 0 after the design trim when nothing is damaged (spec 2466).
        Autotest.Fields["cut_design_extra"] = () => Me().BlueprintParts.Count - Me().Parts.Count;
    }
}

using System.Globalization;
using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Cut;
using HotCargo.Dev;
using Halfling.Geometry;

namespace HotCargo.Weld;

/// <summary>Test commands of the weld (spec 2494). "me" is the welding ship, "target" the piece that it welds on.</summary>
static class WeldAutotest
{
    static ObjectID? _me, _target;
    static bool _blueprint;
    static WeldVerdict? _verdict;
    /// <summary>What weld_note saw of "me" and the target together: parts, crew, health and stored resources.</summary>
    static (int Parts, int Crew, long Health, long Stored) _noted;

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;
    static Ship Get(ObjectID? id, string what) => id is { } i ? Sim().Ships[i] ?? throw new InvalidOperationException($"the weld {what} is gone") : throw new InvalidOperationException($"no weld {what}; weld_mark and weld_target first");
    static Ship Me() => Get(_me, "ship");
    static Ship Target() => Get(_target, "target");
    static WeldOrder? Order() => _me is { } id ? Autotest.RequireRun().State.Weld.Orders.FirstOrDefault(o => o.Ship == id) : null;

    static long Stored(Ship s) => GameApp.Rules.Resources.Sum(r => (long)s.Resources.GetResourceTotal(r.ID, includeStored: true, includeCarried: false));
    static (int, int, long, long) Totals(params Ship[] ships) =>
        (ships.Sum(s => s.Parts.Count), ships.Sum(s => s.Crew.Assigned.Count), ships.Sum(s => s.Parts.Sum(p => (long)p.Health)), ships.Sum(Stored));

    /// <summary>
    /// The valid place nearest to where the target is now: the snapped turn first, then the smallest move, then by tile. Searches up to
    /// the weld range around the snapped place.
    /// </summary>
    static Placement NearestValid(Ship ship, Ship target, int? turns = null)
    {
        var snap = WeldShip.Snap(ship, target);
        int r = (int)Autotest.RequireRun().T.Weld.Range;
        var places = from turn in Enumerable.Range(0, 4)
                     from dx in Enumerable.Range(-r, 2 * r + 1)
                     from dy in Enumerable.Range(-r, 2 * r + 1)
                     let p = new Placement((snap.Turns + turn) & 3, snap.X + dx, snap.Y + dy)
                     where turns is null || p.Turns == turns
                     orderby turn, dx * dx + dy * dy, dy, dx
                     select p;
        foreach (var p in places)
            if (WeldShip.Check(ship, target, p).Verdict == WeldVerdict.Ok) return p;
        throw new InvalidOperationException($"no valid place for {target.Metadata.PlainShipName} on {ship.Metadata.PlainShipName}");
    }

    /// <summary>
    /// The valid place whose joints lie farthest from the ship's walkable tiles (the most tiles to the nearest one, for the joint
    /// nearest to them). Issue #346: the crew path grid covers only the walkable tiles and a margin.
    /// </summary>
    static Placement FarthestValid(Ship ship, Ship target)
    {
        var walkable = ship.Parts.Where(q => q.Rules.CrewSpeedFactor.IsNonZero).SelectMany(q => q.PhysicalRect.Cast<IntVector2>()).ToList();
        var shipTiles = ship.Parts.SelectMany(q => q.PhysicalRect.Cast<IntVector2>()).ToHashSet();
        int Far(Edge e) => (shipTiles.Contains(new IntVector2(e.A.X, e.A.Y)) ? e.A : e.B) is var c
            ? walkable.Min(w => Math.Max(Math.Abs(w.X - c.X), Math.Abs(w.Y - c.Y))) : 0;
        var bounds = ship.Parts.BoundingRect ?? throw new InvalidOperationException("the ship has no parts");
        var best = (from turn in Enumerable.Range(0, 4)
                    from x in Enumerable.Range(bounds.Left - 8, bounds.Width + 16)
                    from y in Enumerable.Range(bounds.Top - 8, bounds.Height + 16)
                    let p = new Placement(turn, x, y)
                    let check = WeldShip.Check(ship, target, p)
                    where check.Verdict == WeldVerdict.Ok
                    select (p, Far: check.Joints.Min(Far)))
            .OrderByDescending(c => c.Far).ThenBy(c => c.p.Turns).ThenBy(c => c.p.Y).ThenBy(c => c.p.X).FirstOrDefault();
        if (best.Far == 0) throw new InvalidOperationException($"no valid place for {target.Metadata.PlainShipName} away from the walkable tiles");
        Log.Info($"AUTOTEST weld: the farthest joints are {best.Far} tiles from a walkable tile");
        return best.p;
    }

    /// <summary>A test ship of vanilla parts in the sim, at a clear spot <paramref name="distance"/> to the right of <paramref name="near"/>.</summary>
    /// <summary>With <paramref name="at"/>, the ship goes exactly there (screenshots; the caller keeps the spot clear).</summary>
    static Ship Spawn(IReadOnlyList<ShipBuilder.PartAt> layout, string name, int playerIndex, Ship near, float distance, Vector2? at = null)
    {
        var sim = Sim();
        var ship = ShipBuilder.Build(near.Rules, layout, name, "weld test");
        ship.Metadata.PlayerIndex = playerIndex;
        ship.WorldCenter = at ?? Autotest.ClearSpotNear(sim, near.DetWorldCenter, distance, ship.WorldBoundingRadius);
        sim.Ships.Add(ship);
        ship.InitializeToMax();
        return ship;
    }

    /// <summary>A place that puts the target's first tile on the ship's first tile.</summary>
    static Placement CoveredPlace()
    {
        var shipTile = Me().Parts.OrderBy(q => q.Location.Y).ThenBy(q => q.Location.X).First().PhysicalRect.Location;
        var targetTile = Target().Parts.OrderBy(q => q.Location.Y).ThenBy(q => q.Location.X).First().PhysicalRect.Location;
        return new Placement(0, shipTile.X - targetTile.X, shipTile.Y - targetTile.Y);
    }

    internal static void Register()
    {
        // weld_mark cut|acting: "me" is the cut tests' ship or the acting player's first ship.
        Autotest.Commands["weld_mark"] = arg => _me = arg.Trim() switch
        {
            "cut" => CutAutotest.MeId ?? throw new InvalidOperationException("cut_mark or cut_twin first"),
            "acting" => (Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("the acting player has no ship")).UniqueID,
            var k => throw new InvalidOperationException($"weld_mark needs cut or acting, not '{k}'"),
        };
        // weld_target piece|nearest: the target is the newest piece that a cut split off, or the nearest target of "me".
        Autotest.Commands["weld_target"] = arg => _target = arg.Trim() switch
        {
            "piece" => CutArea.LastPiece ?? throw new InvalidOperationException("no cut piece yet"),
            "nearest" => WeldShip.Targets(Autotest.RequireRun(), Me(), Me().Metadata.PlayerIndex).FirstOrDefault()?.UniqueID
                ?? throw new InvalidOperationException("no weld target near the ship"),
            var k => throw new InvalidOperationException($"weld_target needs piece or nearest, not '{k}'"),
        };
        // weld_frame <size>: the camera on "me", this many world units across (screenshots).
        Autotest.Commands["weld_frame"] = arg =>
        {
            var view = Sim().Camera.ViewController;
            view.ClearFocus(clearFramesOfReferenceAndRotation: true);
            view.FRLocation = Me().WorldCenter;
            Autotest.FitCamera(Sim(), float.Parse(arg, CultureInfo.InvariantCulture));
        };
        // weld_fill <n>: test only, single player. Stores n steel in the target.
        // weld_build arm|piece: test only, single player.
        // arm: an own ship with a crewed core (control room, reactor, quarters, corridors, an airlock) and an arm of 12 armor tiles,
        // far out of the crew path grid (issue #346). It becomes "me".
        // piece: a derelict of structure, armor and wedges, which cannot all turn (issue #347), next to "me". It becomes the target.
        Autotest.Commands["weld_build"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("weld_build is single player only");
            var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (words.FirstOrDefault())
            {
                case "arm":
                    var home = Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("the acting player has no ship");
                    List<ShipBuilder.PartAt> arm =
                    [
                        // Corridors first: the automatic door rule adds doors only toward parts that the ship already has.
                        .. Enumerable.Range(0, 6).Select(y => new ShipBuilder.PartAt("cosmoteer.corridor", 2, y, 0)),
                        new("cosmoteer.crew_quarters_small", 1, 4, 0), new("cosmoteer.airlock", 2, 6, 0),
                        new("cosmoteer.control_room_small", 0, 0, 0), new("cosmoteer.reactor_small", 0, 2, 0),
                        // Up from the top corridor: a joint above the grid gives a negative index, which throws. Out to one
                        // side, the flat index of the grid array stays in range and the search reads wrong cells without a throw.
                        .. Enumerable.Range(1, 12).Select(i => new ShipBuilder.PartAt("cosmoteer.armor", 2, -i, 0)),
                    ];
                    // Far from the start ship and its neighbors, so the pieces find a clear spot next to it.
                    var me = Spawn(arm, "Arm", home.Metadata.PlayerIndex, home, 300f);
                    me.Crew.CreateCrewsAtRandomParts(me.Crew.TotalCrewCapacityWithConstruction);
                    _me = me.UniqueID;
                    Log.Info($"AUTOTEST weld: arm ship {me.UniqueID} with {me.Parts.Count} parts and {me.Crew.Assigned.Count} crew");
                    break;
                case "piece":
                    List<ShipBuilder.PartAt> piece =
                    [
                        new("cosmoteer.structure", 0, 0, 0), new("cosmoteer.structure", 1, 0, 0), new("cosmoteer.structure", 2, 0, 0),
                        new("cosmoteer.structure", 0, 1, 0), new("cosmoteer.structure", 1, 1, 0), new("cosmoteer.armor_wedge", 2, 1, 1),
                        new("cosmoteer.armor_2x1", 0, 2, 0),
                    ];
                    // weld_build piece <dx> <dy>: the piece at this offset from "me" in world units (screenshots with several pieces).
                    Vector2? at = words.Length == 3
                        ? Me().DetWorldCenter + new Vector2(float.Parse(words[1], CultureInfo.InvariantCulture), float.Parse(words[2], CultureInfo.InvariantCulture))
                        : null;
                    var target = Spawn(piece, "Piece", Cosmoteer.PlayerIndexes.Junk, Me(), 25f, at);
                    _target = target.UniqueID;
                    Log.Info($"AUTOTEST weld: derelict piece {target.UniqueID} with {target.Parts.Count} parts");
                    break;
                default: throw new InvalidOperationException($"weld_build needs arm or piece [dx dy], not '{arg.Trim()}'");
            }
        };
        Autotest.Commands["weld_fill"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("weld_fill is single player only");
            int added = Target().Resources.AddResources(new Cosmoteer.Data.ID<Cosmoteer.Resources.ResourceRules>("steel"), int.Parse(arg, CultureInfo.InvariantCulture), excludeConstructionSinks: true);
            Log.Info($"AUTOTEST weld: the target took {added} steel");
        };
        Autotest.Commands["weld_note"] = _ => _noted = Totals(Me(), Target());
        Autotest.Commands["weld_blueprint"] = arg => _blueprint = arg.Trim() == "on";
        // weld_auto [<turns>|far]: the nearest valid place (with the given quarter turns in the ship's grid; or the place whose joints lie
        // farthest from the walkable tiles) becomes an order through the MP input, as a click of the Weld tool does.
        Autotest.Commands["weld_auto"] = arg =>
        {
            var p = arg.Trim() switch
            {
                "" => NearestValid(Me(), Target()),
                "far" => FarthestValid(Me(), Target()),
                var t => NearestValid(Me(), Target(), int.Parse(t, CultureInfo.InvariantCulture)),
            };
            Log.Info($"AUTOTEST weld: place {p} (snapped {WeldShip.Snap(Me(), Target())})");
            Autotest.RequireRun().Game.NetManager.EnqueueInput(new WeldOrderInput(Me(), Target(), p, _blueprint));
        };
        // weld_try covered: the check of a place that puts the target's first tile on a tile of the ship (weld_verdict).
        Autotest.Commands["weld_try"] = arg =>
        {
            if (arg.Trim() != "covered") throw new InvalidOperationException("weld_try needs: covered");
            _verdict = WeldArea.Refusal(Me(), Target(), CoveredPlace());
            Log.Info($"AUTOTEST weld: covered place, verdict {_verdict?.ToString() ?? "Ok"}");
        };
        Autotest.Fields["weld_crew"] = () => Order()?.Joints.Sum(e => e.Job?.AssignedCrew.Count ?? 0) ?? 0;
        Autotest.Fields["weld_work"] = () => Order()?.Joints.Sum(e => e.Work) ?? 0;
        Autotest.Fields["weld_jobs"] = () => Order()?.Joints.Count(e => e.Job?.Ship is not null) ?? 0;
        // Screenshots, UI only. weld_tool: selects "me", opens build mode and picks the Weld tool. weld_show auto|covered: the ghost at the
        // nearest valid place, or on the ship's first tile (red). weld_set <field> <value>: one Weld tunable for this run.
        Autotest.Commands["weld_tool"] = _ =>
        {
            Sim().PlayerInput.SelectSingleShip(Me());
            Autotest.RequireRun().Game.Gui.ShipGui.BuildButton.IsSelected = true;
            WeldTool.Select();
        };
        Autotest.Commands["weld_show"] = arg =>
        {
            var p = arg.Trim() switch
            {
                "auto" => NearestValid(Me(), Target()),
                "covered" => CoveredPlace(),
                var k => throw new InvalidOperationException($"weld_show needs auto or covered, not '{k}'"),
            };
            WeldToolState.RequireCurrent().Show(Me(), Target(), p);
        };
        // weld_cancel: the ship's order is canceled through the MP input, as a right click of the Weld tool does.
        Autotest.Commands["weld_cancel"] = _ => Autotest.RequireRun().Game.NetManager.EnqueueInput(new WeldCancelInput(Me()));
        // weld_button apply|discard|cancelall: the vanilla buttons "Make it so", "Discard changes" and "Cancel all" of "me" (spec 2475).
        Autotest.Commands["weld_button"] = arg => CutAutotest.Buttons(Me(), arg.Trim() switch
        {
            "apply" => "_constructBtn",
            "discard" => "_discardBtn",
            "cancelall" => "_cancelAllBtn",
            var k => throw new InvalidOperationException($"weld_button needs apply, discard or cancelall, not '{k}'"),
        });
        // weld_pick: the Weld tool picks the target, as a click on it does. weld_esc: Esc in the Weld tool (back to picking).
        // weld_hover target|none: the cursor stands on the target's middle, or off every ship (screenshots).
        Autotest.Commands["weld_pick"] = _ => WeldToolState.RequireCurrent().PickByCommand(Me(), Target());
        Autotest.Commands["weld_esc"] = _ => Log.Info($"AUTOTEST weld: Esc reset the tool: {WeldToolState.RequireCurrent().TryReset()}");
        Autotest.Commands["weld_hover"] = arg => WeldToolState.RequireCurrent().HoverAt = arg.Trim() switch
        {
            "target" => Target().DetWorldCenter,
            "none" => null,
            var k => throw new InvalidOperationException($"weld_hover needs target or none, not '{k}'"),
        };
        Autotest.Fields["weld_tool_picked"] = () => WeldToolState.RequireCurrent().IsPicking ? 0 : 1;
        // Spec 2484: the target's speed relative to the ship at its point (a canceled pull leaves it floating).
        Autotest.Fields["weld_target_relspeed"] = () =>
        {
            var body = Target().Physics.Body;
            return (body.LinearVelocity - Me().Physics.Body.GetLinearVelocityFromWorldPoint(body.WorldCenter)).Length();
        };
        Autotest.Commands["weld_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Weld, arg);
        // Verdicts: 0 Ok, 1 Covered, 2 NoContact (WeldVerdict).
        Autotest.Fields["weld_verdict"] = () => (double)(_verdict ?? WeldVerdict.Ok);
        Autotest.Fields["weld_orders"] = () => Autotest.RequireRun().State.Weld.Orders.Count;
        Autotest.Fields["weld_pending"] = () => Order() is { Pending: true } ? 1 : 0;
        // -1 no order, 0 pull, 1 join.
        Autotest.Fields["weld_phase"] = () => Order() is { } o ? (double)o.Phase : -1;
        Autotest.Fields["weld_joints_left"] = () => Order()?.Joints.Count(e => !e.Done) ?? 0;
        Autotest.Fields["weld_merges"] = () => Autotest.RequireRun().State.Weld.Merges;
        Autotest.Fields["weld_target_live"] = () => _target is { } t && Sim().Ships[t] is not null ? 1 : 0;
        // Spec 2487: after the merge, "me" alone holds what weld_note saw of both (1 when equal).
        Autotest.Fields["weld_parts_kept"] = () => Me().Parts.Count == _noted.Parts ? 1 : 0;
        Autotest.Fields["weld_crew_kept"] = () => Me().Crew.Assigned.Count == _noted.Crew ? 1 : 0;
        Autotest.Fields["weld_health_kept"] = () => Me().Parts.Sum(p => (long)p.Health) == _noted.Health ? 1 : 0;
        Autotest.Fields["weld_stored_kept"] = () => Stored(Me()) >= _noted.Stored ? 1 : 0;
    }
}

using System.Globalization;
using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Cut;
using HotCargo.Dev;

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
    static Placement NearestValid(Ship ship, Ship target)
    {
        var snap = WeldShip.Snap(ship, target);
        int r = (int)Autotest.RequireRun().T.Weld.Range;
        var places = from turn in Enumerable.Range(0, 4)
                     from dx in Enumerable.Range(-r, 2 * r + 1)
                     from dy in Enumerable.Range(-r, 2 * r + 1)
                     let p = new Placement((snap.Turns + turn) & 3, snap.X + dx, snap.Y + dy)
                     orderby turn, dx * dx + dy * dy, dy, dx
                     select p;
        foreach (var p in places)
            if (WeldShip.Check(ship, target, p).Verdict == WeldVerdict.Ok) return p;
        throw new InvalidOperationException($"no valid place for {target.Metadata.PlainShipName} on {ship.Metadata.PlainShipName}");
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
        // weld_target piece: the target is the newest piece that a cut split off.
        Autotest.Commands["weld_target"] = arg => _target = arg.Trim() == "piece"
            ? CutArea.LastPiece ?? throw new InvalidOperationException("no cut piece yet")
            : throw new InvalidOperationException("weld_target needs: piece");
        // weld_fill <n>: test only, single player. Stores n steel in the target.
        Autotest.Commands["weld_fill"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("weld_fill is single player only");
            int added = Target().Resources.AddResources(new Cosmoteer.Data.ID<Cosmoteer.Resources.ResourceRules>("steel"), int.Parse(arg, CultureInfo.InvariantCulture), excludeConstructionSinks: true);
            Log.Info($"AUTOTEST weld: the target took {added} steel");
        };
        Autotest.Commands["weld_note"] = _ => _noted = Totals(Me(), Target());
        Autotest.Commands["weld_blueprint"] = arg => _blueprint = arg.Trim() == "on";
        // weld_auto: the nearest valid place becomes an order through the MP input, as a click of the Weld tool does.
        Autotest.Commands["weld_auto"] = _ =>
        {
            var p = NearestValid(Me(), Target());
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

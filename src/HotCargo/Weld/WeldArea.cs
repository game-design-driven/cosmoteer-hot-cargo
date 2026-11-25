using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Cut;

namespace HotCargo.Weld;

/// <summary>
/// Entry point of the Weld area (spec 2480 to 2494). The player places a ghost of a target in the build window (<see cref="WeldTool"/>);
/// an MP input makes it an order; the sim tick pulls the target into place, keeps one crew job for each joint edge and merges the
/// target into the ship when every joint is done.
/// </summary>
public static class WeldArea
{
    /// <summary>
    /// Spec 2488: ships that a weld joined to another ship in this session. The ship-loss alert (Ux/ShipAlerts) skips them, since a
    /// weld is not a loss. Not run state: nothing in the sim reads it.
    /// </summary>
    internal static readonly HashSet<ObjectID> Welded = [];

    public static void Register()
    {
        Run.Hooks.Tick += (run, sim, dt) => Errors.Guard("weld tick", () => OnTick(run, sim, (float)dt));
        WeldTool.Register();
        WeldDraw.Register();
        WeldAutotest.Register();
        CutApplyHooks.Edits.Add(new WeldEdits());
    }

    /// <summary>Spec 2484: weld orders follow the vanilla edit flow, as cuts do (spec 2475).</summary>
    sealed class WeldEdits : IShipEdits
    {
        static WeldOrder? OrderOf(Run run, Ship ship) => run.State.Weld.Orders.FirstOrDefault(o => o.Ship == ship.UniqueID);
        public bool Pending(Run run, Ship ship) => OrderOf(run, ship) is { Pending: true };
        public bool Running(Run run, Ship ship) => OrderOf(run, ship) is { Pending: false };
        public Cosmoteer.Game.Multiplayer.MPInput Apply(Ship ship, bool discard) => new WeldApplyInput(ship, discard);
        public Cosmoteer.Game.Multiplayer.MPInput Cancel(Ship ship) => new WeldCancelInput(ship);
    }

    /// <summary>Spec 2483: why the target cannot go to the place now, or null when it can.</summary>
    internal static WeldVerdict? Refusal(Ship ship, Ship target, Placement p) =>
        WeldShip.Check(ship, target, p).Verdict is var v && v == WeldVerdict.Ok ? null : v;

    /// <summary>Spec 2484: makes or replaces the ship's order. Sim only (an MP input).</summary>
    internal static bool Order(Run run, Ship ship, Ship target, Placement p, int playerIndex, bool pending)
    {
        if (!run.T.Weld.Enabled || !WeldShip.IsTarget(run, ship, target, playerIndex) || Refusal(ship, target, p) is not null) return false;
        var orders = run.State.Weld.Orders;
        if (orders.FirstOrDefault(o => o.Ship == ship.UniqueID) is { } old) End(ship, old, orders);
        // A target belongs to one order: a second ship's order on it replaces the first.
        if (orders.FirstOrDefault(o => o.Target == target.UniqueID) is { } other) End(ship.Sim.Ships[other.Ship], other, orders);
        orders.Add(new WeldOrder { Ship = ship.UniqueID, Target = target.UniqueID, Turns = p.Turns, X = p.X, Y = p.Y, PullSince = run.State.GameSeconds, Pending = pending });
        Log.Info($"weld: {(pending ? "pending " : "")}order on {ship.Metadata.PlainShipName} for {target.Metadata.PlainShipName} at {p}");
        return true;
    }

    /// <summary>Spec 2475: "Make it so" applies the ship's pending weld; the pull starts now. Sim only.</summary>
    internal static void Apply(Run run, Ship ship)
    {
        if (run.State.Weld.Orders.FirstOrDefault(o => o.Ship == ship.UniqueID && o.Pending) is not { } order) return;
        order.Pending = false;
        order.PullSince = run.State.GameSeconds;
        Log.Info($"weld: order on {ship.Metadata.PlainShipName} applied");
    }

    /// <summary>Spec 2475: "Discard changes" drops the ship's pending weld. Sim only.</summary>
    internal static void Discard(Run run, Ship ship)
    {
        var orders = run.State.Weld.Orders;
        if (orders.FirstOrDefault(o => o.Ship == ship.UniqueID && o.Pending) is not { } order) return;
        End(ship, order, orders);
        Log.Info($"weld: pending order on {ship.Metadata.PlainShipName} discarded");
    }

    /// <summary>Spec 2484: cancels the ship's order. Sim only.</summary>
    internal static void Cancel(Run run, Ship ship)
    {
        var orders = run.State.Weld.Orders;
        if (orders.FirstOrDefault(o => o.Ship == ship.UniqueID) is not { } order) return;
        End(ship, order, orders);
        Log.Info($"weld: order on {ship.Metadata.PlainShipName} canceled");
    }

    static void End(Ship? ship, WeldOrder order, List<WeldOrder> orders)
    {
        foreach (var e in order.Joints) CutArea.DropJob(ship, e);
        if (ship is not null && ship.Sim.Ships[order.Target] is var target)
        {
            WeldShip.PassThrough(ship, target, on: false);
            if (target is not null && !order.Pending) WeldShip.Release(ship, target);
        }
        orders.Remove(order);
    }

    static void EndWithLine(SimRoot sim, Ship ship, WeldOrder order, List<WeldOrder> orders, string key, string why)
    {
        End(ship, order, orders);
        Log.Info($"weld: order on {ship.Metadata.PlainShipName} ended: {why}");
        if (ship.IsHumanPlayerShip) Ux.UxKit.ChatLine(sim.RequireGame(), Strings.GetText(key));
    }

    static void OnTick(Run run, SimRoot sim, float dt)
    {
        var orders = run.State.Weld.Orders;
        foreach (var order in orders.ToList())
        {
            var ship = sim.Ships[order.Ship];
            var target = sim.Ships[order.Target];
            if (ship is null || target is null)
            {
                // Spec 2489: the ship or the target left this system.
                End(ship, order, orders);
                Log.Info("weld: an order ended, its ship or target is not in this system");
                continue;
            }
            if (!run.T.Weld.Enabled || order.Pending) continue;
            WeldShip.PassThrough(ship, target, on: true);
            var p = order.Placement;
            if (order.Phase == WeldPhase.Pull)
            {
                if (run.State.GameSeconds - order.PullSince > run.T.Weld.PullSeconds)
                {
                    EndWithLine(sim, ship, order, orders, "HotCargoWeld/NoPull", "the pull took too long");
                    continue;
                }
                if (!WeldShip.Pull(ship, target, p, run.T.Weld, dt)) continue;
                var check = WeldShip.Check(ship, target, p);
                if (check.Verdict != WeldVerdict.Ok)
                {
                    EndWithLine(sim, ship, order, orders, "HotCargoWeld/Blocked", $"the place is no longer valid ({check.Verdict})");
                    continue;
                }
                var layout = WeldShip.Place(ship, target, p);
                order.Joints = check.Joints.Select(e =>
                {
                    var work = layout.Both.PartAt(e.A) is { } a && a < layout.ShipParts ? e.A : e.B;
                    return new CutEdge { X = e.X, Y = e.Y, Down = e.Down, WorkX = work.X, WorkY = work.Y };
                }).ToList();
                order.Phase = WeldPhase.Join;
                Log.Info($"weld: {target.Metadata.PlainShipName} holds at its place on {ship.Metadata.PlainShipName}, {order.Joints.Count} joints");
                continue;
            }
            WeldShip.Hold(ship, target, p);
            var both = WeldShip.Place(ship, target, p);
            CutArea.KeepJobs(ship, order.Joints, e => both.Both.NeedsWork(e), r => r.T.Weld.EdgeSeconds, "HotCargoWeld/JobName", "weld");
            if (!order.Joints.All(e => e.Done)) continue;
            if (WeldShip.Check(ship, target, p).Verdict == WeldVerdict.Covered)
            {
                EndWithLine(sim, ship, order, orders, "HotCargoWeld/Blocked", "a part is in the way");
                continue;
            }
            End(ship, order, orders);
            string name = target.Metadata.PlainShipName;
            int parts = target.Parts.Count;
            Welded.Add(target.UniqueID);
            try
            {
                WeldShip.Merge(ship, target, p);
            }
            catch (Exception ex)
            {
                // A failed merge changes nothing (WeldShip.Merge checks first and undoes its parts). The error fails every test, and in
                // play the piece stays a free object.
                Welded.Remove(target.UniqueID);
                Errors.Report("weld merge", ex.Message, ex);
                if (ship.IsHumanPlayerShip) Ux.UxKit.ChatLine(sim.RequireGame(), Strings.GetText("HotCargoWeld/Failed"));
                continue;
            }
            CutDraw.Clunk(); // determinism: a sound only, nothing in the sim reads it
            run.State.Weld.Merges++;
            Log.Info($"weld: {name} ({parts} parts) joined {ship.Metadata.PlainShipName}, now {ship.Parts.Count} parts");
        }
    }
}

using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>
/// Entry point of the Cut area (spec 2460 to 2472). The player draws a cut line in the build window (<see cref="CutTool"/>); an MP input
/// makes it an order; the sim tick keeps one crew job for each edge that needs work and splits the ship when every edge is done.
/// </summary>
public static class CutArea
{
    public static void Register()
    {
        Run.Hooks.Tick += (run, sim, dt) => Errors.Guard("cut tick", () => OnTick(run, sim));
        CutDraw.Register();
        CutTool.Register();
        CutAutotest.Register();
    }

    /// <summary>Spec 2462, 2470: why a line cannot become an order on the ship now, or null when it can.</summary>
    internal static CutVerdict? Refusal(Ship ship, IReadOnlyList<Edge> edges, out CutCheck? check)
    {
        check = null;
        if (!ship.IsConstructionLegal || ship.Ftl.IsPreparingToJump) return CutVerdict.NotNow;
        check = CutModel.Check(CutShip.Grid(ship.Parts).Grid, edges);
        return check.Verdict == CutVerdict.Ok ? null : check.Verdict;
    }

    /// <summary>Spec 2463: makes or replaces the ship's order. Sim only (an MP input or a test command in single player).</summary>
    internal static bool Order(Run run, Ship ship, IReadOnlyList<Edge> edges)
    {
        if (!run.T.Cut.Enabled || Refusal(ship, edges, out var check) is not null || check is null) return false;
        var layout = CutShip.Grid(ship.Parts);
        int stay = CutModel.StayPiece(check.Pieces, i => layout.Parts[i].Rules.ContiguityOriginPriority, i => CutShip.Area(layout.Parts[i]));
        var orders = run.State.Cut.Orders;
        var old = orders.FirstOrDefault(o => o.Ship == ship.UniqueID);
        var order = new CutOrder { Ship = ship.UniqueID };
        foreach (var e in edges.Distinct())
        {
            var work = CutModel.WorkCell(layout.Grid, check.Pieces, stay, e);
            var kept = old?.Edges.FirstOrDefault(o => o.Edge == e);
            order.Edges.Add(new CutEdge
            {
                X = e.X, Y = e.Y, Down = e.Down, WorkX = work.X, WorkY = work.Y,
                Work = kept?.Work ?? 0f,
                Done = !check.Work.Contains(e),
            });
        }
        if (old is not null) End(ship, old, orders);
        orders.Add(order);
        Log.Info($"cut: order on {ship.Metadata.PlainShipName}, {check.Work.Count} edges need work{(old is null ? "" : " (replaces the old order)")}");
        return true;
    }

    /// <summary>Spec 2463: cancels the ship's order. Sim only.</summary>
    internal static void Cancel(Run run, Ship ship)
    {
        var orders = run.State.Cut.Orders;
        if (orders.FirstOrDefault(o => o.Ship == ship.UniqueID) is not { } order) return;
        End(ship, order, orders);
        Log.Info($"cut: order on {ship.Metadata.PlainShipName} canceled");
    }

    static void End(Ship? ship, CutOrder order, List<CutOrder> orders)
    {
        foreach (var e in order.Edges) DropJob(ship, e);
        orders.Remove(order);
    }

    static void DropJob(Ship? ship, CutEdge e)
    {
        if (e.Job is { } job && ship is not null && job.Ship == ship) ship.Jobs.RemoveJob(job, unassignCrew: true);
        e.Job = null;
    }

    static void OnTick(Run run, SimRoot sim)
    {
        var orders = run.State.Cut.Orders;
        foreach (var order in orders.ToList())
        {
            var ship = sim.Ships[order.Ship];
            if (ship is null)
            {
                // Spec 2467: the ship is gone or in another system.
                End(null, order, orders);
                Log.Info("cut: an order ended, its ship is not in this system");
                continue;
            }
            if (!run.T.Cut.Enabled) continue;
            foreach (var e in order.Edges)
            {
                if (!e.Done && !CutShip.NeedsWork(ship, e.Edge))
                {
                    e.Done = true;
                    Log.Info($"cut: edge {e.X},{e.Y} {(e.Down ? "down" : "right")} on {ship.Metadata.PlainShipName} needs no more work at {e.Work:0.0} s");
                }
                if (e.Done) DropJob(ship, e);
                else if (e.Job?.Ship != ship)
                {
                    e.Job = new CutJob(e);
                    ship.Jobs.AddJob(e.Job);
                }
            }
            if (order.Edges.All(e => e.Done)) Finish(run, sim, ship, order, orders);
        }
    }

    /// <summary>Spec 2465 to 2467: the split and the design trim when every edge is done.</summary>
    static void Finish(Run run, SimRoot sim, Ship ship, CutOrder order, List<CutOrder> orders)
    {
        End(ship, order, orders);
        var gaps = order.Edges.Select(e => e.Edge).ToHashSet();
        int before = sim.Ships.Count;
        if (!CutShip.Split(ship, gaps))
        {
            Log.Info($"cut: {ship.Metadata.PlainShipName} did not split");
            if (ship.IsHumanPlayerShip) Ux.UxKit.ChatLine(sim.RequireGame(), Cosmoteer.Localization.Strings.GetText("HotCargoCut/NoSplit"));
            return;
        }
        int trimmed = CutShip.TrimDesign(ship, gaps);
        run.State.Cut.Splits++;
        Log.Info($"cut: {ship.Metadata.PlainShipName} split into {sim.Ships.Count - before + 1} ships, the design lost {trimmed} parts");
    }
}

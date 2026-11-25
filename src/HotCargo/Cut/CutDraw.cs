using Cosmoteer;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>
/// Spec 2468: draws the line of every cut order on its ship in the world, with the game's remove line (BuildGuiRules.RemovePartXLine).
/// Edges that need work are bright, done edges dim. Draws only; reads the run state and never changes it.
/// </summary>
static class CutDraw
{
    static SimRoot? _sim;

    internal static void Register() => Run.Hooks.Frame += run => Ux.UxKit.Guard("cut draw", () =>
    {
        // The overlay renderer belongs to the sim, and a jump makes a new sim: subscribe to each new one.
        if (run.Game.Sim is not { } sim || sim == _sim) return;
        _sim = sim;
        sim.Overlays.DrawingCustomOverlays += (_, _) => Ux.UxKit.Guard("cut draw orders", () => DrawOrders(sim));
    });

    static void DrawOrders(SimRoot sim)
    {
        if (Run.Current is not { } run || run.Game.Sim != sim) return;
        var line = run.Game.Rules.GameGui.Build.RemovePartXLine;
        foreach (var order in run.State.Cut.Orders)
        {
            if (sim.Ships[order.Ship] is not { } ship) continue;
            using (App.Graphics.PushTransformMultiply(ship.WorldTransform))
                foreach (var e in order.Edges) DrawEdge(line, e.Edge, e.Done ? Color.White * 0.3f : Color.White);
        }
    }

    /// <summary>One edge, in ship space (the caller pushes the ship transform).</summary>
    internal static void DrawEdge(Cosmoteer.Simulation.Overlays.CappedLine line, Edge e, Color color)
    {
        var (a, b) = e.Down ? (new Vector2(e.X, e.Y + 1), new Vector2(e.X + 1, e.Y + 1)) : (new Vector2(e.X + 1, e.Y), new Vector2(e.X + 1, e.Y + 1));
        line.Draw(a, b, color, 0.5f);
    }
}

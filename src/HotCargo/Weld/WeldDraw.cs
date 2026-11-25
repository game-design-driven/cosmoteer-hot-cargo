using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Cut;

namespace HotCargo.Weld;

/// <summary>
/// Spec 2490: draws the joint edges of every weld that holds its target, in the cut's art (a torch glow, sparks where crew work, a
/// seam when done). Draws only; reads the run state and never changes it.
/// </summary>
static class WeldDraw
{
    static SimRoot? _sim;

    internal static void Register() => Run.Hooks.Frame += run => Ux.UxKit.Guard("weld draw", () =>
    {
        // The overlay renderer belongs to the sim, and a jump makes a new sim: subscribe to each new one.
        if (run.Game.Sim is not { } sim || sim == _sim) return;
        _sim = sim;
        sim.Overlays.DrawingCustomOverlays += (_, _) => Ux.UxKit.Guard("weld draw joints", () =>
        {
            if (Run.Current is not { } r || r.Game.Sim != sim) return;
            foreach (var order in r.State.Weld.Orders)
            {
                if (sim.Ships[order.Ship] is not { } ship) continue;
                if (order.Phase == WeldPhase.Join) CutDraw.DrawEdges(ship, order.Joints);
                else if (order.Pending && sim.Ships[order.Target] is { } target)
                    // Spec 2475: a pending weld shows its joints as the pending cut lines do.
                    using (Halfling.App.Graphics.PushTransformMultiply(ship.WorldTransform))
                        CutDraw.DrawDrag(WeldShip.Check(ship, target, order.Placement).Joints);
            }
        });
    });
}

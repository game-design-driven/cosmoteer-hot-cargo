using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Geometry;
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
                else if (sim.Ships[order.Target] is { } target)
                {
                    // Spec 2475, 2490: a pending weld shows its ghost with the other design edits; during the pull the joints show
                    // where the piece goes, as the pending cut lines do.
                    var check = WeldShip.Check(ship, target, order.Placement);
                    if (order.Pending) Ghost(ship, target, order.Placement, check.Verdict == WeldVerdict.Ok);
                    using (Halfling.App.Graphics.PushTransformMultiply(ship.WorldTransform))
                        CutDraw.DrawDrag(check.Joints);
                }
            }
        });
    });

    /// <summary>
    /// Spec 2482: the target's parts at a place on the ship, in the art of the vanilla part brush (PartRules.DrawBlueprint, as
    /// BaseAddPartsState draws a part at the cursor): blue when <paramref name="valid"/>, red when not.
    /// </summary>
    internal static void Ghost(Ship ship, Ship target, Placement p, bool valid)
    {
        var placed = WeldShip.PlaceParts(ship, target, p);
        var byCell = new Dictionary<IntVector2, PartInfo>();
        foreach (var (_, info) in placed)
            foreach (IntVector2 c in info.Rect) byCell[c] = info;
        using (Halfling.App.Graphics.PushTransformMultiply(ship.WorldTransform))
            foreach (var (_, info) in placed)
            {
                var at = info;
                // Neighbors decide the joined look of armor and walls: the other ghost parts first, then the ship's design.
                at.Rules.DrawBlueprint(at.Location, at.Rotation * MathF.PI / 2f, at.Rotation, at.FlipX, valid, isAtCursor: true,
                    (loc, prt) => byCell.TryGetValue(loc + at.Location, out var other) ? other
                        : ship.BlueprintParts[loc + at.Location, prt] is { } bp ? new PartInfo(bp) : null,
                    _ => null, ship);
            }
    }
}

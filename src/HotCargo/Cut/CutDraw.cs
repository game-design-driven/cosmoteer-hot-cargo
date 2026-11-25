using Cosmoteer;
using System.Runtime.CompilerServices;
using Cosmoteer.Simulation.Overlays;
using Halfling.ObjectText;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>
/// Spec 2468: draws the line of every cut order on its ship in the world, in the game's own art (mod/cut/lines.rules): an edge that
/// waits for work glows like the crew salvage beam (a cutting torch) with a stitch across it; where crew work, a spark flickers; a done
/// edge fades from the glow to a dark seam. The Cut tool draws the line in progress as marching dashes, and a blocked edge as the
/// remove tool's red X. Draws only; reads the run state and never changes it.
/// </summary>
static class CutDraw
{
    sealed class Art(CappedLine torch, CappedLine seam, CappedLine drag, ISprite spark)
    {
        public readonly CappedLine Torch = torch, Seam = seam, Drag = drag;
        public readonly ISprite Spark = spark;
    }

    /// <summary>What the eye saw of an edge in earlier frames: its work and when it last grew, and when it was first seen done.</summary>
    sealed class Look
    {
        public float Work;
        public double WorkedAt = double.NegativeInfinity;
        public double? DoneAt;
    }

    static SimRoot? _sim;
    static Art? _art;
    static readonly ConditionalWeakTable<CutEdge, Look> Looks = new();

    // Thickness of each line in tiles. Each texture is mostly glow, so the bright core is about a third of it.
    const float TorchThickness = 1.3f, SeamThickness = 0.9f, StitchThickness = 0.55f, DragThickness = 1.7f;
    const double FadeSeconds = 0.8, SparkSeconds = 0.3;
    static readonly Color Stitch = new(1f, 0.85f, 0.55f, 0.9f), SeamColor = new(0.04f, 0.04f, 0.05f, 0.85f), DragColor = new(1f, 0.72f, 0.3f);

    static Art LoadArt()
    {
        var file = new OTFile(Path.Combine(Mod.ModRoot, "cut/lines.rules"));
        T Read<T>(string name) where T : class => App.ObjectTextSerializer.ReadFromPath<T>(file, name) ?? throw new InvalidDataException($"{name} missing in cut/lines.rules");
        return new(Read<CappedLine>("Torch"), Read<CappedLine>("Seam"), Read<CappedLine>("Drag"), Read<ISprite>("Spark"));
    }

    internal static void Register() => Run.Hooks.Frame += run => Ux.UxKit.Guard("cut draw", () =>
    {
        // The overlay renderer belongs to the sim, and a jump makes a new sim: subscribe to each new one.
        if (run.Game.Sim is not { } sim || sim == _sim) return;
        _sim = sim;
        sim.Overlays.DrawingCustomOverlays += (_, _) => Ux.UxKit.Guard("cut draw orders", () => DrawOrders(sim));
    });

    static double Now => App.Clock.Time; // determinism: animation time of a drawing, never read by the sim

    static void DrawOrders(SimRoot sim)
    {
        if (Run.Current is not { } run || run.Game.Sim != sim) return;
        var art = _art ??= LoadArt();
        foreach (var order in run.State.Cut.Orders)
        {
            if (sim.Ships[order.Ship] is not { } ship) continue;
            using (App.Graphics.PushTransformMultiply(ship.WorldTransform))
                foreach (var e in order.Edges) DrawOrderEdge(art, e);
        }
    }

    static (Vector2 A, Vector2 B) Ends(Edge e) =>
        e.Down ? (new Vector2(e.X, e.Y + 1), new Vector2(e.X + 1, e.Y + 1)) : (new Vector2(e.X + 1, e.Y), new Vector2(e.X + 1, e.Y + 1));

    /// <summary>A short line across the edge at its middle: the stitch that marks where to cut.</summary>
    static void DrawStitch(CappedLine line, Edge e, Color color)
    {
        var (a, b) = Ends(e);
        var mid = (a + b) * 0.5f;
        var across = e.Down ? new Vector2(0f, 0.24f) : new Vector2(0.24f, 0f);
        line.Draw(mid - across, mid + across, color, StitchThickness);
    }

    /// <summary>A flickering spark at the edge's middle while crew work on it.</summary>
    static void DrawSpark(ISprite spark, Edge e, double now)
    {
        var (a, b) = Ends(e);
        var mid = (a + b) * 0.5f;
        float phase = (float)(now * 23.0) + e.X * 1.7f + e.Y * 2.9f;
        float size = 1.2f + 0.5f * MathF.Abs(MathF.Sin(phase));
        var quad = new Quad(Rect.CenteredOn(mid, new Vector2(size, size)), phase * 0.6f, spark);
        quad.MultiplyColor(new Color(1f, 0.95f, 0.8f, 0.75f + 0.25f * MathF.Sin(phase * 1.3f)));
        App.Graphics.Draw<Quad>(quad);
    }

    static void DrawOrderEdge(Art art, CutEdge e)
    {
        // An edge that was done before any work (in space, no contact, a loose end) shows nothing: there is nothing to cut there.
        if (e.Done && e.Work <= 0f) return;
        double now = Now;
        var look = Looks.GetValue(e, _ => new Look());
        if (e.Work > look.Work)
        {
            look.Work = e.Work;
            look.WorkedAt = now;
        }
        if (e.Done) look.DoneAt ??= now;
        var (a, b) = Ends(e.Edge);
        float glow = look.DoneAt is { } doneAt ? (float)Math.Clamp(1.0 - (now - doneAt) / FadeSeconds, 0.0, 1.0) : 1f;
        if (look.DoneAt is not null) art.Seam.Draw(a, b, SeamColor * (1f - glow), SeamThickness);
        if (glow <= 0f) return;
        bool working = now - look.WorkedAt < SparkSeconds;
        float pulse = working ? 1f : 0.8f + 0.2f * MathF.Sin((float)(now * 3.0) + e.X + e.Y);
        art.Torch.Draw(a, b, Color.White * (pulse * glow), TorchThickness);
        if (look.DoneAt is null) DrawStitch(art.Seam, e.Edge, Stitch);
        if (working && look.DoneAt is null) DrawSpark(art.Spark, e.Edge, now);
    }

    /// <summary>Spec 2461: the line in progress, as marching dashes with stitches, in ship space.</summary>
    internal static void DrawDrag(IEnumerable<Edge> edges)
    {
        var art = _art ??= LoadArt();
        float march = (float)(-Now * 1.5);
        foreach (var e in edges)
        {
            var (a, b) = Ends(e);
            art.Drag.Draw(a, b, DragColor, DragThickness, march);
            DrawStitch(art.Seam, e, Stitch);
        }
    }

    /// <summary>
    /// Spec 2461: the remove tool's red X over a blocked edge, in ship space, scaled like the game's
    /// (BaseBuildState.DrawRemovePartXRect: the square root of the camera scale).
    /// </summary>
    internal static void DrawBlocked(SimRoot sim, CappedLine line, Edge e)
    {
        var (a, b) = Ends(e);
        var c = (a + b) * 0.5f;
        const float r = 0.3f;
        float thickness = 3f * Mathx.Sqrt(sim.Camera.UniformScale);
        line.Draw(c + new Vector2(-r, -r), c + new Vector2(r, r), Color.White, thickness);
        line.Draw(c + new Vector2(r, -r), c + new Vector2(-r, r), Color.White, thickness);
    }
}

using Cosmoteer;
using System.Runtime.CompilerServices;
using Cosmoteer.Simulation.Overlays;
using Halfling.ObjectText;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Audio;
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
    sealed class Art(CappedLine torch, CappedLine seam, CappedLine drag, ISprite spark, ISoundEffect workSound, ISoundEffect clunk)
    {
        public readonly CappedLine Torch = torch, Seam = seam, Drag = drag;
        public readonly ISprite Spark = spark;
        public readonly ISoundEffect WorkSound = workSound, Clunk = clunk;
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
        return new(Read<CappedLine>("Torch"), Read<CappedLine>("Seam"), Read<CappedLine>("Drag"), Read<ISprite>("Spark"),
            Read<ISoundEffect>("WorkSound"), Read<ISoundEffect>("Clunk"));
    }

    internal static void Register() => Run.Hooks.Frame += run => Ux.UxKit.Guard("cut draw", () =>
    {
        WorkSound();
        // The overlay renderer belongs to the sim, and a jump makes a new sim: subscribe to each new one.
        if (run.Game.Sim is not { } sim || sim == _sim) return;
        _sim = sim;
        sim.Overlays.DrawingCustomOverlays += (_, _) => Ux.UxKit.Guard("cut draw orders", () => DrawOrders(sim));
    });

    static double Now => App.Clock.Time; // determinism: animation time of a drawing, never read by the sim

    static void DrawOrders(SimRoot sim)
    {
        if (Run.Current is not { } run || run.Game.Sim != sim) return;
        foreach (var order in run.State.Cut.Orders)
        {
            if (sim.Ships[order.Ship] is not { } ship) continue;
            if (!order.Pending)
            {
                DrawEdges(ship, order.Edges);
                continue;
            }
            // Spec 2475: a pending cut shows as the line it was drawn as, with the other pending design edits.
            using (App.Graphics.PushTransformMultiply(ship.WorldTransform))
                DrawDrag(order.Edges.Where(e => !e.Done).Select(e => e.Edge));
        }
    }

    /// <summary>Spec 2468, 2490: the edges of a cut or a weld on a ship, in the world.</summary>
    internal static void DrawEdges(Ship ship, IEnumerable<CutEdge> edges)
    {
        var art = _art ??= LoadArt();
        bool inView = ship.Sim?.WorldViewRect.Contains(ship.WorldCenter) == true;
        using (App.Graphics.PushTransformMultiply(ship.WorldTransform))
            foreach (var e in edges)
                if (DrawOrderEdge(art, e) && inView) _workSeenAt = Now;
    }

    static SoundInstance? _workLoop;
    /// <summary>When an edge of a cut or a weld on a ship in view last showed crew at work.</summary>
    static double _workSeenAt = double.NegativeInfinity;

    /// <summary>
    /// Spec 2474: the crew construction sound loops while crew work on an edge of a ship in view, one loop for all cuts. Crew inside a
    /// ship get no construction beams, and only the beams carry the vanilla sound (SpaceCrew.RenewConstructionBeams).
    /// </summary>
    static void WorkSound()
    {
        bool working = Now - _workSeenAt < 0.2;
        var art = _art ??= LoadArt();
        if (working && _workLoop is null) _workLoop = App.Audio.Play(art.WorkSound, AudioGroups.Effects, loop: true);
        else if (!working && _workLoop is not null)
        {
            _workLoop.Stop();
            _workLoop = null;
        }
    }

    /// <summary>Spec 2465, 2474: the clunk of the separation. A sound only; the sim calls it at the split on every machine.</summary>
    internal static void Clunk()
    {
        var art = _art ??= LoadArt();
        App.Audio.Play(art.Clunk, AudioGroups.Effects);
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

    /// <summary>Draws one edge; returns true while crew work on it.</summary>
    static bool DrawOrderEdge(Art art, CutEdge e)
    {
        // An edge that was done before any work (in space, no contact, a loose end) shows nothing: there is nothing to cut there.
        if (e.Done && e.Work <= 0f) return false;
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
        if (glow <= 0f) return false;
        bool working = now - look.WorkedAt < SparkSeconds;
        float pulse = working ? 1f : 0.8f + 0.2f * MathF.Sin((float)(now * 3.0) + e.X + e.Y);
        art.Torch.Draw(a, b, Color.White * (pulse * glow), TorchThickness);
        if (look.DoneAt is null) DrawStitch(art.Seam, e.Edge, Stitch);
        if (working && look.DoneAt is null) DrawSpark(art.Spark, e.Edge, now);
        return working && look.DoneAt is null;
    }

    /// <summary>Spec 2461, 2475: the line in progress or a pending cut, as marching dashes with stitches, in ship space.</summary>
    internal static void DrawDrag(IEnumerable<Edge> edges, float alpha = 1f)
    {
        var art = _art ??= LoadArt();
        float march = (float)(-Now * 1.5);
        foreach (var e in edges)
        {
            var (a, b) = Ends(e);
            art.Drag.Draw(a, b, DragColor * alpha, DragThickness, march);
            DrawStitch(art.Seam, e, Stitch * alpha);
        }
    }

    /// <summary>
    /// Spec 2473: the tile edge nearest to a ship point (<paramref name="x"/>, <paramref name="y"/>), faint, and a mark at the nearest
    /// corner, in ship space.
    /// </summary>
    internal static void DrawHover(float x, float y)
    {
        var art = _art ??= LoadArt();
        int cx = (int)MathF.Round(x), cy = (int)MathF.Round(y);
        int fx = (int)MathF.Floor(x), fy = (int)MathF.Floor(y);
        // The nearer of the horizontal edge on the nearest grid row and the vertical edge on the nearest grid column.
        var edge = MathF.Abs(y - cy) <= MathF.Abs(x - cx) ? Edge.Along(new Corner(fx, cy), new Corner(fx + 1, cy)) : Edge.Along(new Corner(cx, fy), new Corner(cx, fy + 1));
        DrawDrag([edge], 0.9f);
        var c = new Vector2(cx, cy);
        const float r = 0.3f;
        art.Seam.Draw(c - new Vector2(r, 0f), c + new Vector2(r, 0f), Stitch, StitchThickness);
        art.Seam.Draw(c - new Vector2(0f, r), c + new Vector2(0f, r), Stitch, StitchThickness);
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

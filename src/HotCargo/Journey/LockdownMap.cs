using System.Text;
using Cosmoteer;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Ux;

namespace HotCargo.Journey;

/// <summary>Spec 2562: the lockdown line, closed systems and old lanes on the galaxy map. UI only: it reads sim state and changes nothing.</summary>
static class LockdownMap
{
    static readonly Color ClosedColor = new(1f, 0.15f, 0.1f, 0.9f);
    static readonly Color LaneColor = new(0.45f, 0.8f, 1f, 1f);

    static Run? For(CareerGameModeManager mode) => Run.Current is { } run && run.Mode == mode && Lockdown.Active(run) ? run : null;

    /// <summary>The tooltip lines of a node, each with a leading line break. Tests log them.</summary>
    internal static string Lines(Run run, MapNode node)
    {
        if (node == run.Game.CurrentMapNode) return "";
        var view = Lockdown.UiView(run);
        if (view.ClosedNodes.Contains(node))
        {
            float stored = run.State.NodeHeat.TryGetValue(node.UniqueID, out float h) ? h : 0f;
            return "\n" + Lockdown.Format("TipClosedFmt", HeatModel.OnEnterSystem(run.State.Heat, stored, 1f));
        }
        if (!view.Thresholds.TryGetValue(node, out float level)) return "";
        string line = "\n" + Lockdown.Format("TipClosesAtFmt", level);
        if (LineModel.SecondsTo(level, run.State.Heat, Lockdown.HeatPerSecond(run)) is { } s)
            line += Lockdown.Format("TipClosesInFmt", $"{(int)s / 60}:{(int)s % 60:00}");
        return line;
    }

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetMapNodeToolTip))]
    static class Tooltip
    {
        static readonly TipCache Cache = new();

        static void Postfix(CareerGameModeManager __instance, MapNode node, ref string? __result)
        {
            if (__result is null || For(__instance) is not { } run) return;
            string? result = __result;
            UxKit.Guard("lockdown tooltip", () => result += Cache.Get(run, node, () => Lines(run, node)));
            __result = result;
        }
    }

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetMapNodeText))]
    static class NodeText
    {
        static void Postfix(CareerGameModeManager __instance, MapNode node, StringBuilder sb)
        {
            if (For(__instance) is not { } run) return;
            // Spec 872: this runs for each map node in every frame while the map shows, so it has no closure.
            try
            {
                if (node != run.Game.CurrentMapNode && Lockdown.UiView(run).ClosedNodes.Contains(node))
                    sb.Append("\n<s16><bad>").Append(Lockdown.Text("TagClosed")).Append("</bad></s16>");
            }
            catch (Exception ex) { Errors.Report("lockdown map tag", ex.Message, ex); }
        }
    }

    /// <summary>A red ring on each closed system, and the line once per frame (drawn with the start system).</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.DrawMapNodeExtras))]
    static class NodeRing
    {
        static void Postfix(CareerGameModeManager __instance, MapNode node, float scale)
        {
            if (For(__instance) is not { } run) return;
            try
            {
                var rules = node.Rules;
                if (node != run.Game.CurrentMapNode && Lockdown.UiView(run).ClosedNodes.Contains(node))
                {
                    var sprite = rules.NodeFtlPulseSprite;
                    App.Graphics.SetToMaterial(sprite);
                    App.Graphics.Draw(new Quad(Rect.CenteredOn(node.Location, sprite.Size * (scale * 0.9f)), ClosedColor, sprite.UVRect));
                }
                if (RunMap.Band(node) == 0) DrawLine(run, rules, scale);
            }
            catch (Exception ex) { Errors.Report("lockdown map draw", ex.Message, ex); }
        }
    }

    /// <summary>The line: red dots (the vanilla jump pulse sprite) across the map, half a band behind the furthest band that the players entered.</summary>
    static void DrawLine(Run run, Cosmoteer.GalaxyMap.MapRules rules, float scale)
    {
        int furthest = RunMap.Step(run) - 1;
        if (furthest <= 0) return;
        var nodes = run.Game.Map.Nodes;
        float x = (furthest - 0.5f) * RunMapModel.BandLength;
        float top = nodes.Min(n => n.Location.Y) - RunMapModel.RowGap, bottom = nodes.Max(n => n.Location.Y) + RunMapModel.RowGap;
        var sprite = rules.NodeFtlPulseSprite;
        App.Graphics.SetToMaterial(sprite);
        for (float y = top; y <= bottom; y += 1.2f)
            App.Graphics.Draw(new Quad(Rect.CenteredOn(new Vector2(x, y), sprite.Size * (scale * 0.3f)), ClosedColor, sprite.UVRect));
    }

    /// <summary>Old lanes (spec 2551) in their own color, over the vanilla link line.</summary>
    [HarmonyPatch(typeof(MapNode), nameof(MapNode.DrawConnections))]
    static class Lanes
    {
        static void Postfix(MapNode __instance, float scale)
        {
            if (Run.Current is not { } run || run.Game.Map != __instance.Map || !RunMap.IsRunMap(run.Mode)) return;
            try
            {
                int band = RunMap.Band(__instance);
                foreach (var other in __instance.Connections)
                    if (RunMap.Band(other) > band && RunMapModel.Kind(band, RunMap.Band(other)) == LinkKind.OldLane)
                        __instance.Map.Nodes.DrawCourseLine(__instance.Location, other.Location, LaneColor);
            }
            catch (Exception ex) { Errors.Report("lockdown map lanes", ex.Message, ex); }
        }
    }
}

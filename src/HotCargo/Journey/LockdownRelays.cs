using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Ships.Special;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Journey;

/// <summary>
/// Spec 2565: the relay (FtlGate) to a closed system shows red in the world, on its screen indicator and on the local system map; the
/// relay to a choice under warning blinks red. UI only: it reads sim state and changes nothing.
/// </summary>
static class LockdownRelays
{
    static readonly Color Red = new(1f, 0.15f, 0.1f, 0.7f);

    enum Mark { None, Warned, Closed }

    static Mark MarkOf(FtlGate gate)
    {
        // FtlGate.Game can be null (vanilla reads it with ?.), so the sim and the run's map decide.
        if (Run.Current is not { } run || run.Game.Sim != gate.Sim || !Lockdown.Active(run) || run.Game.Map.Nodes[gate.DestinationID] is not { } dest) return Mark.None;
        if (Lockdown.UiView(run).ClosedNodes.Contains(dest)) return Mark.Closed;
        return run.State.Journey.Choices.TryGetValue(dest.UniqueID, out var c) && c.Warned > 0 ? Mark.Warned : Mark.None;
    }

    /// <summary>The vanilla relay color feeds the screen indicator and the local map marker (ShipIndicatorSource.MinimapSpriteColor).</summary>
    [HarmonyPatch(typeof(FtlGate), nameof(FtlGate.IndicatorColor), MethodType.Getter)]
    static class IndicatorColor
    {
        static void Postfix(FtlGate __instance, ref Color? __result)
        {
            try
            {
                var mark = MarkOf(__instance);
                if (mark == Mark.Closed || mark == Mark.Warned && Blink()) __result = Red;
            }
            catch (Exception ex) { Errors.Report("lockdown relay color", ex.Message, ex); }
        }
    }

    /// <summary>The relay name on its indicator: "Relay to X (CLOSED)".</summary>
    [HarmonyPatch(typeof(FtlGate), nameof(FtlGate.PlainIndicatorName), MethodType.Getter)]
    static class IndicatorName
    {
        static void Postfix(FtlGate __instance, ref string? __result)
        {
            try
            {
                if (__result is not null && MarkOf(__instance) == Mark.Closed) __result += " (" + Lockdown.Text("TagClosed") + ")";
            }
            catch (Exception ex) { Errors.Report("lockdown relay name", ex.Message, ex); }
        }
    }

    /// <summary>In the world: the vanilla destination frame (DestinationGateNineSlice) around the relay, in red.</summary>
    [HarmonyPatch(typeof(FtlGate), "OnDrawDestinationIndicator")]
    static class Frame
    {
        static void Postfix(FtlGate __instance)
        {
            try
            {
                var mark = MarkOf(__instance);
                if (mark == Mark.None || mark == Mark.Warned && !Blink()) return;
                // The vanilla destination frame is green, so a red tint turns it dark: the white jump pulse ring of the galaxy map
                // (MapRules.NodeFtlPulseSprite) takes the red as it is.
                if (Run.Current?.Game.Map.Rules.NodeFtlPulseSprite is not { } ring) return;
                var rect = Rect.CenteredOn(__instance.Ship.WorldCenter, __instance.Ship.WorldBoundingCircle.BoundingRect.Size * 1.6f);
                App.Graphics.SetToMaterial(ring);
                App.Graphics.Draw(new Quad(rect, Red, ring.UVRect));
            }
            catch (Exception ex) { Errors.Report("lockdown relay frame", ex.Message, ex); }
        }
    }

    /// <summary>On the relay's screen indicator: the same frame in red.</summary>
    [HarmonyPatch(typeof(FtlGate), nameof(FtlGate.DrawSpecialIndicator))]
    static class IndicatorFrame
    {
        static void Postfix(FtlGate __instance, Rect uiScreenRect)
        {
            try
            {
                var mark = MarkOf(__instance);
                if (mark == Mark.None || mark == Mark.Warned && !Blink()) return;
                if (Run.Current?.Game.Map.Rules.NodeFtlPulseSprite is not { } ring) return;
                App.Graphics.SetToMaterial(ring);
                App.Graphics.Draw(new Quad(Rect.CenteredOn(uiScreenRect.Center, uiScreenRect.Size * 1.6f), Red, ring.UVRect));
            }
            catch (Exception ex) { Errors.Report("lockdown relay indicator", ex.Message, ex); }
        }
    }

    static bool Blink() => App.Clock.Time.Seconds % 1.0 < 0.6; // determinism: UI blink only
}

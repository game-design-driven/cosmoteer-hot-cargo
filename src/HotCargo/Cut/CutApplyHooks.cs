using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>Spec 2475: an area's edits of a ship that follow the vanilla edit flow (cut orders, weld orders).</summary>
internal interface IShipEdits
{
    /// <summary>The ship has an edit made in blueprint mode that waits for "Make it so".</summary>
    bool Pending(Run run, Ship ship);
    /// <summary>The ship has an applied edit that runs (crew work, a pull).</summary>
    bool Running(Run run, Ship ship);
    /// <summary>The MP input that applies (<paramref name="discard"/> false) or drops the ship's pending edit.</summary>
    MPInput Apply(Ship ship, bool discard);
    /// <summary>The MP input that cancels the ship's running edit.</summary>
    MPInput Cancel(Ship ship);
}

/// <summary>
/// Spec 2475: cuts and welds follow the vanilla edit flow of the build window, through vanilla's own buttons. The vanilla code that
/// decides whether a button shows also counts a cut or weld (the patches below), and the click patches here send the MP inputs:
/// "Make it so" applies a pending edit; "Discard changes" drops it, and in blueprint mode also ends one that runs, as it drops vanilla
/// construction in progress; "Cancel all" (live mode) ends one that runs. Each button state comes from vanilla's own line in the
/// update: a second write after it makes the widget flip each frame, and the game then drops a mouse press (issue #393).
/// </summary>
[HarmonyPatch(typeof(BuildToolbox))]
static class CutApplyHooks
{
    sealed class CutEdits : IShipEdits
    {
        static CutOrder? OrderOf(Run run, Ship ship) => run.State.Cut.Orders.FirstOrDefault(o => o.Ship == ship.UniqueID);
        public bool Pending(Run run, Ship ship) => OrderOf(run, ship) is { Pending: true };
        public bool Running(Run run, Ship ship) => OrderOf(run, ship) is { Pending: false };
        public MPInput Apply(Ship ship, bool discard) => new CutApplyInput(ship, discard);
        public MPInput Cancel(Ship ship) => new CutCancelInput(ship);
    }

    /// <summary>The areas whose edits follow the flow. An area adds its own in its Register.</summary>
    internal static readonly List<IShipEdits> Edits = [new CutEdits()];

    static bool Pending(Ship ship) => Run.Current is { } run && Edits.Any(e => e.Pending(run, ship));
    static bool Running(Ship ship) => Run.Current is { } run && Edits.Any(e => e.Running(run, ship));

    static void Send(BuildToolbox box, Func<Run, Ship, IShipEdits, MPInput?> pick)
    {
        if (box._ship is not { } ship || Run.Current is not { } run) return;
        foreach (var edits in Edits)
            if (pick(run, ship, edits) is { } input) box.Game.NetManager.EnqueueInput(input);
    }

    [HarmonyPostfix, HarmonyPatch("OnConstructClicked")]
    static void Construct(BuildToolbox __instance) => Ux.UxKit.Guard("edit apply", () =>
    {
        if (__instance._ship is not { IsConstructionLegal: true }) return;
        Send(__instance, (run, ship, e) => e.Pending(run, ship) ? e.Apply(ship, discard: false) : null);
    });

    [HarmonyPostfix, HarmonyPatch("OnDiscardBlueprintChangesClicked")]
    static void Discard(BuildToolbox __instance) => Ux.UxKit.Guard("edit discard", () =>
        Send(__instance, (run, ship, e) => e.Pending(run, ship) ? e.Apply(ship, discard: true)
            : __instance.IsEditingBlueprints && e.Running(run, ship) ? e.Cancel(ship) : null));

    [HarmonyPostfix, HarmonyPatch("OnCancelAllClicked")]
    static void CancelAll(BuildToolbox __instance) => Ux.UxKit.Guard("edit cancel all", () =>
        Send(__instance, (run, ship, e) => e.Running(run, ship) ? e.Cancel(ship) : null));

    /// <summary>
    /// Live mode: vanilla shows "Cancel all" while construction is queued (<c>OptionalConstructionManager?.HasAnyQueued</c>, false
    /// for a ship that never queued any). A running cut or weld also shows it: the vanilla assignment of the button's
    /// <c>SelfActive</c> in this method gets that value. So the button is set once per frame, by vanilla's own line, and never flips.
    /// </summary>
    [HarmonyTranspiler, HarmonyPatch("OnLiveModeUpdatingUIState")]
    static IEnumerable<CodeInstruction> LiveButtons(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var button = AccessTools.Field(typeof(BuildToolbox), "_cancelAllBtn");
        var set = AccessTools.PropertySetter(typeof(Halfling.Gui.Widget), nameof(Halfling.Gui.Widget.SelfActive));
        bool loaded = false;
        int replaced = 0;
        foreach (var ci in instructions)
        {
            if (ci.LoadsField(button)) loaded = true;
            if (loaded && replaced == 0 && ci.Calls(set))
            {
                replaced++;
                // Stack: the button, vanilla's value. The helper also takes the build window.
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(ci).MoveBlocksFrom(ci);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CutApplyHooks), nameof(ShowCancelAll)));
                continue;
            }
            yield return ci;
        }
        if (replaced != 1) Errors.Report($"edit buttons {original.Name}", "no SelfActive set of Cancel all; it does not show for cuts and welds");
    }

    static void ShowCancelAll(Halfling.Gui.Widget button, bool vanilla, BuildToolbox box) =>
        button.SelfActive = vanilla || box._ship is { } ship && Running(ship);

    /// <summary>
    /// Blueprint mode: vanilla enables "Discard changes" while the design differs from the ship (<see cref="Ship.ConstructionEqualsRebuild"/>);
    /// a pending or running cut or weld counts. The simulation reads that method too, so this replaces the call in this method only.
    /// </summary>
    [HarmonyTranspiler, HarmonyPatch("OnBlueprintModeUpdatingUIState")]
    static IEnumerable<CodeInstruction> BlueprintButtons(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        Replace(instructions, original, AccessTools.Method(typeof(Ship), nameof(Ship.ConstructionEqualsRebuild)), nameof(EqualsRebuild));

    static bool EqualsRebuild(Ship ship) => ship.ConstructionEqualsRebuild() && !Pending(ship) && !Running(ship);

    /// <summary>The one call of <paramref name="target"/> in the method becomes a call of the helper with the same arguments.</summary>
    static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, MethodBase original, MethodInfo target, string helper)
    {
        var call = AccessTools.Method(typeof(CutApplyHooks), helper);
        int replaced = 0;
        var result = instructions.Select(ci =>
        {
            if (!ci.Calls(target)) return ci;
            replaced++;
            return new CodeInstruction(OpCodes.Call, call).MoveLabelsFrom(ci).MoveBlocksFrom(ci);
        }).ToList();
        if (replaced != 1)
            Errors.Report($"edit buttons {original.Name}", $"{replaced} calls of {target.Name}, expected 1; the vanilla buttons do not count cuts and welds there");
        return result;
    }

    /// <summary>
    /// Blueprint mode: vanilla enables "Make it so" for blueprint changes that wait for it; a pending cut or weld is one. The value
    /// comes from sim state, so every machine reads the same (MPUpdateShipInput.IsEmpty reads it too).
    /// </summary>
    [HarmonyPatch(typeof(ShipMetadata), nameof(ShipMetadata.HasUnappliedBlueprintStates), MethodType.Getter)]
    static class Unapplied
    {
        static void Postfix(ShipMetadata __instance, ref bool __result)
        {
            if (!__result && Pending(__instance.Ship)) __result = true;
        }
    }
}

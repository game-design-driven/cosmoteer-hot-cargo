using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Localization;
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
/// Spec 2475: cuts and welds follow the vanilla edit flow of the build window. "Make it so" applies a pending edit, "Discard changes"
/// drops it, and "Cancel all" cancels a running one, each with the other edits. The vanilla buttons also light up when such an edit is
/// the only one. UI only: each button sends MP inputs.
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

    static void Send(BuildToolbox box, Func<Run, Ship, IShipEdits, MPInput?> pick)
    {
        if (box._ship is not { } ship || Run.Current is not { } run) return;
        foreach (var edits in Edits)
            if (pick(run, ship, edits) is { } input) box.Game.NetManager.EnqueueInput(input);
    }

    static bool Any(BuildToolbox box, Func<Run, Ship, IShipEdits, bool> test) =>
        box._ship is { } ship && Run.Current is { } run && Edits.Any(e => test(run, ship, e));

    [HarmonyPostfix, HarmonyPatch("OnConstructClicked")]
    static void Construct(BuildToolbox __instance) => Ux.UxKit.Guard("edit apply", () =>
    {
        if (__instance._ship is not { IsConstructionLegal: true }) return;
        Send(__instance, (run, ship, e) => e.Pending(run, ship) ? e.Apply(ship, discard: false) : null);
    });

    [HarmonyPostfix, HarmonyPatch("OnDiscardBlueprintChangesClicked")]
    static void Discard(BuildToolbox __instance) => Ux.UxKit.Guard("edit discard", () =>
        Send(__instance, (run, ship, e) => e.Pending(run, ship) ? e.Apply(ship, discard: true) : null));

    [HarmonyPostfix, HarmonyPatch("OnCancelAllClicked")]
    static void CancelAll(BuildToolbox __instance) => Ux.UxKit.Guard("edit cancel all", () =>
        Send(__instance, (run, ship, e) => e.Running(run, ship) ? e.Cancel(ship) : null));

    /// <summary>Blueprint mode: "Make it so" and "Discard changes" work when a pending cut or weld is the only edit.</summary>
    [HarmonyPostfix, HarmonyPatch("OnBlueprintModeUpdatingUIState")]
    static void BlueprintButtons(BuildToolbox __instance) => Ux.UxKit.Guard("edit buttons", () =>
    {
        if (!Any(__instance, (run, ship, e) => e.Pending(run, ship))) return;
        __instance._discardBtn.SelfInputActive = true;
        if (__instance._constructBtn.Text != Strings.GetText("BuildBox/NoWorkNeeded")) return;
        __instance._constructBtn.SelfInputActive = __instance._ship!.IsConstructionLegal;
        __instance._constructBtn.Text = Strings.GetText("BuildBox/Construct");
    });

    /// <summary>Live mode: "Cancel all" shows while a cut or weld runs, as it does while construction is queued.</summary>
    [HarmonyPostfix, HarmonyPatch("OnLiveModeUpdatingUIState")]
    static void LiveButtons(BuildToolbox __instance) => Ux.UxKit.Guard("edit buttons", () =>
    {
        if (__instance._cancelAllBtn.SelfActive || !Any(__instance, (run, ship, e) => e.Running(run, ship))) return;
        __instance._cancelAllBtn.SelfActive = true;
        __instance._cancelAllBtn.Text = Strings.GetText("BuildBox/CancelAll");
        __instance._repairAllBtn.SelfActive = false;
    });
}

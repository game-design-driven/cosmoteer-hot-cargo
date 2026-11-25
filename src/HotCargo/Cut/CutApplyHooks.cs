using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>
/// Spec 2475: a cut follows the vanilla edit flow of the build window. "Make it so" applies a pending cut, "Discard changes" drops it,
/// and "Cancel all" cancels a running cut, each with the other edits. The vanilla buttons also light up when a cut is the only edit.
/// UI only: each button sends an MP input.
/// </summary>
[HarmonyPatch(typeof(BuildToolbox))]
static class CutApplyHooks
{
    static CutOrder? OrderOf(Ship? ship) =>
        ship is not null && Run.Current is { } run ? run.State.Cut.Orders.FirstOrDefault(o => o.Ship == ship.UniqueID) : null;

    [HarmonyPostfix, HarmonyPatch("OnConstructClicked")]
    static void Construct(BuildToolbox __instance) => Ux.UxKit.Guard("cut apply", () =>
    {
        if (__instance._ship is { IsConstructionLegal: true } ship && OrderOf(ship) is { Pending: true })
            __instance.Game.NetManager.EnqueueInput(new CutApplyInput(ship, discard: false));
    });

    [HarmonyPostfix, HarmonyPatch("OnDiscardBlueprintChangesClicked")]
    static void Discard(BuildToolbox __instance) => Ux.UxKit.Guard("cut discard", () =>
    {
        if (__instance._ship is { } ship && OrderOf(ship) is { Pending: true })
            __instance.Game.NetManager.EnqueueInput(new CutApplyInput(ship, discard: true));
    });

    [HarmonyPostfix, HarmonyPatch("OnCancelAllClicked")]
    static void CancelAll(BuildToolbox __instance) => Ux.UxKit.Guard("cut cancel all", () =>
    {
        if (__instance._ship is { } ship && OrderOf(ship) is { Pending: false })
            __instance.Game.NetManager.EnqueueInput(new CutCancelInput(ship));
    });

    /// <summary>Blueprint mode: "Make it so" and "Discard changes" work when a pending cut is the only edit.</summary>
    [HarmonyPostfix, HarmonyPatch("OnBlueprintModeUpdatingUIState")]
    static void BlueprintButtons(BuildToolbox __instance) => Ux.UxKit.Guard("cut buttons", () =>
    {
        if (__instance._ship is not { } ship || OrderOf(ship) is not { Pending: true }) return;
        __instance._discardBtn.SelfInputActive = true;
        if (__instance._constructBtn.Text != Strings.GetText("BuildBox/NoWorkNeeded")) return;
        __instance._constructBtn.SelfInputActive = ship.IsConstructionLegal;
        __instance._constructBtn.Text = Strings.GetText("BuildBox/Construct");
    });

    /// <summary>Live mode: "Cancel all" shows while a cut runs, as it does while construction is queued.</summary>
    [HarmonyPostfix, HarmonyPatch("OnLiveModeUpdatingUIState")]
    static void LiveButtons(BuildToolbox __instance) => Ux.UxKit.Guard("cut buttons", () =>
    {
        if (__instance._ship is not { } ship || OrderOf(ship) is not { Pending: false } || __instance._cancelAllBtn.SelfActive) return;
        __instance._cancelAllBtn.SelfActive = true;
        __instance._cancelAllBtn.Text = Strings.GetText("BuildBox/CancelAll");
        __instance._repairAllBtn.SelfActive = false;
    });
}

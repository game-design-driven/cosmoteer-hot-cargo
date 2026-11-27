using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Input;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui;
using Halfling.Gui.Components.Toggles;
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
/// drops it, and "Cancel all" cancels a running one, each with the other edits. When such an edit is the only one, the mod's stand-ins
/// for these controls show. UI only: each button sends MP inputs.
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

    /// <summary>The widgets that a player sees as "Make it so": vanilla's and the mod's stand-in (at most one shows).</summary>
    internal static IEnumerable<Widget> ConstructButtons(BuildToolbox box) => [box._constructBtn, .. Own.TryGetValue(box, out var o) ? [o.Apply] : Array.Empty<Widget>()];
    /// <summary>The widgets that a player sees as "Cancel all".</summary>
    internal static IEnumerable<Widget> CancelButtons(BuildToolbox box) => [box._cancelAllBtn, .. Own.TryGetValue(box, out var o) ? [o.Cancel] : Array.Empty<Widget>()];
    /// <summary>The menu items that a player sees as "Discard changes" (in the build window's menu).</summary>
    internal static IEnumerable<Widget> DiscardItems(BuildToolbox box) => [box._discardBtn, .. Own.TryGetValue(box, out var o) ? [o.Discard] : Array.Empty<Widget>()];

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

    /// <summary>The mod's stand-ins for vanilla controls of one build window.</summary>
    sealed record StandIns(Button Cancel, Button Apply, MenuItem Discard);

    static readonly ConditionalWeakTable<BuildToolbox, StandIns> Own = new();

    /// <summary>
    /// Spec 2475: the stand-ins, copies of the vanilla "Cancel all" and "Make it so" buttons (look and Ctrl+Enter hotkey) and a
    /// "Discard changes" item in the build window's menu. The vanilla buttons show from vanilla state only. Turning a vanilla button
    /// back on after the vanilla update turned it off makes it flip off and on each frame, and Halfling drops a mouse press when a
    /// widget turns off (WidgetInputStateController.OnComponentDeactivated): a real click then never fires (issue #393). Only this
    /// code sets the stand-ins, and only when their state changes.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch(MethodType.Constructor, typeof(GameRoot), typeof(GameGui))]
    static void AddStandIns(BuildToolbox __instance) => Ux.UxKit.Guard("edit buttons", () =>
    {
        var box = __instance;
        // The button's parent is the children component of the vanilla button box (a LayoutBox).
        var slot = (box._cancelAllBtn.Parent as Halfling.Gui.Components.WidgetComponent)?.Widget as LayoutBox
            ?? throw new InvalidOperationException("the build window's button box is not a LayoutBox");
        Button Copy(Button from, string text, Action click)
        {
            var b = new Button(from) { SelfActive = false, PercentileRect = new Rect(0f, 0f, 100f, 100f), Hotkey = Inputs.Design.Construct };
            b.TextProvider = Strings.KeyString(text);
            b.Clicked += (_, _) => Ux.UxKit.Guard("edit button", click);
            slot.AddChild(b);
            return b;
        }
        var cancel = Copy(box._cancelAllBtn, "BuildBox/CancelAll", () =>
        {
            Send(box, (run, ship, e) => e.Running(run, ship) ? e.Cancel(ship) : null);
            App.Audio.Play(box.Rules.CancelAllSound, AudioGroups.Interface);
        });
        var apply = Copy(box._constructBtn, "BuildBox/Construct", () =>
        {
            if (box._ship is not { IsConstructionLegal: true }) return;
            Send(box, (run, ship, e) => e.Pending(run, ship) ? e.Apply(ship, discard: false) : null);
            App.Audio.Play(box.Rules.BlueprintMakeItSoSound, AudioGroups.Interface);
        });
        var discard = box._menu.AddItem(Strings.KeyString("BuildBox/Discard"), null, (_, _) => Ux.UxKit.Guard("edit button", () =>
            Send(box, (run, ship, e) => e.Pending(run, ship) ? e.Apply(ship, discard: true) : null)));
        discard.ToolTips.TextProvider = Strings.KeyString("BuildBox/DiscardTip");
        discard.SelfActive = false;
        // Vanilla shows the button box only while one of its buttons shows (the BuildToolbox constructor); the stand-ins count too.
        slot.ComponentToggles[slot] = new OrToggle(new SelfActiveStateToggle(box._constructBtn, onWhenActive: true),
            new SelfActiveStateToggle(box._repairAllBtn, onWhenActive: true), new SelfActiveStateToggle(box._cancelAllBtn, onWhenActive: true),
            new SelfActiveStateToggle(cancel, onWhenActive: true), new SelfActiveStateToggle(apply, onWhenActive: true));
        Own.Add(box, new(cancel, apply, discard));
    });

    /// <summary>
    /// Spec 2475: after the vanilla update of each frame. "Cancel all" shows while a cut or weld runs and vanilla shows no "Cancel all"
    /// (live mode, or blueprint mode with no other design change). In blueprint mode with no other design change, "Make it so" and
    /// "Discard changes" show for a pending cut or weld. Vanilla's disabled "No work needed" and "Discard changes" hide behind them:
    /// a disabled widget takes no click, so hiding it each frame loses nothing.
    /// </summary>
    [HarmonyPostfix, HarmonyPatch("OnUpdatingUIState")]
    static void UpdateStandIns(BuildToolbox __instance) => Ux.UxKit.Guard("edit buttons", () =>
    {
        var box = __instance;
        if (box._ship is not { } ship || !Own.TryGetValue(box, out var own)) return;
        bool blueprint = box.IsEditingBlueprints;
        bool noWork = blueprint && box._constructBtn.Text == Strings.GetText("BuildBox/NoWorkNeeded");
        bool pending = Any(box, (run, s, e) => e.Pending(run, s));
        bool cancel = Any(box, (run, s, e) => e.Running(run, s)) && !box._cancelAllBtn.SelfActive && (!blueprint || noWork);
        bool apply = noWork && pending;
        bool discard = blueprint && pending && !box._discardBtn.SelfInputActive;
        Set(own.Cancel, cancel);
        Set(own.Apply, apply);
        if (apply && own.Apply.SelfInputActive != ship.IsConstructionLegal) own.Apply.SelfInputActive = ship.IsConstructionLegal;
        Set(own.Discard, discard);
        if (box._discardBtn.SelfActive == discard) box._discardBtn.SelfActive = !discard;
        if (cancel || apply) box._constructBtn.SelfActive = false;
    });

    static void Set(Widget w, bool active)
    {
        if (w.SelfActive != active) w.SelfActive = active;
    }
}

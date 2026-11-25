using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Gui;
using Cosmoteer.Input;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Input;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Cut;

namespace HotCargo.Weld;

/// <summary>Spec 2480: the Weld button of the build window, after the Cut button (both follow BuildToolbox.AddRemovePartButton).</summary>
[HarmonyPatch(typeof(BuildToolbox), "AddRemovePartButton")]
static class WeldTool
{
    static ImageListItem? _button;

    [HarmonyPriority(Priority.Low)]
    static void Postfix(BuildToolbox __instance, LayoutBox box, GameGui gameGui) => Ux.UxKit.Guard("weld button", () =>
    {
        var btn = new ImageListItem(Ui.ModSprite.Read("weld/icons.rules", "Tool"), WidgetRules.Instance.ToolButton);
        btn.ImageSprite.ScaleMode = SpriteScaleMode.FitMaintainAspect;
        btn.Size = new IntVector2(50, 50);
        btn.ToolTips.TextProvider = Strings.KeyString("HotCargoWeld/ToolTip");
        btn.SelectionController.SelectionManager = gameGui.StateSelectionManager;
        btn.SelectionController.DeselectWhenDeactivated = true;
        btn.SelectionController.EnableDeselect = true;
        // Shown by the frame hook only in a Hot Cargo run with the weld on (spec 2493), never in the vanilla ship designer.
        btn.SelfActive = false;
        box.AddChild(btn);
        gameGui.SelectionStates.Add(btn, new WeldToolState(__instance.Game, __instance));
        _button = btn;
    });

    /// <summary>Picks the Weld tool, like a click on its button (screenshots).</summary>
    internal static void Select() => (_button ?? throw new InvalidOperationException("the build window has no Weld button")).IsSelected = true;

    internal static void Register() => Run.Hooks.Frame += run => Ux.UxKit.Guard("weld button", () =>
    {
        if (_button is { } btn && btn.SelfActive != run.T.Weld.Enabled) btn.SelfActive = run.T.Weld.Enabled;
    });
}

/// <summary>
/// Spec 2482 to 2484: the Weld tool. It shows the picked target as a ghost in the ship's grid that follows the cursor; R turns it,
/// Tab picks the next target, a click sends the order or shows why not, and a right click cancels the ship's order. UI only: the
/// sim changes only through the MP inputs.
/// </summary>
internal sealed class WeldToolState : BaseBuildState
{
    /// <summary>The state of the newest build window, for the screenshot commands.</summary>
    static WeldToolState? _current;

    ObjectID? _target;
    Placement _placement;
    IntVector2? _mouseCell;

    public WeldToolState(GameRoot game, IBuildModeProvider buildModeProvider) : base(game, buildModeProvider) => _current = this;

    internal static WeldToolState RequireCurrent() => _current ?? throw new InvalidOperationException("no build window with a Weld tool");

    public override void OnDeselected()
    {
        _target = null;
        _mouseCell = null;
    }

    /// <summary>The picked target, or the nearest one when none is picked or the picked one is no longer a target.</summary>
    Ship? Target(Run run, Ship ship)
    {
        var targets = WeldShip.Targets(run, ship, Game.LocalPlayerIndex);
        var picked = targets.FirstOrDefault(t => t.UniqueID == _target);
        if (picked is null && targets.Count > 0) Pick(ship, targets[0]);
        return picked ?? targets.FirstOrDefault();
    }

    void Pick(Ship ship, Ship target)
    {
        _target = target.UniqueID;
        _placement = WeldShip.Snap(ship, target);
        _mouseCell = null;
    }

    /// <summary>Puts a target and a place into the tool, as the cursor would (screenshots).</summary>
    internal void Show(Ship ship, Ship target, Placement p)
    {
        Pick(ship, target);
        _placement = p;
        _mouseCell = new IntVector2(int.MinValue, int.MinValue);
    }

    /// <summary>A click, as the left mouse button does (screenshots).</summary>
    internal void Click(Ship ship)
    {
        if (Run.Current is { } run && Target(run, ship) is { } target) Submit(ship, target);
    }

    /// <summary>The middle tile of the target's parts, in its own grid: the tile that follows the cursor.</summary>
    static Cell Middle(Ship target)
    {
        var rect = target.Parts.BoundingRect ?? throw new InvalidOperationException("the target has no parts");
        return new Cell(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    public override void InputImpl()
    {
        if (Run.Current is not { } run || !run.T.Weld.Enabled || Sim.PlayerInput.GetSingleSelectedShip() is not { } ship) return;
        if (Inputs.Game.CancelTool.WasPressed && run.State.Weld.Orders.Any(o => o.Ship == ship.UniqueID))
        {
            Game.NetManager.EnqueueInput(new WeldCancelInput(ship));
            App.Audio.Play(Rules.CancelConstructionSound, AudioGroups.Interface);
            return;
        }
        if (Target(run, ship) is not { } target) return;
        if (App.Keyboard.HotkeyPressed(ViKey.Tab))
        {
            var targets = WeldShip.Targets(run, ship, Game.LocalPlayerIndex);
            Pick(ship, targets[(targets.FindIndex(t => t.UniqueID == _target) + 1) % targets.Count]);
            return;
        }
        if (App.Keyboard.HotkeyPressed(ViKey.R))
        {
            _placement = _placement.Turned();
            App.Audio.Play(Rules.RotateRightSound, AudioGroups.Interface);
        }
        var local = ship.TransformPointFromWorld(Sim.WorldMouseLoc);
        var cell = new IntVector2((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y));
        if (_mouseCell is { } last && last != cell)
        {
            var middle = WeldModel.Place(Middle(target), _placement with { X = 0, Y = 0 });
            _placement = _placement with { X = cell.X - middle.X, Y = cell.Y - middle.Y };
        }
        _mouseCell = cell;
        if (Inputs.Game.UseTool.WasPressed) Submit(ship, target);
    }

    void Submit(Ship ship, Ship target)
    {
        if (WeldArea.Refusal(ship, target, _placement) is { } verdict)
        {
            PlayErrorSound();
            Game.Gui.CreateFloatingText(Strings.GetText($"HotCargoWeld/{verdict}"), ship.TransformPointToWorld(new Vector2(_placement.X, _placement.Y)),
                WidgetRules.Instance.BadTextColor, 1f, null, 1f, xmlFormatting: false);
            return;
        }
        // Spec 2475: in blueprint mode the weld waits for "Make it so"; in live mode it starts at once.
        Game.NetManager.EnqueueInput(new WeldOrderInput(ship, target, _placement, IsEditingBlueprints));
        if (IsEditingBlueprints) App.Audio.Play(Rules.BlueprintAddSound, AudioGroups.Interface);
        else App.Audio.Play(Rules.MakeItSoSound, AudioGroups.Effects);
    }

    public override void DrawOverlayImpl()
    {
        if (Run.Current is not { } run || Sim.PlayerInput.GetSingleSelectedShip() is not { } ship) return;
        if (Target(run, ship) is not { } target)
        {
            DrawTextUnderCursor(Strings.GetText("HotCargoWeld/NoTarget"));
            return;
        }
        var check = WeldShip.Check(ship, target, _placement);
        var slice = check.Verdict == WeldVerdict.Ok ? Rules.RepairPartNineSlice : Rules.RemovePartNineSlice;
        using (App.Graphics.PushTransformMultiply(ship.WorldTransform))
        {
            foreach (var part in target.Parts)
            {
                var r = part.Rect;
                var (x, y, w, h) = WeldModel.Place((r.X, r.Y, r.Width, r.Height), _placement);
                slice.Draw(new Rect(x, y, w, h), Direction.Zero, Color.White);
            }
            CutDraw.DrawDrag(check.Joints);
        }
        if (check.Verdict != WeldVerdict.Ok) DrawTextUnderCursor(Strings.GetText($"HotCargoWeld/{check.Verdict}"));
    }
}

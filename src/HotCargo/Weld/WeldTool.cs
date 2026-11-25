using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Gui;
using Cosmoteer.Input;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation.Overlays;
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
/// Spec 2482 to 2484: the Weld tool. First the player picks a piece: every target shows an outline, the piece under the cursor a strong
/// one, and an object that is not a target names the reason. A click picks a target; its ghost then follows the cursor in the part
/// brush art, R turns it, a click sends the order or shows why not, and a right click or Esc goes back to picking. With no piece picked,
/// a right click cancels the ship's order. UI only: the sim changes only through the MP inputs.
/// </summary>
internal sealed class WeldToolState : BaseBuildState
{
    /// <summary>The state of the newest build window, for the screenshot commands.</summary>
    static WeldToolState? _current;

    /// <summary>The picked target, or null while the player picks.</summary>
    ObjectID? _picked;
    Placement _placement;
    /// <summary>The tile of the picked target, in its own grid, that stays under the cursor.</summary>
    Cell _grab;
    IntVector2? _mouseCell;
    /// <summary>A place set by the screenshot commands; the cursor does not move it.</summary>
    bool _pinned;
    /// <summary>A world point that stands in for the cursor (screenshots, where the cursor cannot move).</summary>
    internal Vector2? HoverAt;

    /// <summary>No piece is picked: the player picks one.</summary>
    internal bool IsPicking => _picked is null;

    public WeldToolState(GameRoot game, IBuildModeProvider buildModeProvider) : base(game, buildModeProvider) => _current = this;

    internal static WeldToolState RequireCurrent() => _current ?? throw new InvalidOperationException("no build window with a Weld tool");

    public override void OnDeselected()
    {
        Unpick();
        HoverAt = null;
    }

    /// <summary>Esc with a picked piece goes back to picking, as Esc resets the vanilla select tool (GameGui.HandleHotkeys).</summary>
    public override bool TryReset()
    {
        if (_picked is null) return false;
        Unpick();
        App.Audio.Play(Rules.CancelConstructionSound, AudioGroups.Interface);
        return true;
    }

    void Unpick()
    {
        _picked = null;
        _mouseCell = null;
        _pinned = false;
    }

    Vector2 Cursor => HoverAt ?? Sim.WorldMouseLoc;

    /// <summary>The object under the cursor other than the ship, with its target verdict, or null.</summary>
    (Ship Ship, WeldTargetVerdict Verdict)? Hovered(Run run, Ship ship)
    {
        var cursor = Cursor;
        // Ships whose parts lie under the cursor, nearest center first, so a small piece next to a big wreck can be picked.
        var hit = Sim.Ships.Where(s => s != ship && s.ExistsInSim(Sim) && s.DetWorldBoundingCircle.Contains(cursor)
                && s.Parts[Ship.GetCellFromShipLoc(s.TransformPointFromWorld(cursor)), PartRectType.Physical] is not null)
            .OrderBy(s => s.WorldCenter.DistanceTo(cursor)).ThenBy(s => s.UniqueID).FirstOrDefault();
        return hit is not null && WeldShip.TargetVerdict(run, ship, hit, Game.LocalPlayerIndex) is { } v ? (hit, v) : null; // determinism: UI only, the local player's own tool
    }

    Ship? Picked(Run run, Ship ship) =>
        _picked is { } id && Sim.Ships[id] is { } t && WeldShip.IsTarget(run, ship, t, Game.LocalPlayerIndex) ? t : null; // determinism: UI only, the local player's own tool

    void Pick(Ship ship, Ship target)
    {
        _picked = target.UniqueID;
        _placement = WeldShip.Snap(ship, target);
        var local = target.TransformPointFromWorld(Cursor);
        _grab = new Cell((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y));
        // Without a part under the cursor (screenshots, picks by command), the middle of the piece follows the cursor.
        if (target.Parts[new IntVector2(_grab.X, _grab.Y), PartRectType.Physical] is null) _grab = Middle(target);
        _mouseCell = null;
    }

    /// <summary>Puts a target and a place into the tool, as a pick and the cursor would (screenshots).</summary>
    internal void Show(Ship ship, Ship target, Placement p)
    {
        Pick(ship, target);
        _placement = p;
        _pinned = true;
    }

    /// <summary>A pick of a target, as a click on it does (screenshots).</summary>
    internal void PickByCommand(Ship ship, Ship target) => Pick(ship, target);

    /// <summary>A click, as the left mouse button does (screenshots).</summary>
    internal void Click(Ship ship)
    {
        if (Run.Current is { } run && Picked(run, ship) is { } target) Submit(ship, target);
    }

    /// <summary>The middle tile of the target's parts, in its own grid.</summary>
    static Cell Middle(Ship target)
    {
        var rect = target.Parts.BoundingRect ?? throw new InvalidOperationException("the target has no parts");
        return new Cell(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    /// <summary>The place that puts the grab tile on <paramref name="cell"/> of the ship's grid.</summary>
    Placement Follow(IntVector2 cell)
    {
        var grab = WeldModel.Place(_grab, _placement with { X = 0, Y = 0 });
        return _placement with { X = cell.X - grab.X, Y = cell.Y - grab.Y };
    }

    public override void InputImpl()
    {
        if (Run.Current is not { } run || !run.T.Weld.Enabled || Sim.PlayerInput.GetSingleSelectedShip() is not { } ship) return;
        var local = ship.TransformPointFromWorld(Cursor);
        var cell = new IntVector2((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y));
        if (Picked(run, ship) is not { } target)
        {
            if (_picked is not null) Unpick();
            InputPicking(run, ship);
            return;
        }
        if (Inputs.Game.CancelTool.WasPressed)
        {
            TryReset();
            return;
        }
        if (App.Keyboard.HotkeyPressed(ViKey.R))
        {
            _placement = _placement.Turned();
            if (!_pinned) _placement = Follow(cell);
            App.Audio.Play(Rules.RotateRightSound, AudioGroups.Interface);
        }
        if (!_pinned && _mouseCell != cell) _placement = Follow(cell);
        _mouseCell = cell;
        if (Inputs.Game.UseTool.WasPressed) Submit(ship, target);
    }

    void InputPicking(Run run, Ship ship)
    {
        if (Inputs.Game.CancelTool.WasPressed && run.State.Weld.Orders.Any(o => o.Ship == ship.UniqueID))
        {
            Game.NetManager.EnqueueInput(new WeldCancelInput(ship));
            App.Audio.Play(Rules.CancelConstructionSound, AudioGroups.Interface);
            return;
        }
        if (!Inputs.Game.UseTool.WasPressed || Hovered(run, ship) is not var (hovered, verdict)) return;
        if (verdict != WeldTargetVerdict.Ok)
        {
            PlayErrorSound();
            Game.Gui.CreateFloatingText(Strings.GetText($"HotCargoWeld/Target{verdict}"), Cursor, WidgetRules.Instance.BadTextColor, 1f, null, 1f, xmlFormatting: false);
            return;
        }
        Pick(ship, hovered);
        App.Audio.Play(Game.Gui.Rules.CopySound, AudioGroups.Interface);
    }

    void Submit(Ship ship, Ship target)
    {
        if (WeldArea.Refusal(ship, target, _placement) is { } verdict)
        {
            PlayErrorSound();
            Game.Gui.CreateFloatingText(Strings.GetText($"HotCargoWeld/{verdict}"), Cursor, WidgetRules.Instance.BadTextColor, 1f, null, 1f, xmlFormatting: false);
            return;
        }
        // Spec 2475: in blueprint mode the weld waits for "Make it so"; in live mode it starts at once.
        Game.NetManager.EnqueueInput(new WeldOrderInput(ship, target, _placement, IsEditingBlueprints));
        if (IsEditingBlueprints) App.Audio.Play(Rules.BlueprintAddSound, AudioGroups.Interface);
        else App.Audio.Play(Rules.MakeItSoSound, AudioGroups.Effects);
        Unpick();
    }

    public override void DrawOverlayImpl()
    {
        if (Run.Current is not { } run || Sim.PlayerInput.GetSingleSelectedShip() is not { } ship) return;
        if (Picked(run, ship) is { } target)
        {
            DrawPicked(ship, target);
            return;
        }
        var targets = WeldShip.Targets(run, ship, Game.LocalPlayerIndex); // determinism: UI only, the local player's own tool
        var hovered = Hovered(run, ship);
        foreach (var t in targets)
            if (t != hovered?.Ship) DrawOutline(t, Rules.SelectedPartNineSlice, new Color(1f, 1f, 1f, 0.45f));
        string hint = run.State.Weld.Orders.Any(o => o.Ship == ship.UniqueID) ? "\n" + Strings.GetText("HotCargoWeld/HintCancel") : "";
        if (hovered is var (h, verdict))
        {
            DrawOutline(h, verdict == WeldTargetVerdict.Ok ? Rules.SelectedPartNineSlice : Rules.RemovePartNineSlice, Color.White);
            DrawTextUnderCursor(Strings.GetText($"HotCargoWeld/Target{verdict}") + hint);
            return;
        }
        DrawTextUnderCursor(Strings.GetText(targets.Count == 0 ? "HotCargoWeld/NoTarget" : "HotCargoWeld/HintPick") + hint);
    }

    static void DrawOutline(Ship target, NineSlice slice, Color color)
    {
        using (App.Graphics.PushTransformMultiply(target.WorldTransform))
            foreach (var part in target.Parts) slice.Draw((Rect)part.Rect, Direction.Zero, color);
    }

    /// <summary>The picked piece in place, and its ghost at the place.</summary>
    void DrawPicked(Ship ship, Ship target)
    {
        DrawOutline(target, Rules.SelectedPartNineSlice, Color.White);
        var check = WeldShip.Check(ship, target, _placement);
        bool valid = check.Verdict == WeldVerdict.Ok;
        WeldDraw.Ghost(ship, target, _placement, valid);
        using (App.Graphics.PushTransformMultiply(ship.WorldTransform))
            CutDraw.DrawDrag(check.Joints);
        DrawTextUnderCursor(valid ? Strings.GetText("HotCargoWeld/HintPlace") : Strings.GetText($"HotCargoWeld/{check.Verdict}"));
    }
}

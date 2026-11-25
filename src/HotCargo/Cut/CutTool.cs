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
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>Spec 2460: the Cut button of the build window, after the remove button (BuildToolbox.AddRemovePartButton).</summary>
[HarmonyPatch(typeof(BuildToolbox), "AddRemovePartButton")]
static class CutTool
{
    static ImageListItem? _button;

    static void Postfix(BuildToolbox __instance, LayoutBox box, GameGui gameGui) => Ux.UxKit.Guard("cut button", () =>
    {
        var btn = new ImageListItem(Ui.ModSprite.Read("cut/icons.rules", "Tool"), WidgetRules.Instance.ToolButton);
        btn.ImageSprite.ScaleMode = SpriteScaleMode.FitMaintainAspect;
        btn.Size = new IntVector2(50, 50);
        btn.ToolTips.TextProvider = Strings.KeyString("HotCargoCut/ToolTip");
        btn.SelectionController.SelectionManager = gameGui.StateSelectionManager;
        btn.SelectionController.DeselectWhenDeactivated = true;
        btn.SelectionController.EnableDeselect = true;
        // Shown by the frame hook only in a Hot Cargo run with the cut on (spec 2471), never in the vanilla ship designer.
        btn.SelfActive = false;
        box.AddChild(btn);
        gameGui.SelectionStates.Add(btn, new CutToolState(__instance.Game, __instance));
        _button = btn;
    });

    /// <summary>Picks the Cut tool, like a click on its button (screenshots).</summary>
    internal static void Select() => (_button ?? throw new InvalidOperationException("the build window has no Cut button")).IsSelected = true;

    internal static void Register() => Run.Hooks.Frame += run => Ux.UxKit.Guard("cut button", () =>
    {
        if (_button is { } btn && btn.SelfActive != run.T.Cut.Enabled) btn.SelfActive = run.T.Cut.Enabled;
    });
}

/// <summary>
/// Spec 2461 to 2463: the Cut tool. A drag draws a line from corner to corner along tile edges; the release checks it and sends the
/// order, or shows why not. A right click cancels the ship's order. UI only: the sim changes only through the MP inputs.
/// </summary>
internal sealed class CutToolState : BaseBuildState
{
    /// <summary>The state of the newest build window, for the screenshot commands.</summary>
    static CutToolState? _current;

    List<Corner>? _line;
    CutGrid? _grid;
    Edge? _blocked;

    public CutToolState(GameRoot game, IBuildModeProvider buildModeProvider) : base(game, buildModeProvider) => _current = this;

    internal static CutToolState RequireCurrent() => _current ?? throw new InvalidOperationException("no build window with a Cut tool");

    /// <summary>A line as a drag leaves it (screenshots).</summary>
    internal void ShowLine(List<Corner> line, Edge? blocked)
    {
        _line = line;
        _blocked = blocked;
    }

    /// <summary>The release of the drag (screenshots).</summary>
    internal void Release(Ship ship)
    {
        if (_line is not null) Submit(ship, _line);
        OnDeselected();
    }

    public override void OnDeselected()
    {
        _line = null;
        _grid = null;
        _blocked = null;
    }

    public override void InputImpl()
    {
        if (Run.Current is not { } run || !run.T.Cut.Enabled || Sim.PlayerInput.GetSingleSelectedShip() is not { } ship) return;
        var local = ship.TransformPointFromWorld(Sim.WorldMouseLoc);
        var corner = new Corner((int)MathF.Round(local.X), (int)MathF.Round(local.Y));
        if (Inputs.Game.UseTool.WasPressed)
        {
            _line = [corner];
            _grid = CutShip.Grid(ship.Parts).Grid;
            _blocked = null;
        }
        else if (_line is not null && _grid is not null && (bool)Inputs.Game.UseTool) _blocked = CutModel.Extend(_line, corner, _grid.Blocked);
        if (_line is not null && Inputs.Game.UseTool.WasReleased)
        {
            Submit(ship, _line);
            OnDeselected();
        }
        if (Inputs.Game.CancelTool.WasPressed && run.State.Cut.Orders.Any(o => o.Ship == ship.UniqueID))
        {
            OnDeselected();
            Game.NetManager.EnqueueInput(new CutCancelInput(ship));
        }
    }

    void Submit(Ship ship, List<Corner> line)
    {
        if (line.Count < 2) return;
        var edges = CutModel.Edges(line);
        if (CutArea.Refusal(ship, edges, out _) is { } verdict)
        {
            PlayErrorSound();
            // At the end of the line, where the cursor is at the release.
            var at = ship.TransformPointToWorld(new Vector2(line[^1].X, line[^1].Y));
            Game.Gui.CreateFloatingText(Strings.GetText($"HotCargoCut/{verdict}"), at, WidgetRules.Instance.BadTextColor, 1f, null, 1f, xmlFormatting: false);
            return;
        }
        Game.NetManager.EnqueueInput(new CutOrderInput(ship, edges));
    }

    public override void DrawOverlayImpl()
    {
        if (_line is null || Sim.PlayerInput.GetSingleSelectedShip() is not { } ship) return;
        var line = Rules.RemovePartXLine;
        using (App.Graphics.PushTransformMultiply(ship.WorldTransform))
        {
            CutDraw.DrawDrag(CutModel.Edges(_line));
            if (_blocked is { } b) CutDraw.DrawBlocked(Sim, line, b);
        }
        if (_blocked is not null) DrawTextUnderCursor(Strings.GetText("HotCargoCut/Blocked"));
    }
}

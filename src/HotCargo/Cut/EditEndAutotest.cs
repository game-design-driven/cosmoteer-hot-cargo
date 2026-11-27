using Cosmoteer.Game;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Ships;
using Halfling.Gui;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Cut;

/// <summary>A cut or a weld, as the test `edit-end` drives it (spec 2477).</summary>
internal interface IEditUnderTest
{
    /// <summary>Only a weld pulls.</summary>
    bool Pulls { get; }
    Ship Ship { get; }
    /// <summary>Picks the area's tool button in the open build window.</summary>
    void SelectTool();
    /// <summary>Tunables for the stage: a slow pull for <see cref="EditStage.Pull"/>, a fast one else, and long edge work.</summary>
    void Prepare(EditStage stage);
    /// <summary>The order through the tool, as a release of a drag or a click does. The build window's mode decides "pending".</summary>
    void Order();
    EditStage? Stage();
    /// <summary>Test only: every edge but one is done, and the last one has no work.</summary>
    void ToLastEdge();
    /// <summary>The tool's right click (the method that the mouse input calls).</summary>
    void RightClick(Run run);
    /// <summary>The tool's state is the one that the build window has selected.</summary>
    bool ToolSelected(GameRoot game);
    /// <summary>Ends a remaining order with the sim input and puts a weld piece back at its start.</summary>
    void Reset(GameRoot game);
    /// <summary>The ships of the edit for the log of each case.</summary>
    string Describe();
    /// <summary>Health that the ships of the edit lost since the first case, or null. Ending an edit must harm no part.</summary>
    string? Harm();
}

/// <summary>
/// Spec 2477: the test `edit-end`. "edit_end cut|weld" puts one block of script lines for each case of <see cref="EditEndModel.Cases"/>
/// in front of the script: set up the mode and the stage, press the control through the real widget, check the result. A control
/// that a player cannot see or click (hidden or disabled widget) is not pressed: its result is <see cref="EditOutcome.Unavailable"/>.
/// The setup takes shortcuts (the order goes through the tool without a mouse; the last edge is set); the controls do not.
/// </summary>
internal static class EditEndAutotest
{
    internal static readonly Dictionary<string, IEditUnderTest> Areas = new(StringComparer.OrdinalIgnoreCase);
    static bool _available;
    static int _failed, _checked;

    static GameRoot Game() => Autotest.RequireRun().Game;
    static IEditUnderTest Area(string name) => Areas.TryGetValue(name, out var a) ? a : throw new InvalidOperationException($"no edit area '{name}' (cut or weld)");
    static BuildToolbox Box() => Game().Gui.ShipGui.BuildToolbox;

    /// <summary>
    /// A click on a widget, as the mouse makes it: only on a widget that shows and takes input (all its parents too). For a menu item,
    /// <paramref name="via"/> is the menu that the player opens first. False when the player cannot click it.
    /// </summary>
    internal static bool Press(Button button, Widget? via = null)
    {
        bool can = via is null ? button.IsActive && button.IsInputActive : via.IsActive && via.IsInputActive && button.SelfActive && button.SelfInputActive;
        if (can) button.ClickController.InvokeClicked();
        return can;
    }

    /// <summary>
    /// Opens the ship's build window in live or blueprint mode with the real mode toggle, so orders and buttons come from the mode
    /// that a player would be in. Vanilla pauses the game in blueprint mode; tests need the sim to run, so this unpauses it.
    /// </summary>
    internal static void SetMode(Ship ship, bool blueprint)
    {
        var game = Game();
        game.Sim.PlayerInput.SelectSingleShip(ship);
        game.Gui.ShipGui.BuildButton.IsSelected = true;
        Box()._blueprintsToggle.IsSelected = blueprint;
        Autotest.Commands["pause"]("off");
    }

    /// <summary>A click that the test needs: it fails when the player cannot click the widget.</summary>
    internal static void Click(Button button, string name, Widget? via = null)
    {
        if (!Press(button, via)) throw new InvalidOperationException($"the player cannot click '{name}' now (hidden or disabled)");
    }

    static IEnumerable<string> Lines(string area, EditCase c)
    {
        // The last edge is a test shortcut on an order in crew work (edit_last).
        string stage = ((int)(c.Stage == EditStage.LastEdge ? EditStage.Work : c.Stage)).ToString();
        yield return $"edit_case {area} {c.Mode} {c.Stage}";
        if (c.Mode == EditMode.Blueprint)
        {
            yield return $"waitfor edit_stage_{area} == {(int)EditStage.Pending} 30";
            if (c.Stage != EditStage.Pending)
            {
                yield return "wait 0.2";
                yield return $"edit_apply {area}";
            }
        }
        yield return $"waitfor edit_stage_{area} == {stage} 120";
        if (c.Stage == EditStage.LastEdge) yield return $"edit_last {area}";
        // One UI frame, so the build window shows the buttons of this stage.
        yield return "wait 0.2";
        yield return $"edit_press {area} {c.Control}";
        yield return "wait 1";
        yield return $"edit_check {area} {c.Mode} {c.Stage} {c.Control}";
        yield return $"edit_reset {area}";
        yield return "wait 1";
    }

    internal static void Register()
    {
        // edit_end cut|weld: every case of the area (spec 2477). Then: expect edit_end_failed == 0.
        Autotest.Commands["edit_end"] = arg =>
        {
            var name = arg.Trim();
            var cases = EditEndModel.Cases(Area(name).Pulls).ToList();
            Log.Info($"AUTOTEST edit_end {name}: {cases.Count} cases");
            Autotest.Inject(cases.SelectMany(c => Lines(name, c)));
        };
        Autotest.Commands["edit_case"] = arg =>
        {
            var w = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var area = Area(w[0]);
            var mode = Enum.Parse<EditMode>(w[1]);
            var stage = Enum.Parse<EditStage>(w[2]);
            SetMode(area.Ship, mode == EditMode.Blueprint);
            area.SelectTool();
            area.Prepare(stage);
            area.Order();
        };
        Autotest.Commands["edit_apply"] = _ => Click(Box()._constructBtn, "Make it so");
        Autotest.Commands["edit_last"] = arg => Area(arg.Trim()).ToLastEdge();
        Autotest.Commands["edit_press"] = arg =>
        {
            var w = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var area = Area(w[0]);
            var game = Game();
            var box = Box();
            _available = Enum.Parse<EditControl>(w[1]) switch
            {
                EditControl.CancelAll => Press(box._cancelAllBtn),
                EditControl.DiscardChanges => Press(box._discardBtn, via: box._menu),
                EditControl.RightClick => area.ToolSelected(game) && Do(() => area.RightClick(Autotest.RequireRun())),
                EditControl.Esc => Do(() => Esc(game)),
                EditControl.OtherTool => Press(box._selectPartsBtn),
                EditControl.CloseEditor => game.Gui.ShipGui.BuildButton.IsActive && Do(() => game.Gui.ShipGui.BuildButton.IsSelected = false),
                var k => throw new InvalidOperationException($"no press for {k}"),
            };
        };
        Autotest.Commands["edit_check"] = arg =>
        {
            var w = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var area = Area(w[0]);
            var c = new EditCase(Enum.Parse<EditMode>(w[1]), Enum.Parse<EditStage>(w[2]), Enum.Parse<EditControl>(w[3]));
            bool ended = area.Stage() is null;
            var got = !_available ? (ended ? (EditOutcome?)null : EditOutcome.Unavailable) : ended ? EditOutcome.Ends : EditOutcome.Stays;
            var want = EditEndModel.Expected(c);
            _checked++;
            var harm = area.Harm();
            if (got == want && harm is null) { Log.Info($"AUTOTEST edit_end {w[0]} {c}: {want} ({area.Describe()})"); return; }
            _failed++;
            Log.Error($"AUTOTEST edit_end {w[0]} {c}: expected {want}, got {got?.ToString() ?? "the order ended without a control"}"
                + $"{(harm is null ? "" : $", {harm}")} ({area.Describe()})");
        };
        Autotest.Commands["edit_reset"] = arg =>
        {
            var game = Game();
            Box()._blueprintsToggle.IsSelected = false;
            game.Gui.ShipGui.BuildButton.IsSelected = false;
            Area(arg.Trim()).Reset(game);
        };
        Autotest.Fields["edit_stage_cut"] = () => Stage("cut");
        Autotest.Fields["edit_stage_weld"] = () => Stage("weld");
        Autotest.Fields["edit_end_failed"] = () => _failed;
        Autotest.Fields["edit_end_checked"] = () => _checked;
    }

    static double Stage(string area) => Area(area).Stage() is { } s ? (int)s : -1;

    static bool Do(Action a)
    {
        a();
        return true;
    }

    /// <summary>Esc as GameGui.HandleHotkeys handles it for the selected tool: its reset, else the tool closes.</summary>
    static void Esc(GameRoot game)
    {
        var gui = game.Gui;
        if (gui.GetSelectionState() is not { } state || state.TryReset()) return;
        if (state.ClearOnEscape) gui.StateSelectionManager.ClearSelected();
    }
}

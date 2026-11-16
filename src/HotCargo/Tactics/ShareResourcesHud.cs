using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tactics;

/// <summary>
/// Spec 1318. Co-op only: the "Share resources" switch right of the ship tool buttons, above every vanilla panel in its column
/// (Ux.HudStack), under the listen-in list (requirement 1132). GUI only: a click sends the MP input; the label reads the run state.
/// </summary>
static class ShareResourcesHud
{
    const float Width = 260f, Height = 36f;
    static ImageButton? _button;
    static Cosmoteer.Game.GameRoot? _game;

    /// <summary>The switch, for the listen-in list that stacks above it. Null before the first frame of a co-op game.</summary>
    internal static Widget? Button => _button;

    /// <summary>
    /// For autotests: the label that the switch shows now, or null while the players cannot see it: hidden, off the screen, or under a
    /// vanilla panel or an open window (Ux.HudStack.Visible).
    /// </summary>
    internal static string? Label => _button is { } b && _game is { } g && Ux.HudStack.Visible(g.Gui, b) ? b.Text.ToString() : null;

    /// <summary>For test logs: where the switch is and what covers it.</summary>
    internal static string Report() => _button is { } b && _game is { } g ? Ux.HudStack.Report(g.Gui, b) : "no switch yet";

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
    static class FramePatch
    {
        static double _next;

        static void Postfix(CareerGameModeManager __instance)
        {
            if (Run.Current is not { } run || run.Mode != __instance || !Ux.UxKit.HudDue(ref _next)) return;
            Errors.Guard("share resources switch", () => Update(run, __instance));
        }
    }

    static void Update(Run run, CareerGameModeManager mode)
    {
        var game = run.Game;
        if (_button is null || _game != game)
        {
            var b = new ImageButton();
            // CopySettingsFrom copies the anchor, the size and the input block mode too, so they are set after it.
            b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
            b.AnchorRect = AnchorPresets.BottomLeft;
            b.InputBlockMode = InputBlockMode.Auto;
            b.Width = Width;
            b.Height = Height;
            foreach (var r in new[] { b.StateNormalTextRenderer, b.StateHighlightedTextRenderer, b.StatePressedTextRenderer, b.StateDisabledTextRenderer })
                r.FontSize = 16;
            b.Clicked += (_, _) => Errors.Guard("share resources click", () =>
                game.NetManager.EnqueueInput(new ShareResourcesInput(!run.State.SharedResources)));
            // Under the floating windows, like the listen-in list.
            var top = game.Gui.RootWidget.Children;
            int windows = top.IndexOf(game.Gui.FloatingWindows);
            if (windows >= 0) top.Insert(windows, b); else top.Add(b);
            Ux.HudStack.Register(game.Gui, b);
            (_button, _game) = (b, game);
        }
        _button.SelfActive = game.HumanPlayersIndexes.Count > 1 && game.MapDisplayFactor < 1f;
        if (!_button.SelfActive) return;
        _button.Text = Ai.AiText.Text(run.State.SharedResources ? "ShareButtonOn" : "ShareButtonOff");
        Ux.HudStack.Place(game.Gui, _button, []);
    }
}

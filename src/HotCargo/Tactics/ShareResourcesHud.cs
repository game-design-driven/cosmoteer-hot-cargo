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
/// Spec 1318. Co-op only: the "Share resources" switch above the comms button, under the listen-in list (requirement 1132).
/// GUI only: a click sends the MP input; the label reads the run state.
/// </summary>
static class ShareResourcesHud
{
    const float Width = 260f, Height = 36f, Gap = 8f;
    static ImageButton? _button;
    static object? _game;

    /// <summary>Space that the switch takes above the comms button, so the listen-in list sits above it.</summary>
    internal static float ReservedHeight => _button is { SelfActive: true } ? Height + Gap : 0f;

    /// <summary>For autotests and screenshots: the label that the switch shows now, or null while it is hidden.</summary>
    internal static string? Label => _button is { SelfActive: true } b ? b.Text.ToString() : null;

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
            var b = new ImageButton { AnchorRect = AnchorPresets.BottomLeft, Width = Width, Height = Height, InputBlockMode = InputBlockMode.Auto };
            b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
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
            (_button, _game) = (b, game);
        }
        _button.SelfActive = game.HumanPlayersIndexes.Count > 1 && game.MapDisplayFactor < 1f;
        if (!_button.SelfActive) return;
        _button.Text = Ai.AiText.Text(run.State.SharedResources ? "ShareButtonOn" : "ShareButtonOff");
        // Above the comms button, or above the comms box while it is open. Without the button (no ship selected): above the tool row.
        var root = game.Gui.RootWidget.ScreenRect;
        var below = mode.CommsToolbox is { SelfActive: true } box ? box.ScreenRect : mode._commsBtn is { SelfActive: true } btn ? btn.ScreenRect : (Rect?)null;
        _button.X = below is { } r1 ? r1.X - root.X : 10f;
        _button.Y = below is { } r2 ? r2.Y - root.Bottom - Gap - Height : -80f - Height;
    }
}

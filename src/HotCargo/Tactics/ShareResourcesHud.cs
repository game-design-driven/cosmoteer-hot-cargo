using Cosmoteer.Game.Gui;
using Cosmoteer.Modes.Career;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tactics;

/// <summary>
/// Spec 1318. Co-op only: the "Share resources" switch right of the ship tool buttons, above every vanilla panel in its column
/// (Ux.HudStack), under the listen-in list (requirement 1132). It is a vanilla ship button (ShipGui.SHIP_UI_BUTTON, like the cancel
/// button of that row) with an icon: green while sharing is on, HUD blue while it is off, as the vanilla on/off tool icons.
/// GUI only: a click sends the MP input; the icon and the tooltip read the run state.
/// </summary>
static class ShareResourcesHud
{
    static ImageButton? _button;
    static Cosmoteer.Game.GameRoot? _game;
    static bool? _shown;
    static (ISprite On, ISprite Off)? _icons;

    /// <summary>The switch, for the listen-in list that stacks above it. Null before the first frame of a co-op game.</summary>
    internal static Widget? Button => _button;

    /// <summary>
    /// For autotests: the state that the switch shows now ("Share resources: ON" or "OFF"), or null while the players cannot see it:
    /// hidden, off the screen, or under a vanilla panel or an open window (Ux.HudStack.Visible).
    /// </summary>
    internal static string? Label => _button is { } b && _game is { } g && _shown is { } on && Ux.HudStack.Visible(g.Gui, b) ? State(on) : null;

    /// <summary>For test logs: where the switch is and what covers it.</summary>
    internal static string Report() => _button is { } b && _game is { } g ? Ux.HudStack.Report(g.Gui, b) : "no switch yet";

    static string State(bool on) => Ai.AiText.Text(on ? "ShareButtonOn" : "ShareButtonOff");

    static string Tip(bool on) => State(on) + "\n" + Ai.AiText.Text(on ? "ShareTipOn" : "ShareTipOff");

    static ISprite Icon(bool on)
    {
        _icons ??= (Ui.ModSprite.Read("tactics/share_icons.rules", "On"), Ui.ModSprite.Read("tactics/share_icons.rules", "Off"));
        return on ? _icons.Value.On : _icons.Value.Off;
    }

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
            // The settings of the vanilla cancel button on the tool button row (ShipGui constructor): 50 x 50, framed, icon filled.
            var b = new ImageButton(Icon(false), ShipGui.SHIP_UI_BUTTON);
            foreach (var n in new[] { b.StateNormalNineSlice, b.StateHighlightedNineSlice, b.StatePressedNineSlice, b.StateDisabledNineSlice })
                n.Flags = NineSliceFlags.All;
            b.ImageRectController.InnerRectMode = InnerRectMode.Fill;
            b.ImageSprite.ScaleMode = SpriteScaleMode.FillMaintainAspect;
            // The copy sets the anchor and the input block mode too, so they are set after it.
            b.AnchorRect = AnchorPresets.BottomLeft;
            b.InputBlockMode = InputBlockMode.Auto;
            b.ToolTipDisplay = game.Gui.ToolTipAbove;
            b.ToolTips.TextProvider = new Ux.LiveText(() => Tip(run.State.SharedResources));
            b.Clicked += (_, _) => Errors.Guard("share resources click", () =>
                game.NetManager.EnqueueInput(new ShareResourcesInput(!run.State.SharedResources)));
            // Under the floating windows, like the listen-in list.
            var top = game.Gui.RootWidget.Children;
            int windows = top.IndexOf(game.Gui.FloatingWindows);
            if (windows >= 0) top.Insert(windows, b); else top.Add(b);
            Ux.HudStack.Register(game.Gui, b);
            (_button, _game, _shown) = (b, game, false);
        }
        _button.SelfActive = game.HumanPlayersIndexes.Count > 1 && game.MapDisplayFactor < 1f;
        if (!_button.SelfActive) return;
        bool on = run.State.SharedResources;
        if (_shown != on)
        {
            _button.ImageSprite.CopySpriteFrom(Icon(on));
            _shown = on;
        }
        Ux.HudStack.Place(game.Gui, _button, []);
    }
}

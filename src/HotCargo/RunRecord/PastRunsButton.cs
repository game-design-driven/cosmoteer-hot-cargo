using Cosmoteer;
using Cosmoteer.Gui;
using Halfling;
using Halfling.Gui;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.RunRecord;

/// <summary>
/// Spec 1227: a "Past Runs" button in the top bar of the main menu, after the vanilla buttons on the left, only while the mod is on.
/// It opens the Past Runs window.
/// </summary>
[HarmonyPatch(typeof(TitleScreen), MethodType.Constructor)]
static class PastRunsButton
{
    /// <summary>The button of the newest title screen, for the test commands.</summary>
    internal static ImageButton? Shown { get; private set; }

    static void Postfix(TitleScreen __instance) => Errors.Guard("past runs button", () =>
    {
        if (!Activation.IsModEnabled()) return;
        // The vanilla top bar holds Continue, New Game, Load Game and Multiplayer on the left (TitleScreen constructor).
        // The bar is a docked child of the screen's fill box; docked widgets have no Parent while the constructor runs.
        var bar = __instance.RootWidget.Children.OfType<LayoutBox>().SelectMany(b => b.DockedChildren.OfType<LayoutBox>())
            .FirstOrDefault(b => b.DockedChildren.Contains(__instance._continueGameBtn))
            ?? throw new InvalidOperationException("no main menu bar holds the Continue button");
        // A flat glyph in the style of the vanilla main menu icons (blue, glow, 48 x 48), from mod/runrecord/icons.rules.
        var button = new ImageButton(Ui.ModSprite.Read("runrecord/icons.rules", "MenuButton"), WidgetRules.Instance.MainMenuIconButton)
        {
            DockMode = DockMode.Left,
            Text = RecordRows.Text("MenuButton"),
        };
        button.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
        button.Clicked += (_, _) => Errors.Guard("past runs window", OpenWindow);
        bar.DockedChildren.Add(button);
        Shown = button;
    });

    internal static void OpenWindow() => App.Director.PushState(new PastRunsDialog());
}

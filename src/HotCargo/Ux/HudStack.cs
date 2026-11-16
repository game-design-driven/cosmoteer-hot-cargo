using Cosmoteer.Game.Gui;
using Halfling.Gui;

namespace HotCargo.Ux;

/// <summary>
/// Spec 1132, 1318. Places the bottom-left HUD widgets of Hot Cargo (the Share resources switch, the listen-in list) next to the ship tool
/// buttons, above every vanilla panel in their column, and checks that the players can see them. The panels are the game's own list of UI that covers the view
/// (GameGui.IndicatorBlockers: the ships card, the tool buttons, the open toolbox, the minimap and others), so a new vanilla panel counts
/// without a change here.
/// </summary>
static class HudStack
{
    const float Gap = 8f;
    static readonly HashSet<Widget> Ours = [];

    /// <summary>
    /// Adds a Hot Cargo widget to the vanilla indicator blockers, so ship markers move off it as they do off vanilla panels. The stack
    /// never counts these widgets on its own; a caller names the ones that it sits above.
    /// </summary>
    internal static void Register(GameGui gui, Widget widget)
    {
        gui.IndicatorBlockers.Add(widget);
        Ours.Add(widget);
    }

    /// <summary>Widgets anchored bottom-left: X and Y count from the bottom-left corner of the root, Y up is negative.</summary>
    internal static void Place(GameGui gui, Widget widget, IEnumerable<Widget?> under)
    {
        if (widget.AnchorRect != AnchorPresets.BottomLeft) throw new InvalidOperationException($"{widget.GetType().Name} is not anchored bottom-left");
        var screen = Box(gui.RootWidget);
        // First choice: right of the vanilla tool buttons, on their row, so it stays under the chat lines (they end just above the tool
        // buttons at the largest UI scale). Second choice, when an open window covers that spot (a station comms window), or while the
        // tool buttons are hidden (no ship selected): the tool button column at the bottom left.
        var row = gui.ShipGui._toolButtonsBox;
        float left = row.Children.BorderPadding.Left;
        float[] columns = Shown(row) ? [Box(row).Right - screen.X, left] : [left];
        var blockers = Panels(gui).Concat(under.OfType<Widget>().Where(Shown).Select(Box)).ToList();
        var covers = blockers.Concat(Windows(gui)).ToList();
        (float X, float Top)? first = null;
        foreach (float x in columns)
        {
            float top = HudStackModel.StackTop(screen, screen.X + x, widget.Width, widget.Height, Gap, blockers);
            first ??= (x, top);
            if (!HudStackModel.Visible(new HudBox(screen.X + x, top, widget.Width, widget.Height), screen, covers)) continue;
            first = (x, top);
            break;
        }
        widget.X = first!.Value.X;
        widget.Y = first.Value.Top - screen.Bottom;
    }

    /// <summary>True when the widget is active, on the screen, and under no vanilla panel and no open window.</summary>
    internal static bool Visible(GameGui gui, Widget widget) =>
        Shown(widget) && HudStackModel.Visible(Box(widget), Box(gui.RootWidget), Covers(gui));

    /// <summary>For test logs: the widget's rect and the vanilla panels and windows that cover it.</summary>
    internal static string Report(GameGui gui, Widget widget)
    {
        var me = Box(widget);
        var covers = gui.IndicatorBlockers.Concat(gui.FloatingWindows.Children).Where(w => !Ours.Contains(w) && Shown(w) && Box(w).Overlaps(me)).Select(w => $"{w.GetType().Name} {Box(w)}");
        return $"{me} on screen {Box(gui.RootWidget)}: active {widget.IsActive}, covered by [{string.Join(", ", covers)}]";
    }

    static IEnumerable<HudBox> Panels(GameGui gui) => gui.IndicatorBlockers.Where(w => !Ours.Contains(w) && Shown(w)).Select(Box);

    static IEnumerable<HudBox> Windows(GameGui gui) => gui.FloatingWindows.Children.Where(Shown).Select(Box);

    static IEnumerable<HudBox> Covers(GameGui gui) => Panels(gui).Concat(Windows(gui));

    /// <summary>Active and not of zero size (hidden tween states have zero size, as in spec 1481).</summary>
    static bool Shown(Widget w) => w.IsActive && w.ScreenRect.Width >= 1f && w.ScreenRect.Height >= 1f;

    static HudBox Box(Widget w)
    {
        var r = w.ScreenRect;
        return new HudBox(r.X, r.Y, r.Width, r.Height);
    }
}

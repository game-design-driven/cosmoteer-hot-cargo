using Cosmoteer.Gui.ToolTips;
using Halfling.Gui;
using Halfling.Gui.Components.ToolTips;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Tech;
using HotCargo.Ui;

namespace HotCargo.Ux;

/// <summary>
/// Spec 2585: hover checks of breakdown tooltips. A test opens the tooltip of a real widget through the game's tooltip display, as the
/// mouse does, and reads the text of the real tooltip label. It fails when the player cannot see the widget.
/// </summary>
static class BreakdownAutotest
{
    static (MultiToolTipBox Box, WidgetToolTips Source, string Name, BreakdownUnit Unit, Func<double> GameValue)? _hover;

    /// <summary>The text that the open hover tooltip shows. For screenshots of the other areas.</summary>
    internal static string Shown => _hover is { } h ? h.Box.PrimaryToolTip.TooltipLabel.Text ?? "" : "";

    /// <summary>
    /// Opens the tooltip of <paramref name="w"/> as a hover does. gameValue: the number that the game uses (the sim or the roll),
    /// read again at each check, so the total line of the tooltip must show it.
    /// </summary>
    internal static void Hover(Widget w, string name, BreakdownUnit unit, Func<double> gameValue)
    {
        var screen = w.Root?.RootWidget.ScreenRect ?? throw new InvalidOperationException($"'{name}' is not in a GUI");
        var r = w.ScreenRect;
        bool onScreen = r.Width > 0 && r.Height > 0 && r.Left >= screen.Left && r.Right <= screen.Right && r.Top >= screen.Top && r.Bottom <= screen.Bottom;
        // A scroll box shows only the part of its page inside its own rect.
        for (var p = w.Parent?.Widget; p is not null && onScreen; p = p.Parent?.Widget)
            if (p is ScrollBox scroll) onScreen = r.Top >= scroll.ScreenRect.Top && r.Bottom <= scroll.ScreenRect.Bottom;
        if (!w.IsActive || !onScreen) throw new InvalidOperationException($"the player cannot see '{name}' (active {w.IsActive}, rect {r}, screen {screen})");
        if (string.IsNullOrEmpty(w.ToolTips.Text)) throw new InvalidOperationException($"'{name}' has no tooltip");
        var box = w.GetToolTipDisplay() as MultiToolTipBox ?? throw new InvalidOperationException($"'{name}' has no tooltip box");
        box.SetToolTipSource(w.ToolTips);
        _hover = (box, w.ToolTips, name, unit, gameValue);
        Log.Info($"ux: hover {name}");
    }

    internal static void Register()
    {
        // hovertip heatrate|techvalue: opens the breakdown tooltip of that heat panel line (spec 2582, 2583). Diplomacy adds its own
        // targets (dip_hovertip). Then: wait 1, expect hovertip_ok == 1.
        Autotest.Commands["hovertip"] = arg =>
        {
            var run = Autotest.RequireRun();
            var panel = Walk(run.Game.Gui.RootWidget).OfType<HeatPanel>().FirstOrDefault(p => p.IsActive)
                ?? throw new InvalidOperationException("the player cannot see the heat panel");
            switch (arg.Trim())
            {
                // The rate that the sim adds: time heat and the contract trail (Run.OnTick, ContractBook.OnTick).
                case "heatrate":
                    Hover(panel.StatusLabel, "heat rate", BreakdownUnit.HeatPerMinute, () =>
                    {
                        var r = Autotest.RequireRun();
                        return r.TimeHeatPerMinute(r.State.FleetTech) + Contracts.ContractBook.TrailPerMinute(r, Contracts.ContractBook.OpenWeight(r));
                    });
                    break;
                case "techvalue":
                    Hover(panel.TechLabel, "tech value", BreakdownUnit.Credits, () => Autotest.RequireRun().TechValue());
                    break;
                default: throw new InvalidOperationException("hovertip needs: heatrate | techvalue");
            }
        };
        // 1 when the game's tooltip box shows the hovered widget's tooltip and its total line is the number that the game uses.
        Autotest.Fields["hovertip_ok"] = () =>
        {
            var (box, source, name, unit, value) = _hover ?? throw new InvalidOperationException("no hovertip open");
            string shown = box.PrimaryToolTip.TooltipLabel.Text ?? "";
            string total = BreakdownText.TotalLine(unit, value());
            bool ok = box.IsActive && box.ToolTipDisplayController.CurrentSource == source && shown.Contains(total, StringComparison.Ordinal);
            Log.Info($"ux: hovertip {name} ok={ok} expects '{total}' shows:\n{shown}");
            return ok ? 1 : 0;
        };
        Autotest.Commands["hovertip_clear"] = _ =>
        {
            if (_hover is { } h) h.Box.ClearToolTipSource(h.Source);
            _hover = null;
        };
    }

    static IEnumerable<Widget> Walk(Widget w) => w.GetChainedWidgets(recursive: true).Prepend(w);
}

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
    /// <summary>Display: where the widget's tooltips go (the game GUI's multi box, or a dialog's own box). Box: the box with the label.</summary>
    static (IToolTipDisplay Display, ToolTipBox Box, WidgetToolTips Source, string Name, BreakdownUnit Unit, Func<double> GameValue)? _hover;

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
        var display = w.GetToolTipDisplay() ?? throw new InvalidOperationException($"'{name}' has no tooltip box");
        var box = display switch
        {
            MultiToolTipBox multi => multi.PrimaryToolTip,
            ToolTipBox single => single,
            _ => throw new InvalidOperationException($"'{name}' shows tooltips in a {display.GetType().Name}"),
        };
        display.SetToolTipSource(w.ToolTips);
        _hover = (display, box, w.ToolTips, name, unit, gameValue);
        Log.Info($"ux: hover {name}");
    }

    internal static void Register()
    {
        // hovertip heatrate|techvalue: opens the breakdown tooltip of that heat panel line (spec 2582, 2583). Diplomacy adds its own
        // targets (dip_hovertip). Then: wait 1, expect hovertip_ok == 1.
        Autotest.Commands["hovertip"] = arg =>
        {
            var run = Autotest.RequireRun();
            HeatPanel Panel() => Walk(run.Game.Gui.RootWidget).OfType<HeatPanel>().FirstOrDefault(p => p.IsActive)
                ?? throw new InvalidOperationException("the player cannot see the heat panel");
            switch (arg.Trim())
            {
                // The rate that the sim adds: time heat and the contract trail (Run.OnTick, ContractBook.OnTick).
                case "heatrate":
                    Hover(Panel().StatusLabel, "heat rate", BreakdownUnit.HeatPerMinute, () =>
                    {
                        var r = Autotest.RequireRun();
                        return r.TimeHeatPerMinute(r.State.FleetTech) + Contracts.ContractBook.TrailPerMinute(r, Contracts.ContractBook.OpenWeight(r));
                    });
                    break;
                case "techvalue":
                    Hover(Panel().TechLabel, "tech value", BreakdownUnit.Credits, () => Autotest.RequireRun().TechValue());
                    break;
                // The trade tab price line of the open comms window (spec 2586). The patch scales each buy price by the markup.
                case "tradeprice":
                    var box = TradeHints.LastBox?.TryGetTarget(out var b) == true ? b : throw new InvalidOperationException("no trade tab opened");
                    var tab = TradeHints.Last?.TryGetTarget(out var t) == true ? t : throw new InvalidOperationException("no trade tab opened");
                    Hover(box.Children.OfType<Label>().Single(), "trade price", BreakdownUnit.Factor, () =>
                    {
                        var faction = Cosmoteer.Factions.FactionRules.GetFaction(tab.Callee.Metadata.PlayerIndex)
                            ?? throw new InvalidOperationException("the trade station has no faction");
                        return tab.SimMode.GameMode.GetCurrentReputationTitle(faction.ID).ResourceBuyCostFactor * Factions.FactionsArea.Markup(Autotest.RequireRun());
                    });
                    break;
                default: throw new InvalidOperationException("hovertip needs: heatrate | techvalue | tradeprice");
            }
        };
        // 1 when the game's tooltip box shows the hovered widget's tooltip and its total line is the number that the game uses.
        Autotest.Fields["hovertip_ok"] = () =>
        {
            var (_, box, source, name, unit, value) = _hover ?? throw new InvalidOperationException("no hovertip open");
            string shown = box.TooltipLabel.Text ?? "";
            string total = BreakdownText.TotalLine(unit, value());
            bool ok = box.IsActive && box.TooltipLabel.ToolTipDisplayController.CurrentSource == source && shown.Contains(total, StringComparison.Ordinal);
            Log.Info($"ux: hovertip {name} ok={ok} expects '{total}' shows:\n{shown}");
            return ok ? 1 : 0;
        };
        Autotest.Commands["hovertip_clear"] = _ =>
        {
            if (_hover is { } h) h.Display.ClearToolTipSource(h.Source);
            _hover = null;
        };
    }

    static IEnumerable<Widget> Walk(Widget w) => w.GetChainedWidgets(recursive: true).Prepend(w);
}

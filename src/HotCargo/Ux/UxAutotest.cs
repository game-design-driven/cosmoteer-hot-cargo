using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Halfling;
using Halfling.Geometry;
using HotCargo.Dev;
using HotCargo.Tech;

namespace HotCargo.Ux;

/// <summary>Autotest commands and fields for the batch 2 UX items (UX.md V7 to V15, H8). Test aids only.</summary>
static class UxAutotest
{
    static Cosmoteer.Modes.Career.Missions.Mission? _highlightMarked;

    static Cosmoteer.Modes.Career.Missions.Mission LatestOfType(Core.Run run, string metatype)
    {
        var id = new ID<Cosmoteer.Modes.Career.Missions.MissionMetatype>(metatype);
        return run.Mode.Missions.LastOrDefault(m => m.MetatypeID.Equals(id)) ?? throw new InvalidOperationException($"no {metatype} mission in the log");
    }

    internal static void Register()
    {
        // Payment hint: hints shown, and the objective lines of open missions that read the hint (the game's own display lines).
        Autotest.Fields["ux_payment_hints"] = () => PaymentHint.HintsShown;
        Autotest.Fields["ux_payment_lines"] = () =>
        {
            var run = Autotest.RequireRun();
            var lines = new List<Cosmoteer.Modes.Career.Missions.Objectives.Objective.DisplayLine>();
            foreach (var m in run.Mode.Missions) m.GetObjectiveDisplayLines(lines);
            return lines.Count(l => l.Text.Text == UxKit.Text("PaymentHereLine"));
        };
        RegisterMissionSelect();
        RegisterJumpWarnings();
        // uxpick N: presses dialogue key N (1-based) in the frontmost dialogue (event window first, then comms).
        // uxsubmit: presses Enter in the frontmost dialogue: the first active submit button of a page with option rows (UX V15).
        Autotest.Commands["uxsubmit"] = _ => Autotest.Check(DialogueKeys.Submit(), "Enter picked a submit button");
        Autotest.Commands["uxpick"] = arg =>
        {
            int n = int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            Autotest.Check(DialogueKeys.Pick(n - 1), $"dialogue key {n} picked an option");
        };
        // supplies: logs the watched supplies and the player ship's fraction of each.
        Autotest.Commands["supplies"] = _ =>
        {
            var ship = TechAutotest.PlayerShip();
            var parts = LowSupplies.Watched(ship.Rules).Select(id =>
                $"{id}={ship.Resources.GetResourceTotal(id)}/{ship.Resources.GetCapacityTotal(id, includeImplicit: true, includeUntyped: false)}");
            Log.Info($"ux: supplies {string.Join(" ", parts)}");
        };
        // wanted: test only. Makes the nearest station's faction want the player, as an attack on it would.
        Autotest.Commands["wanted"] = _ =>
        {
            var run = Autotest.RequireRun();
            run.Mode.SimMode.Wanted.WarTriggeredByPlayer(TechAutotest.NearestStation());
        };
        // Spec 1128. ux_click_marker <metatype ID>: clicks the indicator of the latest mission of that type, through the indicator's own
        // click controller, as a mouse click does. ux_click_area <metatype ID>: the logic of a left-click on empty space at the center
        // of that mission's area (a test cannot press the mouse in space). ux_highlight_mark <metatype ID> remembers that mission; the
        // field ux_highlight_marked is 1 while it is the highlighted one, ux_highlight_shown 1 while the list shows it highlighted.
        Autotest.Commands["ux_click_marker"] = arg =>
        {
            var run = Autotest.RequireRun();
            var m = LatestOfType(run, arg.Trim());
            var poi = run.Game.Sim.Doodads.OfType<Cosmoteer.Simulation.Doodads.POIDoodad>().Where(m.IsPOIAssociated).OrderBy(p => p.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException($"the latest {arg.Trim()} mission has no marker in this system");
            var indicator = run.Game.Gui._indicators.FirstOrDefault(kv => kv.Key is Cosmoteer.Simulation.Doodads.POIDoodad.POIIndicatorSource s && s.Doodad == poi).Value;
            if (indicator is not null) indicator.Components.OfType<Halfling.Gui.Components.Input.WidgetClickController>().First().InvokeClicked();
            else
            {
                // The headless sim draws no screen, so it has no indicator widgets. A click on one calls its source's OnClicked.
                Log.Info($"AUTOTEST marker {poi.UniqueID} has no indicator widget (no screen); calling the indicator source's OnClicked");
                new Cosmoteer.Simulation.Doodads.POIDoodad.POIIndicatorSource(poi).OnClicked();
            }
            Autotest.Wait(1);
        };
        Autotest.Commands["ux_click_area"] = arg =>
        {
            var run = Autotest.RequireRun();
            var area = LatestOfType(run, arg.Trim()).Objectives.Select(o => o.GetAreaLocation(run.Game)).FirstOrDefault(c => c is not null)
                ?? throw new InvalidOperationException($"the latest {arg.Trim()} mission has no area in this system");
            MissionHighlight.FromPoint(run, area.Center);
            Autotest.Wait(1);
        };
        // Spec 1129. ux_open_details <metatype ID>: what a left-click on that mission's name in the Nearby Missions list runs (a test
        // cannot click the list). The field ux_details_shown is 1 while the open mission log shows the mission of ux_highlight_mark.
        Autotest.Commands["ux_open_details"] = arg =>
        {
            var run = Autotest.RequireRun();
            MissionDetails.Open(run.Game, LatestOfType(run, arg.Trim()));
            Autotest.Wait(1);
        };
        Autotest.Fields["ux_details_shown"] = () => _highlightMarked is { } m && MissionDetails.Shows(Autotest.RequireRun().Game, m) ? 1 : 0;
        Autotest.Commands["ux_highlight_mark"] = arg => _highlightMarked = LatestOfType(Autotest.RequireRun(), arg.Trim());
        Autotest.Fields["ux_highlight_marked"] = () => _highlightMarked is { } m && MissionHighlight.Current == m ? 1 : 0;
        Autotest.Fields["ux_highlight_shown"] = () => MissionHighlight.ShownInList(Autotest.RequireRun().Game) ? 1 : 0;
        // selectparts: selects every part of the player ship, so the parts card shows the several-parts info.
        Autotest.Commands["selectparts"] = _ =>
        {
            var ship = TechAutotest.PlayerShip();
            ship.Sim.PlayerInput.SelectParts(ship.Parts.ToList());
            var gui = Autotest.RequireRun().Game.Gui.ShipGui;
            Log.Info($"ux: selected {ship.Sim.PlayerInput.SelectedParts.Count} parts; ship gui active={gui.SelfActive} parts card={gui.PartsCard.SelfActive}");
        };
        // partsstate: logs the part selection and the parts card state.
        Autotest.Commands["partsstate"] = _ =>
        {
            var game = Autotest.RequireRun().Game;
            var gui = game.Gui.ShipGui;
            Log.Info($"ux: selected parts {game.Sim.PlayerInput.SelectedParts.Count}, ships {game.Sim.PlayerInput.SelectedShips.Count}; ship gui={gui.SelfActive} parts card={gui.PartsCard.SelfActive} active={gui.PartsCard.IsActive} map={game.MapDisplayFactor}");
        };
        Autotest.Fields["ux_parts_info"] = () => MultiPartInfo.Shown;
        // supplycheck: runs the low supply check now, without the one-second wait.
        Autotest.Commands["supplycheck"] = _ =>
        {
            var run = Autotest.RequireRun();
            LowSupplies.Poll(run.Game, run.Game.Sim, run.T.Ux);
        };
        // commstab trade|dialogue: selects a tab in the comms window that comms open opened (the tab builds on first show).
        Autotest.Commands["commstab"] = arg =>
        {
            var window = UxArea.OpenComms;
            var tab = arg switch
            {
                "trade" => window._tradeTab,
                "dialogue" => window._dialogueTab,
                _ => throw new InvalidOperationException("commstab needs: trade | dialogue"),
            } ?? throw new InvalidOperationException($"the station has no {arg} tab");
            window._tabs.TabsListBox.SelectedWidget = tab;
        };
        // commsmission: shows the Missions tab of the open comms window and selects the offer that contract_pick chose
        // (or the first offer), so a screenshot shows its info panel with the contract terms. Test only.
        Autotest.Commands["commsmission"] = _ =>
        {
            var window = UxArea.OpenComms;
            window._tabs.TabsListBox.SelectedWidget = window._missionsTab ?? throw new InvalidOperationException(
                $"the station has no missions tab: answers={window.SimMode.WillShipAnswerCall(window.Caller, window.Callee)} missions={window.Callee.AI?.WillProvideMissions(window.Caller)} window active={window.SelfActive}");
        };
        // commsselect [text]: selects the offer whose name contains text, else the offer that contract_pick chose, else the first.
        Autotest.Commands["commsselect"] = arg => SelectMission(UxArea.OpenComms, arg, Contracts.ContractsArea.PickedForTests);
        // infoscroll comms|log: scrolls the mission info panel of the comms window or the mission log to the bottom (the contract terms).
        Autotest.Commands["infoscroll"] = arg =>
        {
            Halfling.Gui.Widget window = arg switch
            {
                "comms" => UxArea.OpenComms,
                "log" => Autotest.RequireRun().Mode._missionLog ?? throw new InvalidOperationException("no mission log"),
                _ => throw new InvalidOperationException("infoscroll needs: comms | log"),
            };
            var info = Walk(window).OfType<Cosmoteer.Modes.Career.Missions.MissionsPanel.MissionInfoPanel>().FirstOrDefault(p => p.IsActive)
                ?? throw new InvalidOperationException("no mission info panel is shown");
            info.VerticalScrollBar.Value = info.VerticalScrollBar.MaxValue;
        };
        // missionlog open|close: opens or closes the career log window, as the HUD button does.
        Autotest.Commands["missionlog"] = arg =>
        {
            var mode = Autotest.RequireRun().Mode;
            bool open = mode._missionLog is { SelfActive: true, Parent: not null };
            if ((arg == "open") != open) mode.OnMissionLogClicked(null, EventArgs.Empty);
        };
        // logselect [text]: selects the mission in the open mission log whose name contains text, else the first.
        Autotest.Commands["logselect"] = arg =>
            SelectMission(Autotest.RequireRun().Mode._missionLog ?? throw new InvalidOperationException("no mission log; run missionlog open first"), arg, null);
        // tipshow map|mapcur|station|fame|money|item|text <xml>: shows that tooltip (item: the shown item action button, spec 1111) at a fixed spot, for screenshots. The capture hides hover
        // tooltips, because the real cursor rests somewhere over the hidden window. tipclear removes it.
        Autotest.Commands["tipshow"] = arg =>
        {
            var run = Autotest.RequireRun();
            var parts = arg.Split(' ', 2);
            string text = parts[0] switch
            {
                "map" => run.Mode.GetMapNodeToolTip(NearestOtherNode(run)) ?? "",
                "mapcur" => run.Mode.GetMapNodeToolTip(run.Game.CurrentMapNode) ?? "",
                "station" => run.Game.Sim.Mode.GetShipMouseOverText(TechAutotest.NearestStation()),
                "fame" => Walk(run.Game.Gui.RootWidget).OfType<Cosmoteer.Game.Gui.PlayerFameLabel>().First().ToolTips.TextProvider?.Text ?? "",
                "money" => Walk(run.Game.Gui.RootWidget).OfType<Cosmoteer.Game.Gui.PlayerMoneyLabel>().First().ToolTips.TextProvider?.Text ?? "",
                "item" => Walk(run.Game.Gui.RootWidget).OfType<Events.ItemActionButton>().FirstOrDefault(b => b.SelfActive)?.ToolTips.TextProvider?.Text
                    ?? throw new InvalidOperationException("no item action button shows"),
                "text" when parts.Length == 2 => parts[1],
                // The hover text and the jump window tooltip of the first own ship that stays behind (spec 1562, 1564).
                "jumpship" => run.Game.Sim.Mode.GetShipMouseOverText(run.Game.Sim.Ships.LocalPlayerSignificantShips.OrderBy(s => s.UniqueID)
                    .FirstOrDefault(s => JumpWarnings.Standing(run.Game, s).Reason != StayReason.None) ?? throw new InvalidOperationException("no own ship stays behind")),
                "jumptip" => JumpWarnings.WindowTip(run.Game, FirstStaying(run)),
                _ => throw new InvalidOperationException("tipshow needs: map | mapcur | station | fame | money | item | jumpship | jumptip | text <xml>"),
            };
            TipClear(run.Game.Gui);
            var screen = run.Game.Gui.RootWidget.ScreenRect;
            _tip = new Cosmoteer.Gui.ToolTips.MultiToolTipBox(run.Game.Gui.ToolTipRight);
            run.Game.Gui.AddDropTarget(_tip);
            _tip.SetToolTipSource(new FixedTip(text, new Rect(screen.Width * 0.55f, screen.Height * 0.3f, 1f, 1f)));
            Log.Info($"ux: tooltip {parts[0]}: {text}");
        };
        Autotest.Commands["tipclear"] = _ => TipClear(Autotest.RequireRun().Game.Gui);
        // eventrect: logs the event window rect, to check that it fits its page.
        Autotest.Commands["eventrect"] = _ => Log.Info($"ux: event {Events.EventDialog.Rects}");
        Autotest.Commands["tradehint"] = _ => Log.Info($"ux: trade hint\n{TradeText()}");
        Autotest.Commands["wantedline"] = _ => Log.Info($"ux: wanted line: {WantedTimer.Text(Autotest.RequireRun().Mode)}");

        // Layout checks (the null graphics device of a headless game still lays out the GUI).
        Autotest.Fields["ux_win_centered"] = () => Centered(Ui.CenterPanel.Newest ?? throw new InvalidOperationException("no center panel is open"), "win panel");
        Autotest.Fields["ux_loss_centered"] = () => Centered(
            CareerInfoPatches.GameOver.LossPanel?.TryGetTarget(out var p) == true ? p : throw new InvalidOperationException("no loss panel"), "loss panel");
        Autotest.Fields["ux_trade_overlap"] = TradeOverlaps;
        Autotest.Fields["ux_event_text_numbers"] = () => DialogueKeys.EventTextNumbers();
        Autotest.Fields["ux_parts_xml"] = () => MultiPartInfo.HealthLabel?.TextRenderer.XmlFormatting == true ? 1 : 0;
        Autotest.Fields["ux_damage_hits"] = () => DamagePings.Hits;
        Autotest.Fields["ux_supply_alerts"] = () => LowSupplies.AlertsShown;
        Autotest.Fields["ux_event_open"] = () => Events.EventDialog.IsShown ? 1 : 0;
        Autotest.Fields["ux_dialogue_picks"] = () => DialogueKeys.Picks;
        Autotest.Fields["ux_trade_hint"] = () => TradeText().Length;
        Autotest.Fields["ux_wanted_line"] = () => WantedTimer.Text(Autotest.RequireRun().Mode).Length;
        Autotest.Fields["ux_comms_open"] = () => CommWindow.IsCommWindowOpen(Autotest.RequireRun().Game) ? 1 : 0;
        Autotest.Fields["ux_watched_battery"] = () => LowSupplies.Watched(TechAutotest.PlayerShip().Rules).Contains(new ID<ResourceRules>("battery")) ? 1 : 0;
        Autotest.Fields["ux_watched_carbon"] = () => LowSupplies.Watched(TechAutotest.PlayerShip().Rules).Contains(new ID<ResourceRules>("carbon")) ? 1 : 0;
    }

    /// <summary>1 when the panel's center is on the screen center (within 2 units) and the panel is inside the screen.</summary>
    static double Centered(Halfling.Gui.Widget panel, string what)
    {
        var r = panel.ScreenRect;
        var screen = panel.Root!.RootWidget.ScreenRect;
        Log.Info($"ux: {what} rect {r} screen {screen}");
        bool inside = r.Left >= screen.Left && r.Right <= screen.Right && r.Top >= screen.Top && r.Bottom <= screen.Bottom;
        return inside && Math.Abs(r.Center.X - screen.Center.X) < 2f && r.Width > 0 ? 1 : 0;
    }

    /// <summary>The number of trade page widgets that the hint box overlaps. Fails when the box has no height.</summary>
    static double TradeOverlaps()
    {
        if (TradeHints.LastBox?.TryGetTarget(out var box) != true || box.Parent?.Widget is not Halfling.Gui.ScrollBox page)
            throw new InvalidOperationException("no trade hint box");
        var b = box.ScreenRect;
        Log.Info($"ux: trade hint box {b}");
        if (!box.IsActive || b.Height < 10f) throw new InvalidOperationException($"the trade hint box is not shown or has no height: {b}");
        int n = 0;
        foreach (var w in page.Children.Concat(page.DockedChildren.Where(d => d != box)))
        {
            var r = w.ScreenRect;
            if (w.IsActive && r.Width > 0 && r.Height > 0 && b.Left < r.Right && r.Left < b.Right && b.Top < r.Bottom && r.Top < b.Bottom)
            {
                Log.Info($"ux: trade hint box overlaps {w.GetType().Name} {r}");
                n++;
            }
        }
        return n;
    }

    static string TradeText() =>
        TradeHints.Last?.TryGetTarget(out var tab) == true ? TradeHints.Text(tab) : throw new InvalidOperationException("no trade tab was opened");

    static Cosmoteer.Gui.ToolTips.MultiToolTipBox? _tip;

    static void TipClear(Cosmoteer.Game.Gui.GameGui gui)
    {
        if (_tip is not null) gui.RemoveDropTarget(_tip);
        _tip = null;
    }

    sealed class FixedTip(string xml, Rect at) : Halfling.Gui.Components.ToolTips.IToolTipSource
    {
        public Halfling.Gui.Components.Text.ITextProvider TextProvider { get; } = new Halfling.Gui.Components.Text.SimpleTextProvider(xml, null);
        public Halfling.Gui.Components.Rects.IRectProvider RectProvider { get; } = new Halfling.Gui.Components.Rects.SimpleRectProvider(at, null);
        public Halfling.Gui.Edges BoundingEdges => Halfling.Gui.Edges.None;
        public void OnAboutToBeDisplayed() { }
    }

    /// <summary>The map node nearest to the current one, for a tooltip of a system that the players have not visited.</summary>
    static Cosmoteer.GalaxyMap.MapNode NearestOtherNode(Core.Run run)
    {
        var here = run.Game.CurrentMapNode;
        return run.Game.Map.Nodes.Where(n => n != here).MinBy(n => n.Location.DistanceSquaredTo(here.Location))
            ?? throw new InvalidOperationException("the map has one node");
    }

    static void SelectMission(Halfling.Gui.Widget window, string text, Cosmoteer.Modes.Career.Missions.Mission? fallback)
    {
        var panel = Walk(window).OfType<Cosmoteer.Modes.Career.Missions.MissionsPanel>().FirstOrDefault()
            ?? throw new InvalidOperationException("no missions panel in the window");
        var item = (text.Length > 0 ? panel.FirstOrDefault(i => i.Mission.Name.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) : null)
            ?? panel.FirstOrDefault(i => i.Mission == fallback) ?? panel.FirstOrDefault()
            ?? throw new InvalidOperationException("the missions panel lists no missions");
        item.IsSelected = true;
        Log.Info($"ux: selected mission {item.Mission.Name.Text}");
    }

    /// <summary>The missions panel of the open comms window.</summary>
    static Cosmoteer.Modes.Career.Missions.MissionsPanel CommsPanel() =>
        Walk(UxArea.OpenComms).OfType<Cosmoteer.Modes.Career.Missions.MissionsPanel>().FirstOrDefault()
        ?? throw new InvalidOperationException("no missions panel in the comms window");

    /// <summary>Shown items of the open comms missions list, by kind.</summary>
    static int ListCount(MissionKind kind) => CommsPanel().Count(i => i.SelfActive && MissionSelect.KindOf(i.Mission) == kind);

    internal static void RegisterMissionSelect()
    {
        // missionfilter <All|HotCargo|Chains|Contracts|Vanilla|NoShady>: picks the filter of the open comms missions list, as a click does.
        Autotest.Commands["missionfilter"] = arg =>
        {
            var filter = Enum.Parse<MissionFilter>(arg);
            var list = Walk(CommsPanel()).OfType<Halfling.Gui.DropList>().FirstOrDefault(d => d.ListBox.Children.Count == Enum.GetValues<MissionFilter>().Length)
                ?? throw new InvalidOperationException("the comms missions list has no filter");
            list.ListBox.Children[(int)filter].IsSelected = true;
        };
        foreach (var kind in Enum.GetValues<MissionKind>())
            Autotest.Fields[$"ux_list_{kind.ToString().ToLowerInvariant()}"] = () => ListCount(kind);
        // Items whose right-hand text names jumps (an offer in another system) or a heat change.
        Autotest.Fields["ux_list_heat"] = () => CommsPanel().Count(i => i.SelfActive && i._timeDist.Text?.Contains("hc_heat") == true);
        // 1 when the chain station's badges name its story chain offer (spec 804).
        Autotest.Fields["ux_badge_chain"] = () => StationBadges.For(Chains.ChainsArea.TestStationShip(), TechAutotest.PlayerShip()).Contains("hc_mission_HotCargoChain") ? 1 : 0;
        Autotest.Fields["ux_list_icons"] = () => CommsPanel().Count(i => i.SelfActive && i._item.Text?.StartsWith("<image name='hc_mission_", StringComparison.Ordinal) == true);
    }

    static IEnumerable<Halfling.Gui.Widget> Walk(Halfling.Gui.Widget w) => w.GetChainedWidgets(recursive: true).Prepend(w);

    static byte[]? _twinDesign;

    static Cosmoteer.Ships.Ship FirstStaying(Core.Run run) =>
        run.Game.Sim.Ships.LocalPlayerSignificantShips.Where(s => JumpWarnings.PlannedReason(run.Game, s) != StayReason.None)
            .OrderBy(s => JumpWarnings.PlannedReason(run.Game, s) != StayReason.TooBig).ThenBy(s => s.UniqueID).FirstOrDefault()
        ?? throw new InvalidOperationException("no own ship stays behind");

    /// <summary>Spec 1569: jump warnings. A copy of the starter ship stands in for a captured ship (the starter ships have no hyperdrive).</summary>
    static void RegisterJumpWarnings()
    {
        // jw_drive notech: single player. Builds a hyperdrive on the own ship without tech, so the tech carrier stays behind (spec 1602).
        Autotest.Commands["jw_drive"] = arg =>
        {
            if (arg != "notech") throw new InvalidOperationException("jw_drive needs: notech");
            var ship = Autotest.RequireRun().Game.Sim.Ships.LocalPlayerSignificantShips.OrderBy(s => s.UniqueID).FirstOrDefault(s => Vaults.ShipTech(s) == 0)
                ?? throw new InvalidOperationException("no own ship without tech; run jw_twin spawn first");
            Factions.FactionsAutotest.BuildDrive(ship);
        };
        // jw_gatejump: single player. Moves every own ship with a working hyperdrive to the relay toward the first unvisited neighbor and
        // orders it to jump. fa_torelay and fa_gatejump act on the lowest ID ship, which is not always the one with the drive.
        Autotest.Commands["jw_gatejump"] = _ =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("jw_gatejump is single player only");
            var sim = run.Game.Sim;
            var ships = sim.Ships.LocalPlayerSignificantShips.Where(s => s.Ftl.HasFtlDrives).OrderBy(s => s.UniqueID).ToList();
            if (ships.Count == 0) throw new InvalidOperationException("no own ship with a working hyperdrive");
            var gate = Factions.FactionsAutotest.Gate();
            foreach (var ship in ships) Autotest.Teleport(ship, Autotest.ClearSpotNear(sim, gate.DetWorldCenter, 700f, ship.WorldBoundingRadius));
            // The relay leaves stasis when a ship comes near it. Ask again each time: the lookup returns a new object then.
            Autotest.WaitFor(() => Factions.FactionsAutotest.Gate().PhysicalShip is not null, 20, "the relay to leave stasis", () =>
            {
                foreach (var ship in ships) Factions.FactionsAutotest.OrderGateJump(ship);
                Log.Info($"AUTOTEST ux: {ships.Count} ships with a hyperdrive ordered to jump");
            });
        };
        Autotest.Fields["jw_ready"] = () => Autotest.RequireRun().Game.Sim.Ships.LocalPlayerSignificantShips.Count(s => s.Ftl.IsReadyToJump);
        // jw_twin save|spawn: single player. save keeps the design of the acting ship; spawn adds a crewed copy of it as an own ship nearby.
        Autotest.Commands["jw_twin"] = arg =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("jw_twin is single player only");
            var ship = TechAutotest.PlayerShip();
            if (arg == "save")
            {
                using var stream = new MemoryStream();
                ship.SaveDesign(stream);
                _twinDesign = stream.ToArray();
                return;
            }
            if (arg != "spawn") throw new InvalidOperationException("jw_twin needs: save | spawn");
            var sim = run.Game.Sim;
            var design = _twinDesign ?? throw new InvalidOperationException("run jw_twin save first");
            Cosmoteer.Game.ObjectID id;
            do id = Cosmoteer.Game.ObjectID.NewRandom(sim.Rand); while (!sim.IsObjectIDAvailable(id));
            var twin = Cosmoteer.Ships.Ship.Load(design, "Twin", ship.Metadata.PlayerIndex, spawnWithCrew: true, initializeToMax: true, randSeed: sim.Rand.UInt64(), id: id);
            twin.WorldCenter = Autotest.ClearSpotNear(sim, ship.DetWorldCenter, 400f, ship.WorldBoundingRadius);
            sim.Ships.Add(twin);
            Log.Info($"AUTOTEST ux: twin {twin.UniqueID} drives={twin.Ftl.HasFtlDrives}");
        };
        Autotest.Fields["ux_jump_standing"] = () =>
        {
            var game = Autotest.RequireRun().Game;
            return game.Sim.Ships.LocalPlayerSignificantShips.Count(s => JumpWarnings.Standing(game, s).Reason != StayReason.None);
        };
        Autotest.Fields["ux_jump_toobig"] = () =>
        {
            var game = Autotest.RequireRun().Game;
            return game.Sim.Ships.LocalPlayerSignificantShips.Count(s => JumpWarnings.Standing(game, s).Reason == StayReason.TooBig);
        };
        // The vanilla hover text (the game's own method) of the own ships that carry the stays-behind line.
        Autotest.Fields["ux_jump_hover"] = () =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            string mark = UxKit.Format("JumpHoverFmt", "");
            return sim.Ships.LocalPlayerSignificantShips.Count(s => sim.Mode.GetShipMouseOverText(s).Contains(mark, StringComparison.Ordinal));
        };
        Autotest.Fields["ux_jump_notices"] = () => JumpWarnings.NoticesShown;
        Autotest.Fields["ux_jump_dialogs"] = () => JumpWarnings.DialogsShown;
        Autotest.Fields["ux_jump_dialog_lines"] = () =>
        {
            var dialog = App.Director.States.OfType<Halfling.Gui.Dialogs.TwoButtonDialog>().LastOrDefault();
            return dialog is null ? -1 : dialog.MessageLabel.Text?.Split('\n').Count(l => l.StartsWith("  ", StringComparison.Ordinal)) ?? 0;
        };
        // jw_strand: screenshots only. Destroys every human ship here and records 4 waiting ships in the nearest other system, like a
        // fleet left behind (spec 1566). Not for game tests: the waiting ships do not exist.
        Autotest.Commands["jw_strand"] = _ =>
        {
            var run = Autotest.RequireRun();
            run.Mode._nodeData[NearestOtherNode(run)].NumFriendlyShips = 4;
            foreach (var ship in run.Game.Sim.Ships.HumanPlayerShips.ToList())
                foreach (var part in ship.Parts.ToList()) part.Health = 0;
        };
        Autotest.Fields["ux_stranded"] = () => JumpWarnings.Stranded(Autotest.RequireRun()) is null ? 0 : 1;
        Autotest.Fields["ux_recalls"] = () => JumpWarnings.RecallsSent;
        Autotest.Fields["ux_ships_here"] = () => Autotest.RequireRun().Game.Sim.Ships.HumanPlayerSignificantShips.Count();
        // jw_jump_split: single player. The playtest case (spec 1601): only the own ships without tech jump to the first unvisited
        // neighbor, like a captured ship that jumps alone; the tech carrier stays behind.
        Autotest.Commands["jw_jump_split"] = _ =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("jw_jump_split is single player only");
            var ships = run.Game.Sim.Ships.HumanPlayerSignificantShips.ToList();
            var jumpers = ships.Where(s => Tech.Vaults.ShipTech(s) == 0).ToList();
            if (jumpers.Count == 0 || jumpers.Count == ships.Count) throw new InvalidOperationException("jw_jump_split needs ships with and without tech");
            var next = run.Game.CurrentMapNode.Connections.FirstOrDefault(n => !run.State.VisitedNodes.Contains(n.UniqueID))
                ?? throw new InvalidOperationException("no unvisited neighbor system");
            Log.Info($"AUTOTEST ux: {jumpers.Count} of {ships.Count} ships jump to {next.Name}, the carrier stays");
            Autotest.WaitFor(() => !run.Game.IsGameSavingBlocked, 30, "game saving to be allowed", () =>
                run.Game.JumpToNode(next, doFtlAnimation: false, jumpers)
                    .ContinueWith(t => Log.Error("AUTOTEST jw_jump_split failed", t.Exception), TaskContinuationOptions.OnlyOnFaulted));
        };
        // jw_engage: the ENGAGE click of the jump window (FtlJumpGui.OnJumpClicked, through the jump dialog patch).
        Autotest.Commands["jw_engage"] = _ => Autotest.RequireRun().Game.Gui.FtlJumpGui.OnJumpClicked(null, EventArgs.Empty);
        // jw_answer yes|no: presses a button of the jump dialog, like the player.
        Autotest.Commands["jw_answer"] = arg =>
        {
            var dialog = App.Director.States.OfType<Halfling.Gui.Dialogs.TwoButtonDialog>().LastOrDefault() ?? throw new InvalidOperationException("no jump dialog");
            (arg == "yes" ? dialog.Option1Button : dialog.Option2Button).ClickController.InvokeClicked();
        };
    }
}

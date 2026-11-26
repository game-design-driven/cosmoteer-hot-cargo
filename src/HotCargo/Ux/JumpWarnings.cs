using System.Runtime.CompilerServices;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Creative.MPInputs;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Halfling;
using Halfling.Gui.Dialogs;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// V21, spec 1560 to 1569: which ships stay behind on a system jump, and why. Vanilla shows NEED FUEL for a ship that no fuel can
/// carry (its jump costs more hyperium than its drives hold), NO DRIVE for a ship without control, and a jump dialog that names no ship.
/// GUI only: it reads sim state and changes none.
/// </summary>
static class JumpWarnings
{
    static bool On => UxKit.On(s => s.JumpWarnings);

    /// <summary>The hyperium that the working drives of the ship can hold.</summary>
    static int Capacity(Ship ship) => ship.Ftl.Sum(d => d.MaxFuel);

    /// <summary>The vanilla path of a jump from the current system (GameRoot.DoHumanPlayersFtlJump), or null without a route.</summary>
    static List<MapNode>? PathTo(GameRoot game, MapNode dest)
    {
        if (game.Map?.Nodes is not { } nodes) return null;
        var path = new List<MapNode>();
        return float.IsPositiveInfinity(nodes.FindPathTo(dest, path, requireJumpable: true)) || path.Count < 2 ? null : path;
    }

    /// <summary>Spec 1560: the facts of the planned jump. The cost goes through FtlManager.GetTotalFtlMapJumpFuelCost.</summary>
    static JumpFacts Facts(GameRoot game, Ship ship)
    {
        var ftl = ship.Ftl;
        int cost = ftl.HasFtlDrives && game.DestinationMapNode.Confirmed is { } dest && PathTo(game, dest) is { } path ? ftl.GetTotalFtlMapJumpFuelCost(path) : 0;
        return new(ftl.HasFtlDrives, ship.Commands.CanExecuteCommands, cost, Capacity(ship), ftl.HasFtlDrives ? ftl.GetTotalFuel() : 0,
            ship.Commands.CurDisplayedCommand is FtlGateJumpCommand, ftl.IsReadyToJump);
    }

    internal static StayReason PlannedReason(GameRoot game, Ship ship) => JumpWarningsModel.Reason(Facts(game, ship));

    /// <summary>Spec 1561: the hyperium of the cheapest jump from this system to a connected system, or null when none is reachable.</summary>
    static int? CheapestCost(GameRoot game, Ship ship)
    {
        if (!ship.Ftl.HasFtlDrives || game.CurrentMapNode is not { } here) return null;
        int? best = null;
        foreach (var next in here.Connections)
            if (PathTo(game, next) is { } path && ship.Ftl.GetTotalFtlMapJumpFuelCost(path) is var cost && (best is null || cost < best))
                best = cost;
        return best;
    }

    sealed class Cached { public double Time = double.MinValue; public StayReason Reason; public int Cost; }
    static readonly ConditionalWeakTable<Ship, Cached> Cache = new();

    /// <summary>Spec 1561, 1562: the lasting reason before a jump is planned, recomputed at most once a second for each ship. GUI thread only.</summary>
    internal static (StayReason Reason, int Cost) Standing(GameRoot game, Ship ship)
    {
        var c = Cache.GetOrCreateValue(ship);
        double now = App.Clock.Time.Seconds;
        if (now - c.Time < 1) return (c.Reason, c.Cost);
        c.Time = now;
        int? cost = CheapestCost(game, ship);
        bool fleetHasDrive = game.Sim.Ships.HumanPlayerSignificantShips.Any(s => s.Ftl.HasFtlDrives);
        c.Reason = JumpWarningsModel.Standing(ship.Ftl.HasFtlDrives, ship.Commands.CanExecuteCommands, cost, Capacity(ship), fleetHasDrive);
        c.Cost = cost ?? 0;
        return (c.Reason, c.Cost);
    }

    /// <summary>Short reason: the jump dialog lines and the first line of the tooltips.</summary>
    static string Short(StayReason reason) => UxKit.Text("JumpWhy" + reason);

    /// <summary>The numbers and the fix. Empty for the reasons that vanilla already explains in the jump window (spin-up, the order).</summary>
    static string Fix(Ship ship, StayReason reason, int cost, bool planned) => reason switch
    {
        StayReason.NoDrive => UxKit.Text("JumpFixNoDrive"),
        StayReason.NoControl => UxKit.Format("JumpFixNoControlFmt", ship.Commands.CommandPointsUsed, ship.Commands.CommandPointsProvided),
        StayReason.TooBig => UxKit.Format(planned ? "JumpFixTooBigFmt" : "JumpFixTooBigHereFmt", cost, Capacity(ship)),
        StayReason.NeedFuel => UxKit.Format("JumpFixNeedFuelFmt", cost, ship.Ftl.GetTotalFuel()),
        _ => "",
    };

    /// <summary>Spec 1564: the tooltip of a ship in the jump window.</summary>
    internal static string WindowTip(GameRoot game, Ship ship)
    {
        if (!On || !ship.ExistsInSim(game.Sim)) return "";
        var f = Facts(game, ship);
        var reason = JumpWarningsModel.Reason(f);
        if (reason == StayReason.None) return "";
        string fix = Fix(ship, reason, f.Cost, planned: true);
        return $"<bad>{UxKit.Format("JumpStaysFmt", ship.Metadata.XmlShipName, Short(reason) + Carries(ship))}</bad>" + (fix == "" ? "" : "\n" + fix);
    }

    [HarmonyPatch(typeof(FtlJumpGui.ShipStatus), MethodType.Constructor, typeof(GameRoot), typeof(Ship))]
    static class WindowTipPatch
    {
        static void Postfix(FtlJumpGui.ShipStatus __instance, GameRoot game, Ship ship) => UxKit.Guard("jump window tip", () =>
        {
            var tip = new LiveText(() => WindowTip(game, ship));
            __instance.ToolTips.TextProvider = tip;
            // The ship icon is a button that takes the hover from the widget under it. Single(): a game change that moves the icon
            // fails loudly instead of leaving the icon without the tip.
            var slot = __instance.Components.OfType<Halfling.Gui.Components.Children.WidgetSingleChild<ShipIconButton>>().Single();
            (slot.Child ?? throw new InvalidOperationException("the jump window ship slot has no icon")).ToolTips.TextProvider = tip;
        });
    }

    /// <summary>Spec 1564: TOO BIG in place of NEED FUEL, NO CONTROL in place of NO DRIVE.</summary>
    [HarmonyPatch(typeof(FtlJumpGui.ShipStatus), "OnUpdateStatus")]
    static class WindowStatusPatch
    {
        static void Postfix(FtlJumpGui.ShipStatus __instance)
        {
            if (!On) return;
            UxKit.Guard("jump window status", () =>
            {
                var ship = __instance._ship;
                if (!ship.ExistsInSim(__instance._game.Sim)) return;
                var reason = PlannedReason(__instance._game, ship);
                if (reason is StayReason.TooBig or StayReason.NoControl) __instance._statusLbl.Text = UxKit.Text("JumpStatus" + reason);
            });
        }
    }

    /// <summary>The ships that the dialog lists: the local ships that vanilla checks before the dialog (FtlJumpGui.AreAllShipsReadyToJump).</summary>
    internal static List<(Ship Ship, StayReason Reason)> Staying(GameRoot game) =>
        game.Sim.Ships.LocalPlayerSignificantShips.Select(s => (s, PlannedReason(game, s))).Where(x => x.Item2 != StayReason.None).ToList();

    /// <summary>Spec 1602: " (carries 3 tech)" after a ship that stays with tech, else empty.</summary>
    static string Carries(Ship ship) => Tech.Vaults.ShipTech(ship) is var tech and > 0 ? " " + UxKit.Format("JumpCarriesFmt", tech) : "";

    /// <summary>Spec 2522: the ships that come along on ENGAGE, like GameRoot.DoHumanPlayersFtlJump: significant human ships that are ready.</summary>
    static bool JumpsOnEngage(Ship ship) => ship.IsSignificant && ship.IsHumanPlayerShip && ship.Ftl.IsReadyToJump;

    /// <summary>Spec 2522: the red line about the tech that a move leaves behind, or null when none stays.</summary>
    internal static string? TechLeftLine(Run run, Func<Ship, bool> jumps)
    {
        var (stays, goes) = Tech.LeftBehind.Split(run, run.Game.Sim, jumps);
        return JumpWarningsModel.LeftKind(stays, goes) switch
        {
            TechLeft.None => null,
            TechLeft.All => UxKit.Format("TechLeftAllFmt", stays),
            _ => UxKit.Format("TechLeftFmt", stays),
        };
    }

    /// <summary>
    /// Spec 1565: the warning lines of the ENGAGE dialog, top first. This is the one place for them: each line also makes the dialog
    /// ask when every local ship is ready. Spec 2522: the tech that stays and is lost.
    /// </summary>
    internal static List<string> JumpLines(GameRoot game)
    {
        var lines = new List<string>();
        if (Run.Current is { } run && run.Game == game && TechLeftLine(run, JumpsOnEngage) is { } techLeft) lines.Add(techLeft);
        // Spec 2567: on the run map, pay that waits here is lost with the jump.
        if (Run.Current is { } p && p.Game == game && Journey.Lockdown.Active(p) && Journey.LeftPayments.Here(p).Count is > 0 and var pays)
            lines.Add(UxKit.Format("PaymentsLostFmt", pays));
        // Spec 2566: a jump to a closed system of the run map asks first.
        if (Run.Current is { } r && r.Game == game && Journey.Lockdown.ConfirmLine(r, game.DestinationMapNode.Confirmed) is { } closed) lines.Add(closed);
        return lines;
    }

    /// <summary>Spec 1565: the warning lines, the local ships that stay with their reasons, and the question.</summary>
    internal static string DialogText(GameRoot game, IReadOnlyList<string> lines)
    {
        var staying = Staying(game);
        string ships = staying.Count == 0 ? "" : UxKit.Text("JumpDialogHead") + "\n" +
            string.Join("\n", staying.Select(x => UxKit.Format("JumpDialogLineFmt", x.Ship.Metadata.XmlShipName, Short(x.Reason)) + Carries(x.Ship))) + "\n";
        return string.Concat(lines.Select(l => l + "\n")) + ships + "\n" + UxKit.Text("JumpDialogAsk");
    }

    /// <summary>
    /// Spec 1565: the vanilla ENGAGE handler with the list in its dialog. The rest is the vanilla flow: no dialog when every local ship
    /// is ready and JumpLines is empty, and the jump goes ahead unless the player picks the second option.
    /// Spec 2566: the vanilla question of this dialog is the confirmation of a jump to a closed system.
    /// </summary>
    [HarmonyPatch(typeof(FtlJumpGui), "OnJumpClicked")]
    static class DialogPatch
    {
        static bool Prefix(FtlJumpGui __instance)
        {
            if (!On) return true;
            Engage(__instance);
            return false;
        }
    }

    internal static int DialogsShown;

    static async void Engage(FtlJumpGui gui)
    {
        // async void: an exception here would end the game, so it reports instead.
        try
        {
            var game = gui.Game;
            bool ready = gui.AreAllShipsReadyToJump(onlyWithCommands: false);
            var lines = JumpLines(game);
            if (!ready || lines.Count > 0)
            {
                DialogsShown++;
                if (await TwoButtonDialog.Show(DialogText(game, lines)) == TwoButtonDialog.DialogResponse.Option2) return;
            }
            game.NetManager.EnqueueInput(new MPReadyFtlJumpInput());
        }
        catch (Exception ex) { Core.Errors.Report("ux jump dialog", ex.Message, ex); }
    }

    /// <summary>Spec 1562: a red line in the hover text of an own or shared ship that stays behind on any jump from here.</summary>
    [HarmonyPatch(typeof(SimModeManager), nameof(SimModeManager.GetShipMouseOverText))]
    static class HoverText
    {
        static void Postfix(Ship ship, ref string __result)
        {
            if (!On || !ship.IsLocalPlayerShip || !ship.IsSignificant || ship.IsDisposed) return;
            if (App.Director.States.OfType<GameRoot>().FirstOrDefault() is not { } game || game.Sim != ship.Sim) return;
            string line = HoverLine(game, ship);
            if (line != "") __result += "\n" + line;
        }
    }

    internal static string HoverLine(GameRoot game, Ship ship)
    {
        var (reason, cost) = Standing(game, ship);
        if (reason == StayReason.None) return "";
        return $"<s16><bad>{UxKit.Format("JumpHoverFmt", Short(reason))}</bad></s16>\n<s14>{Fix(ship, reason, cost, planned: false)}</s14>";
    }

    static readonly JoinWatch<Ship> Joins = new();
    static double _nextPoll;
    internal static int NoticesShown;

    /// <summary>Spec 1563: one chat line and one banner when an own or shared ship that stays behind joins the fleet. Polled once a second.</summary>
    internal static void OnFrame(GameRoot game)
    {
        double now = App.Clock.Time.Seconds;
        if (now < _nextPoll || game.Sim is not { } sim) return;
        _nextPoll = now + 1;
        var joined = Joins.Joined(sim, sim.Ships.LocalPlayerSignificantShips);
        if (!On)
        {
            if (_button is { } b) b.Button.SelfActive = false;
            return;
        }
        StrandedNotice(game, sim);
        foreach (var ship in joined.OrderBy(s => s.UniqueID))
        {
            var (reason, cost) = Standing(game, ship);
            if (reason == StayReason.None) continue;
            NoticesShown++;
            string head = UxKit.Format("JumpStaysFmt", ship.Metadata.XmlShipName, Short(reason));
            Log.Info($"ux: jump notice {ship.Metadata.PlainShipName}: {reason}");
            UxKit.ChatLine(game, $"<bad>{head}</bad> {Fix(ship, reason, cost, planned: false)}");
            // The banner label shows plain text.
            ShipAlerts.Banner(game, UxKit.Format("JumpStaysFmt", ship.Metadata.PlainShipName, Short(reason)), WidgetRules.Instance.LimitedTextColor);
        }
    }

    /// <summary>Spec 1566: the system to go back to, or null. Reads the vanilla ship count of each system (CareerGameModeManager._nodeData).</summary>
    internal static (MapNode Node, int Ships)? Stranded(Run run)
    {
        var game = run.Game;
        if (game.Sim is not { } sim || game.CurrentMapNode is not { } here) return null;
        bool underWay = sim.Ships.IsDoingLocalJump || game.IsDoingNodeJump || game.ShipsInJumpLimbo.Count > 0;
        var waiting = run.Mode._nodeData.Where(kv => kv.Key != here).OrderBy(kv => kv.Key.Name, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value.NumFriendlyShips)).ToList();
        var node = JumpWarningsModel.RejoinTarget(sim.Ships.HumanPlayerSignificantShips.Count(), underWay, waiting);
        return node is null ? null : (node, waiting.First(w => w.Key == node).NumFriendlyShips);
    }

    /// <summary>Spec 1567, 1601: the heat panel line while no human ship is here (HeatPanel.ExtraLines), with the recall countdown.</summary>
    internal static string? StrandedLine(Run run)
    {
        if (!On || run.State.Result != RunResult.Running || Stranded(run) is not { } s) return null;
        string line = $"<bad>{StrandedText(s.Node, s.Ships)}</bad>";
        // Spec 2522: the recall leaves the tech of this system behind (derelicts, crates).
        if (Tech.LeftBehind.OnSwitch(run, run.Game.Sim) is > 0 and var here) line += "\n" + UxKit.Format("StrandedTechFmt", here);
        if (RecallLeft(run) is { } left)
            line += "\n" + UxKit.Format("RecallInFmt", Halfling.StringTools.EscapeForXml(s.Node.Name), Math.Max(0, (int)Math.Ceiling(left)));
        return line;
    }

    static string StrandedText(MapNode node, int ships) => UxKit.Format("StrandedFmt", Halfling.StringTools.EscapeForXml(node.Name), ships);

    /// <summary>The "SHIP LOST" banner of the last ship lasts about 5 s (ShipAlerts.Banner). The center text waits for it, so the two do not overlap.</summary>
    const double StrandedNoticeDelay = 6;

    static object? _strandedSim;
    static double? _strandedSince;
    static double? _lastRecall;
    static bool _strandedTold;
    internal static int StrandedNotices;
    internal static int RecallsSent;

    /// <summary>Spec 1601: seconds until the automatic recall on this machine, or null when it is off or the players are not stranded.</summary>
    static double? RecallLeft(Run run) =>
        _strandedSince is { } since && run.T.Ux.RecallSeconds > 0
            ? JumpWarningsModel.RecallLeft(App.Clock.Time.Seconds, since, _lastRecall, run.T.Ux.RecallSeconds)
            : null;

    /// <summary>
    /// Spec 1567, 1601: one center text and one chat line once the players are stranded for a few seconds, the recall button, and the
    /// automatic recall. A new sim starts quiet. Only single player and the co-op host send the automatic recall, so one vote starts.
    /// </summary>
    static void StrandedNotice(GameRoot game, Cosmoteer.Simulation.SimRoot sim)
    {
        if (Run.Current is not { } run || run.Game != game) return;
        if (!ReferenceEquals(sim, _strandedSim)) { _strandedSim = sim; _strandedSince = null; _lastRecall = null; _strandedTold = false; }
        var s = run.State.Result == RunResult.Running ? Stranded(run) : null;
        RecallButton(game, s?.Node);
        if (s is null) { _strandedSince = null; _lastRecall = null; _strandedTold = false; return; }
        double now = App.Clock.Time.Seconds;
        _strandedSince ??= now;
        if (!_strandedTold && now - _strandedSince >= StrandedNoticeDelay)
        {
            _strandedTold = true;
            StrandedNotices++;
            string name = Halfling.StringTools.EscapeForXml(s.Value.Node.Name);
            Log.Info($"ux: stranded, fleet waits in {s.Value.Node.Name} ({s.Value.Ships} ships)");
            UxKit.CenterText(game, $"<bad>{UxKit.Format("StrandedCenterFmt", name)}</bad>");
            UxKit.ChatLine(game, $"<bad>{StrandedText(s.Value.Node, s.Value.Ships)}</bad>");
        }
        if (RecallLeft(run) is not <= 0 || (run.IsMultiplayer && !game.NetManager.IsHost)) return;
        _lastRecall = now;
        Recall(game, s.Value.Node, "automatic");
    }

    /// <summary>The vanilla system switch (CareerGameModeManager.InputMap): no fuel, no heat cut; in co-op a unanimous vote.</summary>
    static void Recall(GameRoot game, MapNode node, string why)
    {
        RecallsSent++;
        Log.Info($"ux: recall to {node.Name} ({why})");
        game.NetManager.EnqueueInput(new Cosmoteer.Game.Multiplayer.MPJumpToNodeInput(node));
    }

    internal static int SwitchDialogsShown;

    /// <summary>
    /// Spec 2522: a switch without a jump (the map click, the recall button) asks first when tech stays in this system, because it is lost.
    /// Without such tech it goes at once, like vanilla.
    /// </summary>
    static async void ConfirmSwitch(GameRoot game, MapNode node, string why)
    {
        // async void: an exception here would end the game, so it reports instead.
        try
        {
            if (Run.Current is { } run && run.Game == game && TechLeftLine(run, _ => false) is { } techLeft)
            {
                SwitchDialogsShown++;
                string ask = UxKit.Format("SwitchAskFmt", Halfling.StringTools.EscapeForXml(node.Name));
                if (await TwoButtonDialog.Show(techLeft + "\n\n" + ask) == TwoButtonDialog.DialogResponse.Option2) return;
            }
            Recall(game, node, why);
        }
        catch (Exception ex) { Core.Errors.Report("ux switch dialog", ex.Message, ex); }
    }

    internal static void ConfirmSwitchForTest(GameRoot game, MapNode node) => ConfirmSwitch(game, node, "test");

    /// <summary>
    /// Spec 2522: the vanilla map click on a system with own ships (CareerGameModeManager.InputMap) goes through ConfirmSwitch.
    /// The condition is the vanilla one; any other click goes to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(Cosmoteer.Modes.Career.CareerGameModeManager), nameof(Cosmoteer.Modes.Career.CareerGameModeManager.InputMap))]
    static class MapSwitchPatch
    {
        static bool Prefix(Cosmoteer.Modes.Career.CareerGameModeManager __instance)
        {
            var game = __instance.Game;
            if (!On || game.Gui.Reacted || game.Gui.GetSelectionState() != null || game.IsDoingNodeJump || Cosmoteer.GameApp.IsDemo
                || !Cosmoteer.Input.Inputs.General.UIClick.WasPressed) return true;
            var node = __instance.Map.Nodes.GetNodeUnderCursor();
            if (node is null || node == game.CurrentMapNode || __instance.GetMapNodeData(node).NumFriendlyShips <= 0) return true;
            ConfirmSwitch(game, node, "map");
            return false;
        }
    }

    static (GameGui Gui, Halfling.Gui.Button Button)? _button;
    static MapNode? _buttonNode;

    /// <summary>Spec 1601: a "Go to <system>" button under the area name text while the players are stranded.</summary>
    static void RecallButton(GameRoot game, MapNode? node)
    {
        if (_button is not { } b || b.Gui != game.Gui)
        {
            var button = new Halfling.Gui.Button { AnchorRect = Halfling.Gui.AnchorPresets.TopCenter, X = -130f, Y = 300f, Width = 260f, Height = 44f };
            button.Clicked += (_, _) => { if (_buttonNode is { } n) ConfirmSwitch(game, n, "button"); };
            game.Gui.RootWidget.AddChild(button);
            _button = b = (game.Gui, button);
        }
        _buttonNode = node;
        b.Button.SelfActive = node is not null && !game.IsDoingNodeJump;
        if (node is not null) b.Button.Text = UxKit.Format("RecallButtonFmt", node.Name);
    }
}

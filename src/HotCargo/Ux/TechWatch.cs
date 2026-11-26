using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Input;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Input;
using Halfling.Gui.Components.Graphics;
using Halfling.Gui.Components.Toggles;
using HotCargo.Core;
using HotCargo.Missions;
using HotCargo.Tech;
using static Halfling.StringTools;

namespace HotCargo.Ux;

/// <summary>One line of the tech watch: where the tech is, how much, the text and where a click goes.</summary>
internal readonly record struct WatchLine(WatchKind Kind, int Units, string Text, FocusTarget Target);

/// <summary>
/// Spec 2120 to 2129: tech of the players that is not in a vault of a player ship. A box under the heat panel lists each place
/// with a line that clicks to it (MissionFocus, spec 2292), a banner and a sound say when tech starts to be at risk, and a banner
/// says at once when a new design has too little vault room. GUI only: it reads synced sim state, so every co-op player sees the
/// same lines, and it changes no sim state.
/// </summary>
internal static class TechWatch
{
    static bool On => UxKit.On(s => s.TechWatch);

    /// <summary>
    /// Spec 2121: every place, most urgent first. Tech that the players gave away (a deal, a surrender, the Buyer, a fence, a
    /// hand-over) left the game through Vaults.Remove or FleetHold.Take, so it has no place here.
    /// </summary>
    internal static List<WatchLine> Lines(Run run)
    {
        var game = run.Game;
        var sim = game.Sim;
        var here = sim.NodeId();
        var lines = new List<WatchLine>();
        var ux = run.T.Ux;

        foreach (var ship in Vaults.HumanShips(sim).OrderBy(s => s.UniqueID))
        {
            int tech = Vaults.ShipTech(ship);
            if (tech == 0) continue;
            var target = new FocusTarget(here, ship, null, ship.DetWorldCenter);
            if (DesignShort(ship, tech) is > 0 and var shortBy)
                lines.Add(new(WatchKind.Design, shortBy, UxKit.Format("WatchDesignFmt", ship.Metadata.XmlShipName, shortBy), target));
            if (Vaults.StorageTech(ship) is > 0 and var stored && run.T.Tech.DecaySeconds > 0)
            {
                double left = Vaults.StorageTiles(ship).Where(t => t.ResourceType == Vaults.TechId && t.Resources > 0).Min(t => TechDecay.SecondsLeft(run, t));
                lines.Add(new(WatchKind.Storage, stored, UxKit.Format("WatchStorageFmt", stored, ship.Metadata.XmlShipName, LossTimer.FormatTime(left)), target));
            }
        }
        foreach (var ship in sim.Ships.AllShips.Where(s => s.IsJunk).OrderBy(s => s.UniqueID))
            if (Vaults.ShipTech(ship) is > 0 and var units)
                lines.Add(new(WatchKind.Derelict, units, UxKit.Format("WatchDerelictFmt", units, ship.Metadata.XmlShipName), new(here, ship, null, ship.DetWorldCenter)));

        var crates = new List<CratePoint>();
        for (int i = 0; i < sim.Nuggets.Count; i++)
            if (sim.Nuggets[i] is { } n && n.ResourceType == Vaults.TechId)
                crates.Add(new((uint)n.UniqueID, n.WorldLocation.X, n.WorldLocation.Y, n.Quantity));
        foreach (var g in TechWatchModel.Groups(crates, ux.TechWatchCrateGroupRadius))
            lines.Add(new(WatchKind.Crates, g.Units, UxKit.Format("WatchCratesFmt", g.Units), new(here, null, null, new Vector2(g.X, g.Y))));

        // Spec 2520: a system switch loses the tech of the old system, so every holder and every waiting ship is in this system.
        foreach (var h in run.State.Tech.HunterHeld.Where(h => h.Units > 0))
            lines.Add(Held(sim, WatchKind.Hunter, "WatchHunterFmt", h.Units, h.ShipName, h.Ship, h.Location));
        foreach (var h in run.State.Diplomacy.Held.Where(h => h.Units > 0))
            lines.Add(Held(sim, WatchKind.Friend, "WatchFriendFmt", h.Units, h.ShipName, h.Ship, h.Location));
        foreach (var w in run.State.Tech.Waiting.Where(w => w.Units > 0))
        {
            var ship = sim.Ships[w.Ship];
            lines.Add(new(WatchKind.Waiting, w.Units, UxKit.Format("WatchWaitingFmt", w.Units, EscapeForXml(w.ShipName)), new(here, ship, null, ship?.DetWorldCenter)));
        }
        return lines.OrderBy(l => l.Kind).ThenByDescending(l => l.Units).ToList();
    }

    /// <summary>Spec 2121: a ship that holds the players' tech. A holder in stasis or gone shows its last place.</summary>
    static WatchLine Held(SimRoot sim, WatchKind kind, string key, int units, string name, ObjectID shipId, Vector2 lastSeen)
    {
        var ship = sim.Ships[shipId];
        return new(kind, units, UxKit.Format(key, units, EscapeForXml(name)), new(sim.NodeId(), ship, null, ship?.DetWorldCenter ?? lastSeen));
    }

    /// <summary>Spec 2124: tech of the ship that its design has no vault room for, while the design removes a built vault.</summary>
    static int DesignShort(Ship ship, int tech)
    {
        var built = Vaults.LiveVaults(ship).ToList();
        if (built.Count == 0) return 0;
        int blueprints = ship.BlueprintParts.Count(p => p.Rules.ID == Vaults.VaultId);
        return TechWatchModel.DesignShort(tech, built.Count, blueprints, built[0].MaxResources);
    }

    /// <summary>
    /// Fields for each kind (from the enum): watch_&lt;kind&gt; (units) and watch_focus_&lt;kind&gt; (the FocusStep of its first line, -1 without a line).
    /// Command watch_click &lt;kind&gt;: the click on the first line of that kind.
    /// </summary>
    internal static void RegisterAutotest()
    {
        static Run R() => Dev.Autotest.RequireRun();
        static WatchLine First(WatchKind k) => Lines(R()).FirstOrDefault(l => l.Kind == k);
        foreach (var kind in Enum.GetValues<WatchKind>())
        {
            string name = kind.ToString().ToLowerInvariant();
            Dev.Autotest.Fields["watch_" + name] = () => Lines(R()).Where(l => l.Kind == kind).Sum(l => l.Units);
            Dev.Autotest.Fields["watch_focus_" + name] = () => Lines(R()).Any(l => l.Kind == kind) ? (int)MissionFocus.Step(R().Game, First(kind).Target) : -1;
        }
        Dev.Autotest.Fields["watch_lines"] = () => Lines(R()).Count;
        Dev.Autotest.Fields["watch_alarms"] = () => Alarms;
        Dev.Autotest.Fields["watch_design_alarms"] = () => DesignAlarms;
        Dev.Autotest.Commands["watch_click"] = arg =>
        {
            var kind = Enum.Parse<WatchKind>(arg.Trim(), ignoreCase: true);
            var line = Lines(R()).FirstOrDefault(l => l.Kind == kind);
            if (line.Text is null) throw new InvalidOperationException($"no tech watch line of kind {kind}");
            MissionFocus.Focus(R().Game, line.Target);
            Log.Info($"AUTOTEST watch click {kind}: {line.Text}");
        };
    }

    static readonly AlarmWatch Alarm = new();
    static HashSet<ObjectID> _shortDesigns = [];
    static double _nextPoll;
    /// <summary>Banners shown since start: risk and design. For autotests.</summary>
    internal static int Alarms, DesignAlarms;

    /// <summary>Spec 2123, 2124: the banners, polled twice a second. Each machine shows them for every player's tech.</summary>
    internal static void OnFrame(Run run)
    {
        Mount(run);
        double now = App.Clock.Time.Seconds;
        if (now < _nextPoll) return;
        _nextPoll = now + 0.5;
        if (!On || run.State.Result != RunResult.Running || run.Game.Sim is null)
        {
            Alarm.Update(now, false, 0);
            _shortDesigns = [];
            return;
        }
        var lines = Lines(run);
        var game = run.Game;

        var designs = lines.Where(l => l.Kind == WatchKind.Design).ToList();
        foreach (var l in designs.Where(l => !_shortDesigns.Contains(l.Target.Live!.UniqueID)))
        {
            DesignAlarms++;
            var ship = l.Target.Live!;
            Log.Info($"ux: tech watch design {ship.Metadata.PlainShipName}: no vault room for {l.Units} tech");
            Warn(game, UxKit.Format("WatchDesignBannerFmt", ship.Metadata.PlainShipName, l.Units), UxKit.Format("WatchDesignChatFmt", ship.Metadata.XmlShipName, l.Units));
        }
        _shortDesigns = designs.Select(l => l.Target.Live!.UniqueID).ToHashSet();

        int atRisk = lines.Where(l => TechWatchModel.Alarms(l.Kind)).Sum(l => l.Units);
        if (!Alarm.Update(now, atRisk > 0, run.T.Ux.TechWatchAlarmSeconds)) return;
        Alarms++;
        Log.Info($"ux: tech watch alarm, {atRisk} tech at risk: {string.Join("; ", lines.Select(l => $"{l.Kind} {l.Units}"))}");
        // The design banner already said it.
        if (designs.Count > 0) return;
        Warn(game, UxKit.Format("WatchBannerFmt", atRisk), UxKit.Format("WatchChatFmt", atRisk, lines.First(l => TechWatchModel.Alarms(l.Kind)).Text));
    }

    /// <summary>Spec 2122: puts the box under the heat panel once per game GUI (the top of the left column without the panel).</summary>
    static void Mount(Run run)
    {
        var column = run.Game.Gui.TopLeftHudBox.Children;
        if (column.OfType<Box>().Any()) return;
        int under = column.OfType<Ui.HeatPanel>().Select(column.IndexOf).DefaultIfEmpty(-1).First();
        column.Insert(under + 1, new Box(run.Mode));
    }

    static void Warn(GameRoot game, string banner, string chat)
    {
        game.Gui.PlayErrorSound();
        ShipAlerts.Banner(game, banner, WidgetRules.Instance.BadTextColor);
        UxKit.ChatLine(game, chat);
    }

    /// <summary>Spec 2122: the box in the left HUD column, under the heat panel. One header line, then one line per place.</summary>
    internal sealed class Box : LayoutBox
    {
        readonly CareerGameModeManager _mode;
        readonly Label _head;
        readonly List<Label> _lines = [];
        double _nextText;

        /// <summary>The lines as the box last showed them. For autotests.</summary>
        internal static List<WatchLine> Shown = [];

        public Box(CareerGameModeManager mode)
        {
            _mode = mode;
            Width = 330f;
            AutoSize.AutoHeightMode = AutoSizeMode.Enable;
            InputBlockMode = InputBlockMode.Auto;
            Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            Children.BorderPadding = new Borders(12f, 6f, 12f, 8f);
            WidgetRules.AddBlur(this);
            _head = new Label { InputBlockMode = InputBlockMode.Never };
            Style(_head, 16);
            AddChild(_head);
            BeforeFrameDraw += (_, _) => UxKit.Guard("tech watch box", Refresh);
        }

        static void Style(Label l, int size)
        {
            l.TextRenderer.Font = WidgetRules.Instance.ShadowFont;
            l.TextRenderer.FontSize = size;
            l.TextRenderer.XmlFormatting = true;
            l.TextRenderer.HAlignment = HAlignment.Left;
            l.TextRenderer.VAlignment = VAlignment.Center;
            l.TextRenderer.OversizeMode = OversizeMode.Wrap;
            l.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        }

        void Refresh()
        {
            var run = Run.Current is { } r && r.Mode == _mode ? r : null;
            bool show = run is not null && On && Ui.Overlay.Visible && run.State.Result == RunResult.Running && _mode.Game.MapDisplayFactor < 1f;
            if (!show) { SelfActive = false; return; }
            if (!UxKit.HudDue(ref _nextText)) return;
            var lines = Shown = Lines(run!);
            SelfActive = lines.Count > 0;
            if (lines.Count == 0) return;
            // Tech of a design line is still in its vault: the header counts only tech that is outside now.
            int outside = lines.Where(l => l.Kind != WatchKind.Design).Sum(l => l.Units);
            _head.Text = outside > 0 ? UxKit.Format("WatchHeadFmt", outside) : UxKit.Text("WatchHeadDesign");
            for (int i = _lines.Count; i < lines.Count; i++) _lines.Add(NewLine());
            for (int i = 0; i < _lines.Count; i++)
            {
                _lines[i].SelfActive = i < lines.Count;
                if (i >= lines.Count) continue;
                _lines[i].Text = lines[i].Text;
                _lines[i].UserData = lines[i].Target;
            }
        }

        /// <summary>A clickable line in the style of the vanilla objective lines (MissionsHud.MissionWidget).</summary>
        Label NewLine()
        {
            var lbl = new Label { InputBlockMode = InputBlockMode.Always };
            Style(lbl, 14);
            lbl.TextRenderer.Padding = new Borders(5f, 2f, 5f, 2f);
            var input = new WidgetInputStateController();
            lbl.Components.Add(input);
            var highlighted = new WidgetNineSlice(lbl);
            highlighted.CopySettingsFrom(ListItem.Default.StateHighlightedNineSlice);
            lbl.ComponentToggles[highlighted] = new InputStateToggle(input, InputState.Highlighted);
            lbl.Components.Insert(0, highlighted);
            var pressed = new WidgetNineSlice(lbl);
            pressed.CopySettingsFrom(ListItem.Default.StatePressedNineSlice);
            lbl.ComponentToggles[pressed] = new InputStateToggle(input, InputState.Pressed);
            lbl.Components.Insert(1, pressed);
            var game = _mode.Game;
            lbl.CustomShapeFunc = (w, loc) => w.ScreenRect.Contains(in loc) && w.UserData is FocusTarget t && MissionFocus.CanFocus(game, t);
            lbl.ConfigureAltClick(Inputs.General.UIEitherClick, (_, _) => UxKit.Guard("tech watch click", () =>
            {
                if (lbl.UserData is FocusTarget t) MissionFocus.Focus(game, t);
            }), game.Sim.Rules.SimGui.FocusModeCursor);
            AddChild(lbl);
            return lbl;
        }
    }
}

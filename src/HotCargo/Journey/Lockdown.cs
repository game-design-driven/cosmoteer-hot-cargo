using Cosmoteer.GalaxyMap;
using Cosmoteer.Localization;
using Cosmoteer.Simulation;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Ux;

namespace HotCargo.Journey;

/// <summary>
/// Spec 2557 to 2563: the corporate lockdown line on the run map. Systems behind the players' furthest band are closed; of the choices of
/// the players' system, more close as heat rises (LineModel). A closed system stays reachable, but a jump there cuts no heat and hunters
/// wait there. Sim logic: the tick, the system switch and the jump read sim state only, so every co-op machine agrees.
/// </summary>
public static class Lockdown
{
    /// <summary>Set when a switch to a closed system starts (Factions.Jumps), read at the arrival. Not saved: a save never holds a jump.</summary>
    static Run? _closedArrival;

    /// <summary>Tests: a system that counts as closed, for a jump test when the random map gave the start no closable choice.</summary>
    internal static MapNode? TestClosed;

    internal static void Register()
    {
        Run.Hooks.Tick += OnTick;
        Run.Hooks.SystemEntered += OnSystemEntered;
        AddAutotest();
    }

    static void AddAutotest()
    {
        static Run R() => Dev.Autotest.RequireRun();
        static List<MapNode> Here() => Choices(R(), R().Game.CurrentMapNode);
        Dev.Autotest.Fields["line_choices"] = () => Here().Count;
        Dev.Autotest.Fields["line_closed"] = () => Here().Count(n => Closed(R(), n));
        // Closed choices minus the closable count at this heat (all but 2 at heat 100). Expect 0 at heat 100 after the grace.
        Dev.Autotest.Fields["line_closed_gap"] = () =>
            Here().Count(n => Closed(R(), n)) - Here().Count(n => Threshold(R(), n) is { } l && R().State.Heat >= l);
        Dev.Autotest.Fields["line_warned"] = () => R().State.Journey.Choices.Values.Count(c => c.Warned >= 2);
        Dev.Autotest.Fields["line_wave_due"] = () => R().State.Journey.ClosedWaveAt >= 0 ? 1 : 0;
        // line_mapfit: the galaxy map camera shows the whole run map (screenshots). line_tips: logs the lockdown tooltip of each system.
        Dev.Autotest.Commands["line_mapfit"] = _ =>
        {
            var map = R().Game.Map;
            var xs = map.Nodes.Select(n => n.Location.X).ToList();
            var ys = map.Nodes.Select(n => n.Location.Y).ToList();
            var vc = map.ViewController;
            vc.ClearFocus(clearFramesOfReferenceAndRotation: true);
            vc.FRLocation = new Halfling.Geometry.Vector2((xs.Min() + xs.Max()) / 2f, (ys.Min() + ys.Max()) / 2f);
            float size = Math.Max(xs.Max() - xs.Min(), ys.Max() - ys.Min()) + 2f * RunMapModel.RowGap;
            vc.ZoomToFit(new Halfling.Geometry.Vector2(size, size), instant: true);
        };
        Dev.Autotest.Commands["line_tips"] = _ =>
        {
            foreach (var n in R().Game.Map.Nodes.OrderBy(RunMap.Band).ThenBy(n => n.Location.Y))
                Log.Info($"AUTOTEST LINE band={RunMap.Band(n)} {n.Name}: closed={Closed(R(), n)}{LockdownMap.Lines(R(), n).Replace('\n', ' ')}");
        };
        // line_relaycam <size>: single player. Moves the own ship next to the relay to the first closed choice and puts the sim camera
        // on that relay (screenshots).
        Dev.Autotest.Commands["line_relaycam"] = arg =>
        {
            var run = R();
            var dest = Here().FirstOrDefault(n => Closed(run, n)) ?? throw new InvalidOperationException("no closed choice");
            var gate = Cosmoteer.Ships.Special.FtlGate.GetGateForDestination(run.Game.Sim, dest)
                ?? throw new InvalidOperationException($"no relay to {dest.Name}");
            var at = gate.DetWorldCenter;
            Log.Info($"AUTOTEST LINE relay to {dest.Name} at {at}");
            var ship = Dev.Autotest.ActingShip(run.Game.Sim) ?? throw new InvalidOperationException("no own ship");
            Dev.Autotest.Teleport(ship, Dev.Autotest.ClearSpotNear(run.Game.Sim, at, 900f, ship.DetWorldBoundingCircle.Radius));
            Dev.Autotest.Commands["camera"](string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{at.X} {at.Y} {arg.Trim()}"));
        };
        // line_confirm: sets the jump destination to a closed choice (the vanilla input) and logs the ENGAGE dialog text.
        Dev.Autotest.Commands["line_confirm"] = _ =>
        {
            var run = R();
            var dest = Here().FirstOrDefault(n => Closed(run, n)) ?? throw new InvalidOperationException("no closed choice");
            run.Game.NetManager.EnqueueInput(new Cosmoteer.Game.Multiplayer.MPInputs.MPSetDestinationMapNodeInput(dest, playLocalSound: false));
            Dev.Autotest.WaitFor(() => run.Game.DestinationMapNode.Confirmed == dest, 10, "the jump destination", () =>
                Log.Info($"AUTOTEST LINE confirm: {Ux.JumpWarnings.DialogText(run.Game, Ux.JumpWarnings.JumpLines(run.Game)).Replace('\n', ' ')}"));
        };
        Dev.Autotest.Fields["line_confirm_lines"] = () =>
            Ux.JumpWarnings.JumpLines(R().Game).Count(l => l == ConfirmLine(R(), R().Game.DestinationMapNode.Confirmed));
        // line_jump closed|open: single player. A relay jump (spec 650) to a closed or an open choice, without the animation. "closed"
        // takes a closed choice, or else marks the first choice closed for the test.
        Dev.Autotest.Commands["line_jump"] = arg =>
        {
            var run = R();
            var choices = Here();
            bool closed = arg.Trim() == "closed";
            var next = choices.FirstOrDefault(n => Closed(run, n) == closed && n != RunMap.BuyerNode(run));
            if (next is null && closed) next = TestClosed = choices.FirstOrDefault() ?? throw new InvalidOperationException("no choice");
            if (next is null) throw new InvalidOperationException($"no {arg} choice");
            Factions.Jumps.TestOverride = Factions.JumpKind.RelayClear;
            Dev.Autotest.JumpWhenAllowed(run, next, "line_jump");
        };
    }

    internal static bool Active(Run run) => run.PressureActive && run.T.Journey.LineEnabled && RunMap.IsRunMap(run.Mode);

    static int FurthestBand(Run run) => RunMap.Step(run) - 1;

    /// <summary>
    /// Spec 2558: the choices of a system (its neighbors that are not behind), in close order: side links first, then a seeded order, the
    /// Buyer last (so it never closes).
    /// </summary>
    internal static List<MapNode> Choices(Run run, MapNode from)
    {
        int furthest = FurthestBand(run);
        var buyer = RunMap.BuyerNode(run);
        int band = RunMap.Band(from);
        return from.Connections.Where(n => !LineModel.Behind(RunMap.Band(n), furthest))
            .OrderBy(n => n == buyer ? 1 : 0)
            .ThenBy(n => RunMap.Band(n) == band ? 0 : 1)
            .ThenBy(n => CloseRoll(run, from, n))
            .ThenBy(n => n.UniqueID)
            .ToList();
    }

    static uint CloseRoll(Run run, MapNode from, MapNode to) =>
        new FastRand((ulong)run.Mode.Init.RandSeed ^ 0x4C4F_434B_444FUL ^ (uint)from.UniqueID.GetHashCode(), (ulong)(uint)to.UniqueID.GetHashCode()).UInt32(); // determinism: ObjectID.GetHashCode is the object ID

    /// <summary>Spec 2558: the heat at which this choice of the players' system closes, or null when it never closes.</summary>
    internal static float? Threshold(Run run, MapNode node)
    {
        var choices = Choices(run, run.Game.CurrentMapNode);
        int rank = choices.IndexOf(node);
        return rank < 0 ? null : LineModel.Threshold(rank, choices.Count);
    }

    /// <summary>Spec 2557, 2558: the system is closed now, seen from the players' system.</summary>
    public static bool Closed(Run run, MapNode node)
    {
        if (!Active(run) || node == RunMap.BuyerNode(run)) return false;
        if (node == TestClosed) return true;
        if (LineModel.Behind(RunMap.Band(node), FurthestBand(run))) return true;
        return run.State.Journey.Choices.TryGetValue(node.UniqueID, out var c) && c.Closed;
    }

    /// <summary>The lockdown as the UI shows it: the closed systems and the closing heat of each choice, at one sim tick.</summary>
    internal sealed record View(Run Run, double At, MapNode Current, HashSet<MapNode> ClosedNodes, Dictionary<MapNode, float> Thresholds);

    static View? _view;

    /// <summary>
    /// Spec 872, 2562: the map texts, the tooltips and the relay marks read the lockdown for every node in every frame. They read this
    /// snapshot, built at most once per sim tick (the game time and the players' system key it). UI only: sim code calls Closed.
    /// </summary>
    internal static View UiView(Run run)
    {
        if (_view is { } v && v.Run == run && v.At == run.State.GameSeconds && v.Current == run.Game.CurrentMapNode) return v;
        var current = run.Game.CurrentMapNode;
        var choices = Choices(run, current);
        var thresholds = new Dictionary<MapNode, float>();
        for (int i = 0; i < choices.Count; i++)
            if (LineModel.Threshold(i, choices.Count) is { } level) thresholds[choices[i]] = level;
        return _view = new View(run, run.State.GameSeconds, current, run.Game.Map.Nodes.Where(n => Closed(run, n)).ToHashSet(), thresholds);
    }

    /// <summary>Heat per game second from time now: the projection of the warnings (spec 2561). 0 while gains are blocked.</summary>
    internal static float HeatPerSecond(Run run) => Run.Hooks.HeatGainsBlocked(run) ? 0f : run.TimeHeatPerMinute(run.State.FleetTech) / 60f;

    /// <summary>Spec 2566: the jump dialog line for a jump to a closed system, with what it costs. Null when the destination is open.</summary>
    internal static string? ConfirmLine(Run run, MapNode? destination)
    {
        if (destination is null || !Closed(run, destination)) return null;
        float stored = run.State.NodeHeat.TryGetValue(destination.UniqueID, out float h) ? h : 0f;
        return Format("ConfirmClosedFmt", destination.Name, HeatModel.OnEnterSystem(run.State.Heat, stored, 1f));
    }

    /// <summary>Spec 2559: a switch to a closed system starts (a relay jump or the vanilla switch to own ships). Called from the switch on every machine.</summary>
    internal static void OnSwitch(Run run, MapNode destination) => _closedArrival = Closed(run, destination) ? run : null;

    /// <summary>Spec 2559: the arrival is a jump to a closed system: it keeps all heat.</summary>
    public static bool ArrivalClosed(Run run) => _closedArrival == run;

    static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = run.State.Journey;
        s.Choices.Clear();
        bool closed = ArrivalClosed(run);
        _closedArrival = null;
        TestClosed = null;
        if (!closed || !Active(run)) return;
        // Spec 2560: hunters wait in a closed system. They attack after a short delay, when the fleet is in the system.
        s.ClosedWaveAt = run.State.GameSeconds + run.T.Journey.ClosedWaveDelaySeconds;
        Log.Info($"lockdown: jump into closed system {run.Game.CurrentMapNode.Name}, hunters at {s.ClosedWaveAt:0}s");
        string line = Format("ArrivalClosedFmt", run.Game.CurrentMapNode.Name);
        Run.OnNextFrame(() => { UxKit.ChatLine(run.Game, line); UxKit.CenterText(run.Game, Text("ArrivalClosedCenter")); });
    }

    static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("lockdown line", () =>
    {
        if (!Active(run)) return;
        var s = run.State.Journey;
        var t = run.T.Journey;
        if (s.ClosedWaveAt >= 0 && run.State.GameSeconds >= s.ClosedWaveAt)
        {
            s.ClosedWaveAt = -1;
            Hunters.HunterSpawner.StartStage(run, sim, t.ClosedWaveStage);
        }
        var choices = Choices(run, run.Game.CurrentMapNode);
        float rate = HeatPerSecond(run);
        for (int i = 0; i < choices.Count; i++)
        {
            var node = choices[i];
            var before = s.Choices.TryGetValue(node.UniqueID, out var c) ? c.Value : ChoiceState.Fresh;
            float? level = LineModel.Threshold(i, choices.Count);
            var (after, notice) = LineModel.Tick(before, level, run.State.Heat, rate, run.State.GameSeconds,
                t.LineFirstWarningSeconds, t.LineLastWarningSeconds);
            if (after != before) s.Choices[node.UniqueID] = ChoiceRecord.From(after);
            if (notice != LineNotice.None) Notify(run, node, notice, level, rate);
        }
    });

    /// <summary>Spec 2561: a chat line for each notice, and a center text when a choice closes. GUI on the next frame.</summary>
    static void Notify(Run run, MapNode node, LineNotice notice, float? level, float rate)
    {
        double? seconds = level is { } l ? LineModel.SecondsTo(l, run.State.Heat, rate) : null;
        var grace = run.State.Journey.Choices.TryGetValue(node.UniqueID, out var c) ? c.GraceUntil - run.State.GameSeconds : 0;
        double left = Math.Max(0, Math.Max(seconds ?? 0, grace));
        string clock = $"{(int)left / 60}:{(int)left % 60:00}";
        string? line = notice switch
        {
            LineNotice.FirstWarning or LineNotice.LastWarning => Format("WarnFmt", node.Name, clock, level ?? 0f),
            LineNotice.Closed => Format("ClosedFmt", node.Name),
            LineNotice.Reopened => Format("ReopenedFmt", node.Name),
            _ => null,
        };
        if (line is null) return;
        Log.Info($"lockdown: {notice} {node.Name} (heat {run.State.Heat:0.0}, closes at {level:0.0})");
        Run.OnNextFrame(() =>
        {
            UxKit.ChatLine(run.Game, line);
            if (notice == LineNotice.Closed) UxKit.CenterText(run.Game, Format("ClosedCenterFmt", node.Name));
        });
    }

    internal static string Text(string key) => Strings.GetText("HotCargoJourney/" + key);

    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoJourney/" + key, args);
}

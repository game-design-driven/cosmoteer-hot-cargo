using System.Globalization;
using Cosmoteer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using Halfling;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;
using Halfling.Gui.Dialogs;

namespace HotCargo.Telemetry;

/// <summary>
/// Entry point of the telemetry area (TELEMETRY.md). It records each game session to a JSONL file in the mod's telemetry folder.
/// It hooks in from outside: Run.Hooks, game EventHub events and read-only Harmony patches in this folder.
/// </summary>
public static class TelemetryArea
{
    static bool _frameHooked, _traceInstalled;
    static Halfling.Gui.TextEditField? _textBox;

    /// <summary>A name for the sessions of this process, for example the autotest script. In the session.start header.</summary>
    internal static string Label = "";

    public static void Register()
    {
        Run.Hooks.NewRunStarted += _ => Session.NewRunPending = true;
        Run.Hooks.StageStarted += Session.OnStageStarted;
        Run.Hooks.StrikeWave += Session.OnStrikeWave;
        Run.Hooks.SystemEntered += Session.OnSystemEntered;
        Events.EventRunner.OutcomeChosen += Session.OnOutcomeChosen;
        DiplomacyTelemetry.Register();
        PlaytestNotes.Register();
        Log.Written += OnLogWritten;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Session.End("process_exit");
        AddAutotest();
    }

    /// <summary>Runs once per session start, on the main thread or a game build thread. Hooks what needs a running game.</summary>
    internal static void OnSessionStarted(Run run)
    {
        if (!_traceInstalled)
        {
            _traceInstalled = true;
            Errors.Guard("telemetry trace", () => Trace.Install(new Harmony(Mod.HarmonyId + ".telemetry"), run.T.Telemetry.TraceMethods));
        }
        if (!_frameHooked)
        {
            _frameHooked = true;
            Run.OnNextFrame(() => App.Director.FrameEnded += Perf.OnFrameEnded);
        }
    }

    static void OnLogWritten(string level, string message)
    {
        if (!Tele.On(Cat.Log)) return;
        Tele.Emit(Cat.Log, "log", (level, message), static (w, a) =>
        {
            w.WriteString("level", a.level);
            // Spec 1585: no user folder or Steam ID in the telemetry files.
            w.WriteString("msg", TelemetryModel.Scrub(a.message));
        });
    }

    /// <summary>
    /// Telemetry time on game threads in percent of the real time of the session. At a steady frame rate this is the share of the frame time
    /// (target: well below 1 %). Headless test games draw no frames, so their frame times are no measure.
    /// </summary>
    static double OverheadPct() =>
        Session.Current is { RealSeconds: > 0 } s ? Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks)) * 100 / (s.RealSeconds * 1000) : 0;

    static void AddAutotest()
    {
        // telename <name>: labels the telemetry of this game process. scripts/autotest.sh sends the script name first.
        Autotest.Commands["telename"] = arg => Label = arg;
        SelfProfile.AddAutotest();
        FrameProbe.AddAutotest();
        Autotest.Fields["tele_active"] = () => Session.Current is null ? 0 : 1;
        Autotest.Fields["tele_events"] = () => Interlocked.Read(ref Stats.Events);
        Autotest.Fields["tele_dropped"] = () => Interlocked.Read(ref Stats.Dropped);
        Autotest.Fields["tele_nested_dropped"] = () => Interlocked.Read(ref Stats.NestedDropped);
        Autotest.Fields["tele_overhead_ms"] = () => Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks));
        Autotest.Fields["tele_overhead_pct"] = OverheadPct;
        Autotest.Fields["tele_trace_missing"] = () => Stats.TraceMissing;
        Autotest.Fields["tele_trace_patched"] = () => Stats.TracePatched;
        // Spec 1763: game seconds of each activity in this session (tele_time_fight, tele_time_build, ...).
        foreach (var a in TimeUse.All) Autotest.Fields[$"tele_time_{TimeUse.Name(a)}"] = () => Session.Current?.SessionUse(a) ?? -1;
        // Spec 1593: the co-op relay. Client: bytes that the host has not confirmed. Host: bytes received, good and bad client copies.
        Autotest.Fields["tele_relay_pending"] = () => Session.Current is { } rs ? Relay.Pending(rs, closedOnly: false) : -1;
        Autotest.Fields["tele_relay_pending_closed"] = () => Session.Current is { } rs ? Relay.Pending(rs, closedOnly: true) : -1;
        Autotest.Fields["tele_relay_received"] = () => Relay.ReceivedBytes;
        Autotest.Fields["tele_relay_copies"] = () => Relay.CheckCopies().Good;
        Autotest.Fields["tele_relay_bad"] = () => Relay.CheckCopies().Bad;
        // telerelaychunk <bytes>: a smaller relay chunk on this machine, so a short test sends many chunks.
        Autotest.Commands["telerelaychunk"] = arg => Relay.TestChunkBytes = int.Parse(arg, CultureInfo.InvariantCulture);
        // teleflush: waits until the writer wrote every queued event, so a check can read the file.
        Autotest.Commands["teleflush"] = _ => Autotest.WaitFor(() => TelemetryWriter.PendingCount == 0, 10, "telemetry flush", () => Autotest.Wait(1.5));
        // telesnap [why]: one snapshot now. Single player only (it reads the sim from the GUI frame).
        Autotest.Commands["telesnap"] = arg => (Session.Current ?? throw new InvalidOperationException("no telemetry session")).Snapshot(Autotest.Game!.Sim, arg.Length > 0 ? arg : "test");
        // telecount <event> <op> <n>: counts the events of that name in every file of the current run. Run teleflush first.
        Autotest.Commands["telecount"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new InvalidOperationException("telecount needs: event op n");
            var s = Session.Current ?? throw new InvalidOperationException("no telemetry session");
            int count = CountEvents(Path.GetDirectoryName(s.FilePath)!, s.RunId, parts[0]);
            double n = double.Parse(parts[2], CultureInfo.InvariantCulture);
            bool ok = parts[1] switch { ">=" => count >= n, "<=" => count <= n, "==" => count == n, ">" => count > n, "<" => count < n, _ => throw new InvalidOperationException($"unknown op {parts[1]}") };
            Autotest.Check(ok, $"telemetry {parts[0]} count {parts[1]} {parts[2]} (actual {count})");
        };
        // damagehostiles <fraction>: sets every part of each hunter and hostile ship within 6000 of the fleet to this fraction of its health,
        // so the player's guns finish them. Test only, single player: it changes the sim from a GUI frame. It stands in for a won fight.
        Autotest.Commands["damagehostiles"] = arg =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("damagehostiles is single player only");
            float fraction = float.Parse(arg, CultureInfo.InvariantCulture);
            var sim = run.Game.Sim;
            var center = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ship");
            int n = 0;
            foreach (var ship in sim.Ships.ToList())
            {
                if (Session.Side(ship) is not ("hunter" or "hostile") || ship.DetWorldCenter.DistanceTo(center) > 6000) continue;
                foreach (var part in ship.Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * fraction));
                n++;
            }
            Log.Info($"AUTOTEST damagehostiles {fraction}: {n} ships");
        };
        // wrecks <n>: spawns n derelict built-in ships (player index -3, junk) on a ring 2,500 to 4,500 from the fleet, for a heavy scene.
        // Test only. In co-op run it through "mp": it uses the sim random source.
        Autotest.Commands["wrecks"] = arg =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            int count = int.Parse(arg, CultureInfo.InvariantCulture);
            var center = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ship");
            for (int i = 0; i < count; i++)
            {
                var pick = GameApp.Rules.BuiltinShips.GetRandomMatchingShip(sim.Rand, tier: new Range<int>(1, 4))
                    ?? throw new InvalidOperationException("no built-in ship for a wreck");
                var near = center + Vector2.FromAngle(i * 2.4f) * (2500f + 2000f * sim.Rand.Single());
                var loc = Autotest.ClearSpotNear(sim, near, 0f, 150f);
                new StasisShipSpawner(sim, pick.ID, loc, SpawnedShipFeatures.None, sim.Rand.UInt64()) { ObjectAllegiance = -3 }.Spawn(sim);
            }
            Log.Info($"AUTOTEST wrecks: {count} derelict ships");
        };
        // shieldplayers on|off: human ship parts lose no health and human crew do not die (TestShield). Test only. In co-op only
        // with "mp", so every machine turns it on at the same sim tick.
        Autotest.Commands["shieldplayers"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer && Autotest.SimPlayer is null)
                throw new InvalidOperationException("shieldplayers in co-op needs \"mp\" (it changes the sim)");
            TestShield.On = arg != "off";
            Log.Info($"AUTOTEST shieldplayers {TestShield.On}");
        };
        // teleactivity: logs the time use signals now and the construction queues of the own ships (TELEMETRY.md, Time use).
        Autotest.Commands["teleactivity"] = _ =>
        {
            var s = Session.Current ?? throw new InvalidOperationException("no telemetry session");
            var sim = Autotest.RequireRun().Game.Sim;
            var queues = string.Join("; ", sim.Ships.HumanPlayerSignificantShips.Select(sh => sh.OptionalConstructionManager is { } cm
                ? $"{sh.Metadata.PlainShipName}: adds {cm.QueuedPartConstructions.Count} (rebuild {cm.QueuedPartConstructions.Count(q => q.IsRepair)}, blocked {cm.BlockedPartConstructions.Count}), repairs {cm.QueuedPartRepairs.Count}, jobs [{string.Join(",", sh.Crew.Assigned.Select(c => c.Job is Cosmoteer.Ships.Crew.Jobs.ResourceTransferJob rj ? $"Transfer:{rj.TradeType}" : c.Job?.GetType().Name).Where(n => n is not null and not "IdleJob"))}]"
                : $"{sh.Metadata.PlainShipName}: no construction manager"));
            Log.Info($"AUTOTEST teleactivity {ActivitySignalsReader.Read(s, sim)} | {queues}");
        };
        // telerepair: the repair-all button of the own ship's build window, without a purchase (time use: repair). Test only.
        Autotest.Commands["telerepair"] = _ => Playthrough.QueueRepair(
            Autotest.RequireRun().Game.Sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship"), (_, _) => { });
        // telemove <distance>: one right click that orders the own ship to a point this far ahead (time use: travel). Test only.
        Autotest.Commands["telemove"] = arg =>
        {
            var game = Autotest.RequireRun().Game;
            var ship = game.Sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship");
            var to = ship.DetWorldCenter + Vector2.FromAngle(0f) * float.Parse(arg, CultureInfo.InvariantCulture);
            var move = new Cosmoteer.Ships.Commands.MoveCommand(ship, ship.Commands.AllocLocalCommandID(), to, null, Cosmoteer.Ships.Commands.CollisionAvoidanceFlags.All, isTentative: false);
            game.NetManager.EnqueueInput(new Cosmoteer.Game.Multiplayer.MPInputs.MPEnqueueCommandsInput(move, append: false, playLocalSound: false));
        };
        // telenote open | type <text> | save | cancel: the note key, the text field, Enter and Escape (spec 1986). Test only.
        Autotest.Commands["telenote"] = arg =>
        {
            var parts = arg.Split(' ', 2);
            var d = PlaytestNotes.OpenDialog;
            switch (parts[0])
            {
                case "open": PlaytestNotes.Open(Autotest.RequireRun()); break;
                case "type": (d ?? throw new InvalidOperationException("no note dialog")).TextEditField.Text = parts.Length > 1 ? parts[1] : ""; break;
                case "save" or "cancel":
                    var dialog = d ?? throw new InvalidOperationException("no note dialog");
                    dialog.Response = parts[0] == "save" ? TwoButtonTextEntryDialog.DialogResponse.Option1 : TwoButtonTextEntryDialog.DialogResponse.Option2;
                    App.Director.PopState(dialog);
                    break;
                default: throw new InvalidOperationException("telenote needs: open | type <text> | save | cancel");
            }
        };
        // Spec 1994: mission ships, items and areas that the last positions record named.
        Autotest.Fields["tele_positions_objects"] = () => MissionPositions.LastObjects;
        Autotest.Fields["telenote_open"] = () => PlaytestNotes.OpenDialog is null ? 0 : 1;
        // 1 when the note key would open a note now (spec 1984).
        Autotest.Fields["telenote_can_open"] = () => PlaytestNotes.CanOpen(Autotest.RequireRun()) ? 1 : 0;
        // telenote_textbox on|off: a text box like the co-op chat (BlockOtherInputWhileFocused) takes or gives up the focus. Test only.
        Autotest.Commands["telenote_textbox"] = arg =>
        {
            var gui = Autotest.RequireRun().Game.Gui;
            if (arg == "on")
            {
                _textBox ??= new Halfling.Gui.TextEditField(Halfling.Gui.TextEditField.Style.SingleLine) { BlockOtherInputWhileFocused = true };
                if (_textBox.Parent is null) gui.RootWidget.AddChild(_textBox);
                _textBox.FocusController.RequestFocus();
            }
            else if (_textBox is not null)
            {
                _textBox.FocusController.RelinquishFocus();
                gui.RootWidget.Children.Remove(_textBox);
            }
        };
        // Lines of the live notes file from this player and from other players (spec 1988).
        Autotest.Fields["telenote_live_own"] = () => LiveNotes(own: true);
        Autotest.Fields["telenote_live_others"] = () => LiveNotes(own: false);
        // telestats: logs the overhead numbers of the session, for reports.
        Autotest.Commands["telestats"] = _ =>
            Log.Info($"TELEMETRY STATS events={Interlocked.Read(ref Stats.Events)} bytes={Interlocked.Read(ref Stats.Bytes)} dropped={Interlocked.Read(ref Stats.Dropped)} " +
                     $"overhead_ms={Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks)):0.0} frames={Perf.SessionFrames} frame_ms={Perf.SessionFrameMs:0} " +
                     $"overhead_pct_wall={OverheadPct():0.000} real_s={Session.Current?.RealSeconds:0.0} ticks={Perf.SessionTicks} tick_ms={Perf.SessionTickMs:0} " +
                     $"writer_ms={Stats.Ms(Interlocked.Read(ref Stats.WriterTicks)):0.0} trace_patched={Stats.TracePatched} trace_missing={Stats.TraceMissing} " +
                     $"missing=[{string.Join(", ", Trace.MissingMethods)}] file={Session.Current?.FilePath}");
    }

    static int LiveNotes(bool own)
    {
        if (!File.Exists(PlaytestNotes.LivePath)) return 0;
        int me = Autotest.RequireRun().Game.LocalPlayerIndex;
        return File.ReadLines(PlaytestNotes.LivePath).Count(l =>
        {
            using var doc = System.Text.Json.JsonDocument.Parse(l);
            return (doc.RootElement.GetProperty("local_player").GetInt32() == me) == own;
        });
    }

    /// <summary>Counts lines with "e":"name" in the files of a run: every session and part file.</summary>
    static int CountEvents(string dir, string runId, string name)
    {
        string needle = $"\"e\":\"{name}\"";
        int count = 0;
        foreach (string file in Directory.GetFiles(dir, runId + "__*.jsonl"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line) if (line.Contains(needle, StringComparison.Ordinal)) count++;
        }
        return count;
    }
}

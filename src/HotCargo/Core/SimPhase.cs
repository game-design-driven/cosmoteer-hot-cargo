using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace HotCargo.Core;

/// <summary>
/// Spec 1009: the sim changes only inside a sim phase. A sim phase is a part of the frame that every co-op machine runs at the same
/// point: the sim tick (SimRoot.FixedUpdate), the input tick (SimRoot.OnInputTick, GameModeManager.OnInputTick) and the execution
/// of player inputs (SPManager.Update, BaseMPManager.AdvanceNetworkTime). All other code is frame code: UI, draw, widget callbacks, Run.Hooks.Frame.
/// At the end of each phase this takes a fingerprint of sim state. At the start of the next phase the fingerprint must be the same.
/// A difference means that frame code changed the sim, which desyncs co-op. It reads only.
/// Limit: the fingerprint holds the sim random stream, the player states and, in autotests, the whole run state (RunHash). A change
/// of other state (a ship) passes. Revisit with the ships hash (sim.Ships.GetIntegrityHash) in the fingerprint if a desync from
/// frame code gets through; first check that vanilla frame code does not break it.
/// </summary>
public static class SimPhase
{
    readonly record struct Print(Run Run, SimRoot Sim, int Rand, int State, int Players)
    {
        /// <summary>The names of the parts that differ from another fingerprint of the same run and sim.</summary>
        internal List<string> Changed(Print now)
        {
            List<string> parts = [];
            if (Rand != now.Rand) parts.Add("the sim random stream");
            if (State != now.State) parts.Add("the run state");
            if (Players != now.Players) parts.Add("a player state");
            return parts;
        }
    }

    static Print? _closed;
    static int _depth;
    static string _lastPhase = "game start";

    /// <summary>The main thread runs every phase. A game that loads builds its state on a worker thread, outside the check.</summary>
    static bool OnMain => Halfling.App.Director.MainThread == Thread.CurrentThread;

    /// <summary>Ends the phase that <see cref="Open"/> started.</summary>
    public readonly struct Scope : IDisposable
    {
        public void Dispose() => Exit();
    }

    /// <summary>
    /// Starts a phase by hand, for code that may change the sim outside the vanilla phases: test commands, single-player debug
    /// keys and the single-player tunables reload. Never use it to pass the check for code that also runs in co-op.
    /// </summary>
    public static Scope Open(string what)
    {
        Enter(what);
        return default;
    }

    /// <summary>
    /// The game that ran a vanilla phase last. A game that starts (a new game, a load, a co-op launch or resync) is built on a worker
    /// thread while Run.Current already names it and the main thread still draws frames: no fingerprint before its first phase.
    /// </summary>
    static GameRoot? _live;

    /// <summary>
    /// Issue 386: no check during a node jump (GameRoot.IsDoingNodeJump). Vanilla builds the next system on a worker thread
    /// (GameRoot.JumpToNode, Task.Run) and that build adds player states to the game. The same build runs on every co-op machine at the
    /// jump signals, so it is no desync; and a fingerprint read of the player states while that thread adds one throws ("Collection was
    /// modified") and crashes the game. The jump ends with another sim, so a new fingerprint starts after it.
    /// Limit: frame code that changes the sim during a jump passes. Revisit if a desync shows right after a jump.
    /// </summary>
    static bool On(out Run run)
    {
        run = Run.Current!;
        return run is not null && run.Game == _live && run.T.Invariants.Enabled && run.T.Invariants.SimPhase && run.Game.Sim is not null
            && !run.Game.IsDoingNodeJump;
    }

    static void EnterVanilla(string what, GameRoot? game)
    {
        if (game is not null && OnMain) _live = game;
        Enter(what);
    }

    static void Enter(string what)
    {
        if (!OnMain) return;
        if (_depth++ > 0) return;
        Check($"between '{_lastPhase}' and '{what}'");
        _lastPhase = what;
    }

    static void Exit()
    {
        if (!OnMain) return;
        if (_depth == 0) throw new InvalidOperationException("SimPhase.Exit without Enter");
        if (--_depth > 0) return;
        _closed = On(out var run) ? Take(run) : null;
    }

    /// <summary>
    /// Frame code only: compares the sim with the last fingerprint now, and names <paramref name="where"/> as the code that changed it.
    /// Run.Hooks.Frame calls it after each handler in autotests, so the ERROR line names the handler.
    /// </summary>
    internal static void Checkpoint(string where)
    {
        if (_depth > 0 || !OnMain) return;
        Check($"in '{where}'");
    }

    static void Check(string where)
    {
        if (_closed is not { } before) return;
        _closed = null;
        // Another run or another sim (a new game, a load, a resync, a jump) starts a new fingerprint.
        if (!On(out var run) || before.Run != run || before.Sim != run.Game.Sim) return;
        var now = Take(run);
        _closed = now;
        var changed = before.Changed(now);
        if (changed.Count == 0) return;
        string parts = string.Join(" and ", changed);
        Dev.Invariants.Fail("sim_outside_phase", $"{parts}|{where}",
            $"frame code changed {parts} {where}. The sim changes only in sim ticks, Run.Hooks (not Frame), rules spawners and MP inputs (spec 1009)");
    }

    static Print Take(Run run)
    {
        var game = run.Game;
        var sim = game.Sim;
        return new(run, sim, Hash(sim.Rand.GetIntegrityHash), StateHash(run), Hash(h =>
        {
            // The same player states as GameRoot.GetIntegrityHash (0.30.5a).
            foreach (var (index, state) in game._playerStates)
            {
                if (!state.ExistsDeterministically || state.Team == -5) continue;
                index.GetIntegrityHash(h);
                state.GetIntegrityHash(h);
            }
        }));
    }

    /// <summary>
    /// The run state hash covers every saved member (RunHash) and costs 9 to 30 µs, several times in each frame. So only autotests
    /// take it: they are where a broken rule fails a build. In play the fingerprint holds the random stream and the player states,
    /// and the game's own integrity hash (with the run state in it) finds a co-op difference at the next input tick.
    /// A test that profiles the mod ("selfprof on") measures the cost of play, so it runs without it too.
    /// </summary>
    static int StateHash(Run run) => Dev.Autotest.IsActive && !AsInPlay ? RunHash.Of(run.State) : 0;

    /// <summary>Test only: set by the profiler ("selfprof on"), so the fingerprint is the one of play.</summary>
    internal static bool AsInPlay;

    static int Hash(Action<IntegrityHash> add)
    {
        using var h = IntegrityHash.Alloc(0, 0, IntegrityHashPhase.TickStart, 0, recordHashes: false, validator: null);
        add(h);
        return h.Hash;
    }

    // The vanilla phases. A finalizer ends the phase also when the vanilla method throws.

    [HarmonyPatch(typeof(SimRoot), "FixedUpdate")]
    static class Tick
    {
        static void Prefix(SimRoot __instance) => EnterVanilla("sim tick", __instance.Game);
        static void Finalizer() => Exit();
    }

    [HarmonyPatch(typeof(SimRoot), nameof(SimRoot.OnInputTick))]
    static class SimInputTick
    {
        static void Prefix(SimRoot __instance) => EnterVanilla("input tick", __instance.Game);
        static void Finalizer() => Exit();
    }

    [HarmonyPatch(typeof(GameModeManager), nameof(GameModeManager.OnInputTick))]
    static class ModeInputTick
    {
        static void Prefix(GameModeManager __instance) => EnterVanilla("input tick", __instance.Game);
        static void Finalizer() => Exit();
    }

    /// <summary>
    /// Player inputs execute here (0.30.5a): in single player in the Update of SPManager and RecordableSPManager, in co-op in
    /// BaseMPManager.AdvanceNetworkTime.
    /// </summary>
    [HarmonyPatch]
    static class Inputs
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            new (Type Type, string Name)[]
            {
                (typeof(SPManager), nameof(NetManager.Update)), (typeof(RecordableSPManager), nameof(NetManager.Update)),
                (typeof(BaseMPManager), nameof(NetManager.AdvanceNetworkTime)),
            }.Select(m => AccessTools.DeclaredMethod(m.Type, m.Name) ?? throw new MissingMethodException(m.Type.Name, m.Name));

        static void Prefix(NetManager __instance) => EnterVanilla("player inputs", __instance.Game);
        static void Finalizer() => Exit();
    }
}

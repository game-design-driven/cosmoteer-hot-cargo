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
/// of player inputs (NetManager.AdvanceNetworkTime). All other code is frame code: UI, draw, widget callbacks, Run.Hooks.Frame.
/// At the end of each phase this takes a fingerprint of sim state. At the start of the next phase the fingerprint must be the same.
/// A difference means that frame code changed the sim, which desyncs co-op. It reads only.
/// Limit: the fingerprint holds the sim random stream, the run state hash and the player states, plus the ships with
/// Invariants.SimPhaseShips. A change of other state passes. Revisit if a desync gets through with the ships hash on.
/// </summary>
public static class SimPhase
{
    /// <summary>The parts of the fingerprint. Ships is 0 while Invariants.SimPhaseShips is off.</summary>
    readonly record struct Print(Run Run, SimRoot Sim, int Rand, int State, int Players, int Ships)
    {
        /// <summary>The names of the parts that differ from another fingerprint of the same run and sim.</summary>
        internal List<string> Changed(Print now)
        {
            List<string> parts = [];
            if (Rand != now.Rand) parts.Add("the sim random stream");
            if (State != now.State) parts.Add("the run state");
            if (Players != now.Players) parts.Add("a player state");
            if (Ships != now.Ships) parts.Add("the ships");
            return parts;
        }
    }

    static Print? _closed;
    static int _depth;
    static string _lastPhase = "game start";

    /// <summary>The game's main thread runs every phase. A game that loads builds its state on a worker thread, outside the check.</summary>
    static int _thread;

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

    static bool On(out Run run)
    {
        run = Run.Current!;
        return run is not null && run.T.Invariants.Enabled && run.T.Invariants.SimPhase && run.Game.Sim is not null;
    }

    static void Enter(string what)
    {
        if (_thread == 0) _thread = Environment.CurrentManagedThreadId;
        if (Environment.CurrentManagedThreadId != _thread) return;
        if (_depth++ > 0) return;
        Check($"between '{_lastPhase}' and '{what}'");
        _lastPhase = what;
    }

    static void Exit()
    {
        if (Environment.CurrentManagedThreadId != _thread) return;
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
        if (_depth > 0 || Environment.CurrentManagedThreadId != _thread) return;
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
        return new(run, sim, Hash(sim.Rand.GetIntegrityHash), Hash(h => CareerPatches.AddRunHash(run, h)), Hash(h =>
        {
            // The same player states as GameRoot.GetIntegrityHash (0.30.5a).
            foreach (var (index, state) in game._playerStates)
            {
                if (!state.ExistsDeterministically || state.Team == -5) continue;
                index.GetIntegrityHash(h);
                state.GetIntegrityHash(h);
            }
        }), run.T.Invariants.SimPhaseShips ? Hash(sim.Ships.GetIntegrityHash) : 0);
    }

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
        static void Prefix() => Enter("sim tick");
        static void Finalizer() => Exit();
    }

    [HarmonyPatch(typeof(SimRoot), nameof(SimRoot.OnInputTick))]
    static class SimInputTick
    {
        static void Prefix() => Enter("input tick");
        static void Finalizer() => Exit();
    }

    [HarmonyPatch(typeof(GameModeManager), nameof(GameModeManager.OnInputTick))]
    static class ModeInputTick
    {
        static void Prefix() => Enter("input tick");
        static void Finalizer() => Exit();
    }

    /// <summary>Player inputs execute here: single player (SPManager, RecordableSPManager) and co-op (BaseMPManager).</summary>
    [HarmonyPatch]
    static class Inputs
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            new[] { typeof(SPManager), typeof(RecordableSPManager), typeof(BaseMPManager) }
                .Select(t => AccessTools.DeclaredMethod(t, nameof(NetManager.AdvanceNetworkTime))
                    ?? throw new MissingMethodException(t.Name, nameof(NetManager.AdvanceNetworkTime)));

        static void Prefix() => Enter("player inputs");
        static void Finalizer() => Exit();
    }
}

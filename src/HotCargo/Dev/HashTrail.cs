using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Dev;

/// <summary>
/// Co-op autotests: a desync names the state that split. At each TickStart integrity hash (every input tick), this records one hash per
/// part of GameRoot.GetIntegrityHash and SimRoot.GetIntegrityHash, and one hash per ship, for the last <see cref="Ticks"/> input ticks.
/// On a desync both machines log them (MPHASHTRAIL, MPHASHSHIPS), and scripts/coop-report.sh names the first part and the ships
/// that differ. Before this, a desync said only "phase TickStart" (#305, queue run 45497788356: input tick 860 in hunters.capture).
/// Cost: about 0.5 ms per input tick (part and ship hashes), in co-op autotests only, and not while a profile runs. Reads only.
/// </summary>
[HarmonyPatch(typeof(GameRoot), nameof(GameRoot.GetIntegrityHash))]
static class HashTrail
{
    /// <summary>The client finds a desync some input ticks late (it waits for the host's hashes), so the trail reaches back this far.</summary>
    const int Ticks = 32;

    sealed record Entry(int Game, int InputTick, int SimTick, string Parts, string Ships);

    static readonly Queue<Entry> _trail = new();

    static void Postfix(GameRoot __instance, IntegrityHash iHash)
    {
        // A profile ("selfprof on") measures the cost of play, so this test-only trail stays off then, like the run state hash
        // (SimPhase.AsInPlay). In coop-stress it took 0.51 ms of each 6 ms tick, a third of the measured Hot Cargo time (#367).
        if (!Autotest.IsActive || SimPhase.AsInPlay || iHash.Phase != IntegrityHashPhase.TickStart || __instance.NetManager is not BaseMPManager
            || Run.Current is not { } run || run.Game != __instance) return;
        var sim = __instance.Sim;
        int Part(Action<IntegrityHash> add)
        {
            using var h = IntegrityHash.Alloc(iHash.Tick, iHash.InputTick, iHash.Phase, iHash.Bucket, recordHashes: false, validator: null);
            add(h);
            return h.Hash;
        }
        // The same parts and order as GameRoot.GetIntegrityHash and SimRoot.GetIntegrityHash (0.30.5a).
        string parts = string.Join(" ", new (string Name, int Hash)[]
        {
            ("last", __instance.NetManager.LastIntegrityHash),
            ("clock", Part(h =>
            {
                sim.Clock.Time.GetIntegrityHash(h);
                sim._inputTickClockTime.GetIntegrityHash(h);
                sim.FixedUpdater.FixedUpdateCount.GetIntegrityHash(h);
                sim.Tick.GetIntegrityHash(h);
                sim.LogicalTime.GetIntegrityHash(h);
            })),
            ("ships", Part(sim.Ships.GetIntegrityHash)),
            ("crew", Part(sim.Crew.GetIntegrityHash)),
            ("nuggets", Part(sim.Nuggets.GetIntegrityHash)),
            ("nebulas", Part(sim.Nebulas.GetIntegrityHash)),
            ("bullets", Part(sim.Bullets.GetIntegrityHash)),
            ("doodads", Part(sim.Doodads.GetIntegrityHash)),
            ("simmode", Part(sim.Mode.GetIntegrityHash)),
            ("rand", Part(sim.Rand.GetIntegrityHash)),
            ("map", Part(__instance.Map.GetIntegrityHash)),
            ("mode", Part(__instance.Mode.GetIntegrityHash)),
            ("run", Part(h => CareerPatches.AddRunHash(run, h))),
            ("players", Part(h =>
            {
                foreach (var (index, state) in __instance._playerStates)
                {
                    if (!state.ExistsDeterministically || state.Team == -5) continue;
                    index.GetIntegrityHash(h);
                    state.GetIntegrityHash(h);
                }
            })),
        }.Select(p => $"{p.Name}={p.Hash:X8}"));
        string ships = string.Join(" ", sim.Ships.Select(s => $"{s.UniqueID}:{(sim.Mode.IncludeInIntegrityHash(s) ? Part(s.GetIntegrityHash) : 0):X8}"));
        _trail.Enqueue(new Entry(MpAutotest._mpGames, iHash.InputTick, iHash.Tick, parts, ships));
        while (_trail.Count > Ticks) _trail.Dequeue();
    }

    /// <summary>Logs the trail once per desync, on each machine. Called by DesyncPatch.</summary>
    internal static void Dump()
    {
        foreach (var e in _trail)
        {
            Log.Info($"MPHASHTRAIL game={e.Game} input={e.InputTick} sim={e.SimTick} {e.Parts}");
            Log.Info($"MPHASHSHIPS game={e.Game} input={e.InputTick} {e.Ships}");
        }
        _trail.Clear();
    }
}

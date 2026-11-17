using System.Security.Cryptography;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Dev;

/// <summary>
/// Spec 1513. At the start of each co-op game, every machine logs one hash per part of the world that it built (MPWORLD), and the
/// content that tells the parts apart (MPWORLDLIST). The game's own integrity check sees a different start world only as a desync,
/// and never names the part; scripts/coop-report.sh compares these lines and names it. Lockstep machines start the same games in
/// the same order, so the n-th line of the host belongs to the n-th line of the client.
/// Runs in every co-op game, not only in tests: a playtest desync then shows in the host's and the client's logs. It reads only.
/// </summary>
[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.OnGameStarting)), HarmonyPriority(Priority.Last)]
static class WorldHash
{
    /// <summary>Co-op games started in this process. The key of the lines.</summary>
    static int _games;

    static void Postfix(CareerGameModeManager __instance, bool loaded) => Errors.Guard("world hash", () =>
    {
        var game = __instance.Game;
        if (!game.NetManager.IsMultiplayer || Run.Current is not { } run || run.Game != game) return;
        var sim = game.Sim;
        _games++;
        var parts = new (string Name, string Hash)[]
        {
            ("seed", Text($"{__instance.Init.RandSeed} {sim.MapNode?.UniqueID} {((CareerSimModeManager)sim.Mode).MapNodeData.RandomSeed}")),
            ("rand", Text($"{sim.Rand._a:x} {sim.Rand._b:x}")),
            ("ships", Vanilla(sim.Ships.GetIntegrityHash)),
            ("crew", Vanilla(sim.Crew.GetIntegrityHash)),
            ("nuggets", Vanilla(sim.Nuggets.GetIntegrityHash)),
            ("nebulas", Vanilla(sim.Nebulas.GetIntegrityHash)),
            ("doodads", Vanilla(sim.Doodads.GetIntegrityHash)),
            ("simmode", Vanilla(sim.Mode.GetIntegrityHash)),
            ("missions", Vanilla(__instance.GetIntegrityHash)),
            ("stasis", Text(string.Join(";", Stasis(sim)))),
            ("offers", Text(string.Join(";", Offers(sim)))),
            ("run", MpAutotest.Hash("RunState", run.State, typeof(RunState))),
        };
        Log.Info($"MPWORLD n={_games} loaded={loaded} {string.Join(" ", parts.Select(p => $"{p.Name}={p.Hash}"))}");
        Log.Info($"MPWORLDLIST n={_games} offers=[{string.Join(",", Counts(Offers(sim).Select(o => o.Split(':')[0])))}] " +
                 $"stasistags=[{string.Join(",", Counts(sim.Stasis.SelectMany(s => SimQueries.StasisTags(s)?.Select(t => t.ToString()) ?? [])))}] " +
                 $"ships={sim.Ships.Count} stasis={sim.Stasis.Count()} doodads={sim.Doodads.Count()}");
    });

    /// <summary>A vanilla integrity hash of one part, as hex. The hash phase is the same on every machine.</summary>
    static string Vanilla(Action<IntegrityHash> part)
    {
        var iHash = IntegrityHash.Alloc();
        try
        {
            part(iHash);
            return iHash.Hash.ToString("X8");
        }
        finally { iHash.Release(); }
    }

    static string Text(string s) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)), 0, 4);

    /// <summary>
    /// Stasis spawners in ID order: ID, type, place, owner and tags. Not the vanilla hash: it includes IsPreloaded, which follows each
    /// machine's own camera and load timing. The vanilla sim hash leaves stasis out.
    /// </summary>
    static IEnumerable<string> Stasis(SimRoot sim) =>
        sim.Stasis.OrderBy(s => s.ReservedObjectID).Select(s =>
            $"{s.ReservedObjectID}:{s.GetType().Name}:{s.Location}:{s.ObjectAllegiance}:" +
            string.Join("|", (SimQueries.StasisTags(s)?.Select(t => t.ToString()) ?? []).Order(StringComparer.Ordinal)));

    /// <summary>Board and global mission offers: metatype and mission ID, sorted.</summary>
    static IEnumerable<string> Offers(SimRoot sim) =>
        Missions.MissionArea.Offers(sim).Select(m => $"{m.MetatypeID}:{m.MissionID}").Order(StringComparer.Ordinal);

    static IEnumerable<string> Counts(IEnumerable<string> keys) =>
        keys.GroupBy(k => k).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}x{g.Count()}");
}

using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Missions;

namespace HotCargo.Dev;

/// <summary>
/// Spec 1700: a single-player autotest world rolls no Hot Cargo board missions, unless its script has the line
/// "# missions: random". So a test that is not about missions does not depend on the mission odds of the release. Outside an
/// autotest, and in co-op sessions (a client runs another script), every gate stays open.
/// </summary>
static class TestWorld
{
    internal const string RandomMissionsLine = "# missions: random";

    /// <summary>Set by Autotest.Init from the script, before the first world generates.</summary>
    internal static bool RandomMissions;

    internal static bool MissionsOff(GameRoot? game) => Autotest.IsActive && !RandomMissions && game?.NetManager.IsMultiplayer != true;

    /// <summary>
    /// Turns off each innermost spawner group that holds a HotCargo* mission spawner, before the system generates. The innermost group,
    /// because an outer group can hold a whole archetype (its current, its asteroids). Runs after the mechanics chances (spec 131).
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.InitSimRoot))]
    [HarmonyPriority(Priority.Last)]
    static class NoMissionsPatch
    {
        static void Prefix(CareerSimModeManager __instance) => Errors.Guard("test world", () =>
        {
            if (!Activation.IsModEnabled() || !MissionsOff(__instance.Sim.Game)) return;
            var groups = new HashSet<SimObjectSpawner>();
            Collect(__instance.SectorType.SystemGenerator.Spawners, null, groups);
            foreach (var group in groups) group.SpawnChance = 0f;
            Log.Info($"test world: {groups.Count} Hot Cargo mission groups off in {__instance.SectorType.ID} (no '{RandomMissionsLine}' line)");
        });

        static void Collect(IEnumerable<SimSpawner> spawners, SimObjectSpawner? parent, HashSet<SimObjectSpawner> groups)
        {
            foreach (var s in spawners)
            {
                if (s is MissionSpawner m && m.Metatype.ToString().StartsWith("HotCargo", StringComparison.Ordinal))
                    groups.Add(parent ?? throw new InvalidOperationException($"mission spawner {m.Metatype} is not inside a spawner group"));
                if (s is SimObjectSpawner { SubSpawners: { } subs } g) Collect(subs, g, groups);
            }
        }
    }
}

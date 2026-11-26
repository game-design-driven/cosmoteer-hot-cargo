using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Generators.Galaxies;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Map;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Journey;

/// <summary>
/// Spec 2414: the galaxy tunables (Journey.RunSectorCount, Journey.FinalSectorTier, Worlds.SystemTypeWeights) go into the spawners of
/// the run map (mod/journey/galaxy.rules) just before a new galaxy is made. The galaxy is made once per run, so they apply to new runs.
/// Each co-op machine makes the galaxy itself, so co-op uses the defaults (TunablesFile.ForNewGame).
/// </summary>
[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.InitGalaxyMap))]
static class MapRules
{
    static void Prefix(CareerGameModeManager __instance) => Errors.Guard("map tunables", () =>
    {
        int index = Math.Clamp(__instance.Init.MapSizeIndex, 0, __instance.Rules.MapSizes.Length - 1);
        if (index != JourneyArea.RunMapSizeIndex()) return;
        Apply(__instance.Rules.MapSizes[index].MapGenerator.Spawners, TunablesFile.ForNewGame(__instance.Game));
    });

    internal static void Apply(IEnumerable<GalaxySpawner> spawners, Tunables t)
    {
        foreach (var spawner in spawners)
        {
            switch (spawner)
            {
                case RunMapSpawner map:
                    map.Steps = t.Journey.RunSectorCount;
                    break;
                case RunMapTiersSpawner tiers:
                    tiers.FinalTier = t.Journey.FinalSectorTier;
                    break;
                case RandomSectorTypesSpawner types:
                    var weights = t.Worlds.SystemTypeWeights.Select(w => (w.Type, w.Weight)).ToList();
                    foreach (var w in weights.Where(w => !types.SectorTypes.Any(s => s.Type.ToString() == w.Type)))
                        Errors.Report("map tunables", $"Worlds.SystemTypeWeights names '{w.Type}', which mod/worlds/archetypes.rules does not have");
                    foreach (var s in types.SectorTypes) s.RandomWeight = MapRulesModel.Weight(weights, s.Type.ToString());
                    break;
            }
        }
        Log.Info($"map tunables: {t.Journey.RunSectorCount} steps, final tier {t.Journey.FinalSectorTier}, " +
                 $"type weights [{string.Join(", ", t.Worlds.SystemTypeWeights.Select(w => $"{w.Type} {w.Weight:0.##}"))}]");
    }
}

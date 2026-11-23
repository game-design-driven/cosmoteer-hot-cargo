using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Generators.Galaxies;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Map;
using Halfling;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Journey;

/// <summary>
/// Spec 1983: the galaxy tunables (Journey.FinalSectorTier, Journey.MapSystems, Worlds.SystemTypeWeights) go into the spawners of the
/// run map (mod/journey/galaxy.rules) just before a new galaxy is made. The galaxy is made once per run, so they apply to new runs.
/// Each co-op machine makes the galaxy itself, so co-op uses the defaults (TunablesFile.ForNewGame).
/// </summary>
[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.InitGalaxyMap))]
static class MapRules
{
    /// <summary>The rules values of a spawner before the first change, so every new galaxy starts from the rules again.</summary>
    static readonly ConditionalWeakTable<object, object> Originals = new();

    static T Original<T>(object spawner, Func<T> read) where T : class => (T)Originals.GetValue(spawner, _ => read());

    static void Prefix(CareerGameModeManager __instance) => Errors.Guard("map tunables", () =>
    {
        int index = Math.Clamp(__instance.Init.MapSizeIndex, 0, __instance.Rules.MapSizes.Length - 1);
        if (index != JourneyArea.RunMapSizeIndex()) return;
        Apply(__instance.Rules.MapSizes[index].MapGenerator.Spawners, TunablesFile.ForNewGame(__instance.Game));
    });

    internal static void Apply(IEnumerable<GalaxySpawner> spawners, Tunables t)
    {
        var list = spawners.ToList();
        var progression = list.OfType<ProgressionNodeTiersSpawner>().Single();
        int rulesFinal = Original(progression, () => new[] { progression.MaxLowTier, progression.MaxChangeableLowTier })[0];
        int final = t.Journey.FinalSectorTier;
        int Shift(int v) => MapRulesModel.Shift(v, rulesFinal, final);
        foreach (var spawner in list)
        {
            switch (spawner)
            {
                case MapNodesSpawner nodes:
                    nodes.Count = new Range<int>(t.Journey.MapSystems, t.Journey.MapSystems);
                    break;
                case ProgressionNodeTiersSpawner p:
                    var pv = Original(p, () => new[] { p.MaxLowTier, p.MaxChangeableLowTier });
                    p.MaxLowTier = Shift(pv[0]);
                    p.MaxChangeableLowTier = Shift(pv[1]);
                    break;
                case TierCapSpawner cap:
                    cap.MaxChangeableLowTier = Shift(Original(cap, () => new[] { cap.MaxChangeableLowTier })[0]);
                    break;
                case FactionNodeTiersSpawner factions:
                    var fv = Original(factions, () => factions.Factions.Select(f => (f.TierRangeLow, f.TierRangeHigh)).ToArray());
                    for (int i = 0; i < factions.Factions.Length; i++)
                    {
                        var (low, high) = fv[i];
                        factions.Factions[i].TierRangeLow = new Range<int>(Shift(low.Min), Shift(low.Max));
                        factions.Factions[i].TierRangeHigh = new Range<int>(Shift(high.Min), Shift(high.Max));
                    }
                    break;
                case RandomSectorTypesSpawner types:
                    var weights = t.Worlds.SystemTypeWeights.Select(w => (w.Type, w.Weight)).ToList();
                    foreach (var w in weights.Where(w => !types.SectorTypes.Any(s => s.Type.ToString() == w.Type)))
                        Errors.Report("map tunables", $"Worlds.SystemTypeWeights names '{w.Type}', which mod/worlds/archetypes.rules does not have");
                    foreach (var s in types.SectorTypes) s.RandomWeight = MapRulesModel.Weight(weights, s.Type.ToString());
                    break;
            }
        }
        Log.Info($"map tunables: final sector tier {final} (rules {rulesFinal}), {t.Journey.MapSystems} systems, " +
                 $"type weights [{string.Join(", ", t.Worlds.SystemTypeWeights.Select(w => $"{w.Type} {w.Weight:0.##}"))}]");
    }
}

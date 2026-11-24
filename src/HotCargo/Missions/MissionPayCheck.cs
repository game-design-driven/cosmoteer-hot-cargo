using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Missions;

/// <summary>
/// Spec 2252: every mission that pays a reward pays it at a station (vanilla ReceiveRewards), like vanilla missions. A mission whose
/// rewards are only contract terms (HotCargoContract: a page trade such as Off the Books, the Buyer sale) pays nothing at a station.
/// The rules decide which missions those are: this check reads every mission spawner that the game loaded (sector generator, events,
/// chains, worlds).
/// </summary>
internal static class MissionPayCheck
{
    static readonly List<WeakReference<MissionSpawner>> Loaded = [];

    [HarmonyPatch(typeof(MissionSpawner), MethodType.Constructor)]
    static class Track
    {
        static void Postfix(MissionSpawner __instance)
        {
            lock (Loaded) Loaded.Add(new WeakReference<MissionSpawner>(__instance));
        }
    }

    /// <summary>The metatypes of loaded missions that pay a reward without a ReceiveRewards step. Empty when the rule holds.</summary>
    internal static List<string> PayOnCompletion()
    {
        List<MissionSpawner> spawners;
        lock (Loaded) spawners = [.. Loaded.Select(w => w.TryGetTarget(out var s) ? s : null).OfType<MissionSpawner>()];
        // Limit: only the mod's metatypes (their IDs start with HotCargo). Vanilla Ambush pays on completion by its own design, and the mod
        // leaves vanilla missions as they are. Revisit if a mod metatype gets another prefix.
        var bad = spawners.Where(s => s.Metatype.ToString().StartsWith("HotCargo", StringComparison.Ordinal) && Pays(s) && !s.Objectives.Any(HasReceiveRewards)).ToList();
        return [.. bad.Select(s => s.Metatype.ToString()).Distinct().Order()];
    }

    static bool Pays(MissionSpawner s) => s.Rewards?.Any(r => r is not Contracts.ContractTermsRules) == true;

    /// <summary>The spawner or one of its sub-objective spawners (any ObjectiveSpawner[] field: Sequential, AnyOrder, TimeLimit) is ReceiveRewards.</summary>
    static bool HasReceiveRewards(ObjectiveSpawner o) =>
        o is ReceiveRewardsObjective.Spawner
        || o.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(ObjectiveSpawner[]))
            .Any(f => f.GetValue(o) is ObjectiveSpawner[] subs && subs.Any(HasReceiveRewards));

    internal static void Register()
    {
        Autotest.Fields["hc_missions_pay_on_completion"] = () =>
        {
            var list = PayOnCompletion();
            Log.Info($"AUTOTEST missions that pay on completion: [{string.Join(", ", list)}]");
            return list.Count;
        };
    }
}

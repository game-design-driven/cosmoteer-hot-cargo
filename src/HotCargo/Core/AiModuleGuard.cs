using System.Collections.Concurrent;
using System.Reflection;
using Cosmoteer.Ships.AI;
using HarmonyLib;

namespace HotCargo.Core;

/// <summary>
/// Spec 17, Rules: the AI modules of the mod (strategy and tactics modules in every area) run inside the Errors.Guard rules. Vanilla
/// calls them from ShipAI.FixedUpdate with no catch, so a throw ended the game. A module that throws reports once, returns false
/// (the next module of the ship, often a vanilla one, decides) and rests like a failed sim step.
/// </summary>
[HarmonyPatch]
static class AiModuleGuard
{
    static readonly ConcurrentDictionary<MethodBase, string> Places = new();

    /// <summary>Every override in this assembly of an abstract bool method of AIStrategyModule or AITacticsModule (the per-tick calls).</summary>
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var baseType in new[] { typeof(AIStrategyModule), typeof(AITacticsModule) })
        {
            var calls = baseType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.IsAbstract && m.ReturnType == typeof(bool)).ToList();
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(AiModuleGuard).Assembly).Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t)))
                foreach (var call in calls)
                    if (type.GetMethod(call.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, [.. call.GetParameters().Select(p => p.ParameterType)]) is { } m)
                        yield return m;
        }
    }

    static string Place(MethodBase m) => Places.GetOrAdd(m, static m => $"ai module {m.DeclaringType?.Name}.{m.Name}");

    [HarmonyPriority(Priority.First)]
    static bool Prefix(MethodBase __originalMethod, ref bool __result)
    {
        if (!Errors.Resting(Place(__originalMethod))) return true;
        __result = false;
        return false;
    }

    static Exception? Finalizer(Exception? __exception, MethodBase __originalMethod, ref bool __result)
    {
        if (__exception is null) return null;
        Errors.Failed(Place(__originalMethod), __exception);
        __result = false;
        return null;
    }
}

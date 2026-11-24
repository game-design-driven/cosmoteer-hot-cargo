using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using HarmonyLib;
using Halfling.Random;

namespace HotCargo.Missions;

/// <summary>
/// Spec 1897: a system generator group with the tag hc_own_stream draws from its own random stream, never from Sim.Rand. The game
/// generates a system from Sim.Rand and keeps using it afterwards (trade routes, offers, AI), so a new group that drew from it would move
/// every later roll of every system: other offers, stations, ships, fights. With its own stream, a new group changes only its own objects.
/// The stream comes from the system's seed, the group's tags and the sim time, so every co-op machine draws the same numbers.
/// Limit: two spawns of one group in the same tick (a test that forces it twice at once) would draw the same numbers. Revisit if that is ever
/// needed.
/// </summary>
[HarmonyPatch(typeof(SimObjectSpawner), "DoSpawn",
    [typeof(SimRoot), typeof(Rand), typeof(List<SimSpawner.SpawnedObject>), typeof(uint), typeof(uint), typeof(bool), typeof(List<SimSpawner.SpawnedObject>),
        typeof(SpawnedObjectCollection), typeof(HashSet<ID<SimSpawner.SpawnFlag>>), typeof(float)],
    [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Normal,
        ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal])]
static class OwnRandomStream
{
    static readonly ID<SimObjectSpawner> Tag = new("hc_own_stream");

    /// <summary>
    /// Gives the group its own stream and saves the state of Sim.Rand. Code inside the group can still draw from Sim.Rand directly (a ship
    /// that joins the sim, its AI), so the postfix puts the saved state back.
    /// </summary>
    static void Prefix(SimObjectSpawner __instance, SimRoot sim, ref Rand rand, out (bool On, ulong A, ulong B) __state)
    {
        __state = default;
        if (__instance.Tags?.Contains(Tag) != true) return;
        if (sim.Mode is not CareerSimModeManager career) throw new InvalidOperationException("hc_own_stream groups belong to career system generation");
        string name = string.Join(",", __instance.Tags.Select(t => t.ToString()).Order(StringComparer.Ordinal));
        rand = new FastRand(career.MapNodeData.RandomSeed ^ StableHash(name), (ulong)Math.Round(sim.LogicalTime.Seconds * 1000));
        sim.Rand.GetState(out ulong a, out ulong b);
        __state = (true, a, b);
    }

    static void Postfix(SimRoot sim, (bool On, ulong A, ulong B) __state)
    {
        if (__state.On) sim.Rand.SetState(__state.A, __state.B);
    }

    /// <summary>FNV-1a over the characters: the same on every machine and run (string.GetHashCode is not).</summary>
    static ulong StableHash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) h = (h ^ c) * 1099511628211UL;
        return h;
    }
}

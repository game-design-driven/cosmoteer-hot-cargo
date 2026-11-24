using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Factions;

/// <summary>Factions area state in the run save. The faction roles come from the galaxy seed, so they need no state.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FactionsState
{
    /// <summary>Spec 664: systems under corporate watch, by map node ID. A Strike stage marks the neighbors of its system.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> WatchedNodes = new();
    /// <summary>Spec 665: factions that shelter the players because a story chain at one of their stations ended well, by faction ID.</summary>
    [Serialize(Optional = true)] public HashSet<string> ChainShelters = new();
}

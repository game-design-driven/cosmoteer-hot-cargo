using Halfling.Serialization;
using HotCargo.Factions;

namespace HotCargo.Core;

// Factions's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public FactionsTunables Factions = new();
}

public sealed partial class RunState
{
    /// <summary>Factions area state: watched systems and chain shelters (spec 664, 665).</summary>
    [Serialize(Optional = true)] public Factions.FactionsState Factions = new();
}

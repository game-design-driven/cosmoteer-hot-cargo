using Halfling.Serialization;
using HotCargo.Tactics;

namespace HotCargo.Core;

// Tactics's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public TacticsTunables Tactics = new();
}

public sealed partial class RunState
{
    /// <summary>Co-op: the ships of all human players pool construction resources like one player's ships (spec 1318).</summary>
    [Serialize(Optional = true)] public bool SharedResources;
}

using Halfling.Serialization;
using HotCargo.Breach;

namespace HotCargo.Core;

// Breach's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public BreachTunables Breach = new();
}

public sealed partial class RunState
{
    /// <summary>Crew breach (spec 1610 to 1617).</summary>
    [Serialize(Optional = true)] public BreachState Breach = new();
}

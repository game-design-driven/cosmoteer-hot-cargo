using Halfling.Serialization;
using HotCargo.Thieves;

namespace HotCargo.Core;

// Thieves' parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public ThievesTunables Thieves = new();
}

public sealed partial class RunState
{
    /// <summary>Pirate thieves (spec 1770 to 1799).</summary>
    [Serialize(Optional = true)] public ThievesState Thieves = new();
}

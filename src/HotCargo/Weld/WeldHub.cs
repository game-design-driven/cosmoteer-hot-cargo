using Halfling.Serialization;
using HotCargo.Weld;

namespace HotCargo.Core;

// Weld's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public WeldTunables Weld = new();
}

public sealed partial class RunState
{
    /// <summary>Weld orders (spec 2484, 2491).</summary>
    [Serialize(Optional = true)] public WeldState Weld = new();
}

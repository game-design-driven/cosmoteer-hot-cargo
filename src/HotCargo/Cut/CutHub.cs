using Halfling.Serialization;
using HotCargo.Cut;

namespace HotCargo.Core;

// Cut's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public CutTunables Cut = new();
}

public sealed partial class RunState
{
    /// <summary>Cut orders (spec 2463, 2469).</summary>
    [Serialize(Optional = true)] public CutState Cut = new();
}

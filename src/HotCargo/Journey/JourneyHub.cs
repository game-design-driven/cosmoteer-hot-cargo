using Halfling.Serialization;
using HotCargo.Journey;

namespace HotCargo.Core;

// Journey's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public JourneyTunables Journey = new();
}

public sealed partial class RunState
{
    /// <summary>The run map and its lockdown line (spec 2557 to 2579).</summary>
    [Serialize(Optional = true)] public JourneyState Journey = new();
}

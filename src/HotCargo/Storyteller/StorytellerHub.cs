using Halfling.Serialization;
using HotCargo.Storyteller;

namespace HotCargo.Core;

// The storyteller's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public StorytellerTunables Storyteller = new();
}

public sealed partial class RunState
{
    [Serialize(Optional = true)] public StorytellerState Storyteller = new();
}

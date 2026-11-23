using Halfling.Serialization;
using HotCargo.Ai;

namespace HotCargo.Core;

// The enemy AI's part of the shared RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class RunState
{
    [Serialize(Optional = true)] public AiReportsState AiReports = new();
}

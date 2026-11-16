using Halfling.Serialization;

namespace HotCargo.Breach;

/// <summary>Crew breach (spec 1610 to 1617). Live reload.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class BreachTunables
{
    /// <summary>Off: crew with no way into their ship float outside, as in vanilla.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 1610: game seconds that a crew member waits outside its home ship with no working airlock before it starts to cut.</summary>
    [Serialize(Optional = true)] public float WaitSeconds = 10f;
    /// <summary>Spec 1611: game seconds that the cut takes.</summary>
    [Serialize(Optional = true)] public float CutSeconds = 5f;
    /// <summary>Spec 1610: a crew member counts as waiting only within this distance of its home ship's hull.</summary>
    [Serialize(Optional = true)] public float Range = 100f;
    /// <summary>Spec 1612: the breach sites nearest to the crew member that the cut tries.</summary>
    [Serialize(Optional = true)] public int MaxSites = 12;
}

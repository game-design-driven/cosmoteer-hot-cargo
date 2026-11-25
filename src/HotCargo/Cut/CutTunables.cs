using Halfling.Serialization;

namespace HotCargo.Cut;

/// <summary>Cut ships apart (spec 2460 to 2472). Live reload.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class CutTunables
{
    /// <summary>Spec 2471: off hides the Cut button and stops the work on cut orders.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 2464: seconds of crew work that one edge of a cut line takes.</summary>
    [Serialize(Optional = true)] public float EdgeSeconds = 3f;
    /// <summary>Spec 2465: speed at which the two pieces drift apart at the separation. 0: they keep the speed of the old ship.</summary>
    [Serialize(Optional = true)] public float SeparationSpeed = 2f;
}

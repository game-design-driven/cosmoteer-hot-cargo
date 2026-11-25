using Halfling.Serialization;

namespace HotCargo.Weld;

/// <summary>Weld (spec 2480 to 2494). Live reload.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class WeldTunables
{
    /// <summary>Spec 2493: off hides the Weld button and stops the weld orders.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 2481: how far from the ship's hull a target may be.</summary>
    [Serialize(Optional = true)] public float Range = 40f;
    /// <summary>Spec 2485: the top speed of the pull, relative to the ship.</summary>
    [Serialize(Optional = true)] public float PullSpeed = 4f;
    /// <summary>Spec 2485: the top turn rate of the pull, in degrees per second, relative to the ship.</summary>
    [Serialize(Optional = true)] public float TurnSpeed = 45f;
    /// <summary>Spec 2485: game seconds that the pull may take before the order ends.</summary>
    [Serialize(Optional = true)] public float PullSeconds = 30f;
    /// <summary>Spec 2486: seconds of crew work that one joint edge takes.</summary>
    [Serialize(Optional = true)] public float EdgeSeconds = 3f;
}

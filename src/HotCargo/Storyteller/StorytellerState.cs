using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Storyteller;

/// <summary>Saved storyteller state (spec 1721 to 1723). The adapt minutes last the run; the loss baselines belong to the system visit.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class StorytellerState
{
    /// <summary>Spec 1721: the adapt minutes of the run.</summary>
    [Serialize(Optional = true)] public float AdaptMinutes;
    /// <summary>Run game seconds of the last update.</summary>
    [Serialize(Optional = true)] public double LastUpdate = -1;
    /// <summary>Spec 1723: the human ships of the system at the last update.</summary>
    [Serialize(Optional = true)] public List<ObjectID> HumanShips = new();
    /// <summary>Spec 1723: the vanilla killed crew count of the human ships at the last update.</summary>
    [Serialize(Optional = true)] public int KilledCrew;
    /// <summary>Spec 1730: losses of this system visit, for the telemetry of the next system entry.</summary>
    [Serialize(Optional = true)] public int VisitShipsLost;
    [Serialize(Optional = true)] public int VisitCrewKilled;
}

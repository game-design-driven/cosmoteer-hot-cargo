using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Journey;

[ReflectiveSerialization(Explicit = true)]
public sealed class JourneyState
{
    /// <summary>Spec 2561: the warning state of each choice of the players' system, by system. A system switch clears it.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, ChoiceRecord> Choices = new();
    /// <summary>Spec 2560: game time when the hunters of a closed system attack, or -1.</summary>
    [Serialize(Optional = true)] public double ClosedWaveAt = -1;
}

/// <summary>The saved form of ChoiceState (LineModel).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ChoiceRecord
{
    [Serialize] public int Warned;
    [Serialize] public double GraceUntil = -1;
    [Serialize] public bool Closed;

    public ChoiceState Value => new(Warned, GraceUntil, Closed);

    public static ChoiceRecord From(ChoiceState s) => new() { Warned = s.Warned, GraceUntil = s.GraceUntil, Closed = s.Closed };
}

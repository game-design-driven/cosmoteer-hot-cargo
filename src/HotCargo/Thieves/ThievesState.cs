using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Thieves;

public enum ThiefPhase { Fight, Collect, Flee }

/// <summary>Sim state of the pirate thieves. Saved with the run, so a load and a co-op resync keep it.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ThievesState
{
    /// <summary>The thieves in the current system, by ship ID.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, ThiefState> Thieves = new();
    /// <summary>Spec 1780: pirate groups (player indexes) whose join roll ran in this system.</summary>
    [Serialize(Optional = true)] public HashSet<int> Rolled = new();
    /// <summary>Spec 1782: the cargo value of each fleeing thief at the last check, for the end event once it jumped out.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, long> EscapingCargo = new();
    /// <summary>Spec 1782: thieves that escaped and that died in this run.</summary>
    [Serialize(Optional = true)] public int Escaped, Died;
    /// <summary>Spec 1781: run game seconds of the encounter roll, or -1 when none is due.</summary>
    [Serialize(Optional = true)] public double EncounterDue = -1;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class ThiefState
{
    /// <summary>The pirate group: its player index. A trigger stops the whole group.</summary>
    [Serialize] public int Group;
    [Serialize] public int Tier;
    [Serialize] public ThiefPhase Phase;
    /// <summary>Run game seconds at which the phase started.</summary>
    [Serialize] public double Since;
}

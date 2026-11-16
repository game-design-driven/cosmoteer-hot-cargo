using Cosmoteer.Game;
using Halfling.Geometry;
using Halfling.Serialization;

namespace HotCargo.Breach;

/// <summary>Sim state of the crew breach. Saved with the run, so a load and a co-op resync keep the timers.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class BreachState
{
    /// <summary>Spec 1610: run game seconds since which each crew member (by soul ID) waits outside its home ship.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, double> Stuck = new();
    /// <summary>Spec 1611: the cuts in progress, in start order.</summary>
    [Serialize(Optional = true)] public List<BreachCut> Cuts = [];
    /// <summary>Breaches made in this run.</summary>
    [Serialize(Optional = true)] public int Made;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class BreachCut
{
    [Serialize] public ObjectID Crew;
    [Serialize] public ObjectID Ship;
    /// <summary>The breach cell and the walkable cell behind it, in the ship's cells.</summary>
    [Serialize] public IntVector2 Cell;
    [Serialize] public IntVector2 Walk;
    /// <summary>Run game seconds at which the breach opens.</summary>
    [Serialize] public double Due;
}

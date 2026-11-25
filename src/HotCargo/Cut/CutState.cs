using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Cut;

/// <summary>Sim state of the cut. Saved with the run, so a load and a co-op resync keep the orders and their work (spec 2469).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class CutState
{
    /// <summary>Spec 2463: the orders, at most one for each ship, in order of the MP inputs.</summary>
    [Serialize(Optional = true)] public List<CutOrder> Orders = [];
    /// <summary>Ships that a cut split in this run.</summary>
    [Serialize(Optional = true)] public int Splits;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class CutOrder
{
    [Serialize] public ObjectID Ship;
    /// <summary>Every edge of the line, in line order. The split uses all of them as gaps.</summary>
    [Serialize] public List<CutEdge> Edges = [];
    /// <summary>Spec 2475: a cut made in blueprint mode waits for the vanilla "Make it so". No crew work on it.</summary>
    [Serialize(Optional = true)] public bool Pending;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class CutEdge
{
    [Serialize] public int X;
    [Serialize] public int Y;
    [Serialize] public bool Down;
    /// <summary>Spec 2464: seconds of crew work done on the edge.</summary>
    [Serialize] public float Work;
    /// <summary>Spec 2467: true when the work is done or the edge stopped needing work.</summary>
    [Serialize] public bool Done;
    /// <summary>The tile next to the edge where the crew member stands (spec 2464), set when the order starts.</summary>
    [Serialize] public int WorkX;
    [Serialize] public int WorkY;

    /// <summary>The crew job of the edge. Not saved: the tick creates it again after a load (spec 2469).</summary>
    internal CutJob? Job;

    public Edge Edge => new(X, Y, Down);
    public Cell WorkCell => new(WorkX, WorkY);
}

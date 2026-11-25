using Cosmoteer.Game;
using Halfling.Serialization;
using HotCargo.Cut;

namespace HotCargo.Weld;

/// <summary>Sim state of the weld. Saved with the run, so a load and a co-op resync keep the orders, the pull and the work (spec 2491).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class WeldState
{
    /// <summary>Spec 2484: the orders, at most one for each ship, in order of the MP inputs.</summary>
    [Serialize(Optional = true)] public List<WeldOrder> Orders = [];
    /// <summary>Targets that a weld joined to a ship in this run.</summary>
    [Serialize(Optional = true)] public int Merges;
}

public enum WeldPhase { Pull, Join }

[ReflectiveSerialization(Explicit = true)]
public sealed class WeldOrder
{
    [Serialize] public ObjectID Ship;
    [Serialize] public ObjectID Target;
    [Serialize] public int Turns;
    [Serialize] public int X;
    [Serialize] public int Y;
    [Serialize] public WeldPhase Phase = WeldPhase.Pull;
    /// <summary>Spec 2485: run game seconds at which the pull started.</summary>
    [Serialize] public double PullSince;
    /// <summary>Spec 2486: the joint edges in the ship's grid, set when the target holds at its place.</summary>
    [Serialize] public List<CutEdge> Joints = [];
    /// <summary>Spec 2475, 2484: a weld made in blueprint mode waits for the vanilla "Make it so". No pull and no crew work.</summary>
    [Serialize(Optional = true)] public bool Pending;

    public Placement Placement => new(Turns, X, Y);
}

using Halfling.Serialization;

namespace HotCargo.Autonomy;

/// <summary>Autonomous ships (spec 1050 to 1099): Funnels, mining and trade of ships that run on their AI rows.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class AutonomyTunables
{
    /// <summary>Game seconds between two Funnel pickups and two checks of the Computer-manned parts.</summary>
    [Serialize(Optional = true)] public float TickSeconds = 0.25f;
    /// <summary>A Funnel takes in loot up to this many tiles outside its own tiles.</summary>
    [Serialize(Optional = true)] public float FunnelReach = 1f;
    /// <summary>Mine and salvage: the gap that the mining laser (or, without one, the hull) keeps outside the target's bounding circle, beside the target part.</summary>
    [Serialize(Optional = true)] public float MineDistance = 80f;
    /// <summary>Mine and salvage: the laser stands at most this share of its range from the target part.</summary>
    [Serialize(Optional = true)] public float MineReach = 0.8f;
    /// <summary>Mine and salvage: at most this many open salvage orders on the target at a time.</summary>
    [Serialize(Optional = true)] public int SalvageJobs = 4;
    /// <summary>Buy never spends the owner's money below this amount.</summary>
    [Serialize(Optional = true)] public int BuyReserve = 5000;
    /// <summary>Go to station: the ship stops this far from the station hull, inside the vanilla trade distance (400).</summary>
    [Serialize(Optional = true)] public float DockDistance = 150f;
    /// <summary>
    /// Spec 1083: a player ship collects no loot and goes to no station when the loot, the station or the straight path there comes this
    /// close to the hull of an enemy on radar, edge to edge. 0 turns it off.
    /// </summary>
    /// <summary>Spec 1087: build ship puts the new ship this far from the builder's hull, edge to edge of their circles.</summary>
    [Serialize(Optional = true)] public float BuildGap = 100f;
    [Serialize(Optional = true)] public float EnemyClearance = ShipAi.ShipAiModel.EnemyClearanceDefault;
}

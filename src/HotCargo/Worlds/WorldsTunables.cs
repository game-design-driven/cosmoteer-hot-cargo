using Halfling.Serialization;

namespace HotCargo.Worlds;

/// <summary>
/// Numbers of the system archetypes (WORLDS.md, spec 220 to 229). The archetype weights apply when the galaxy is made, once
/// per run (Journey/MapRules.cs writes them into the rules spawner of mod/worlds/archetypes.rules, spec 1983).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class WorldsTunables
{
    /// <summary>Spec 221: the speed of the current along its flow, in world units per second. Ships slower than this downstream get pushed.</summary>
    [Serialize(Optional = true)] public float CurrentFlowSpeed = 45f;
    /// <summary>Spec 221: the push of the current on a ship at rest, in world units per second squared, at full cloud strength.</summary>
    [Serialize(Optional = true)] public float CurrentAccel = 9f;
    /// <summary>
    /// Spec 221: the sideways part of the drift, as a share of the flow. The current also pushes across the flow, toward the
    /// nearer side of the band, at this share of the flow speed and push, so a drifting ship or item leaves the band. 0 turns it off.
    /// Drift leaves at the side when the share is above (distance to the side) ÷ (distance to the end). From the band
    /// center that is 1200 ÷ 3000 = 0.4; below it, drift from the center leaves at the far end.
    /// </summary>
    [Serialize(Optional = true)] public float CurrentSideShare = 0.25f;
    /// <summary>Spec 221: draw flow arrows over the current.</summary>
    [Serialize(Optional = true)] public bool CurrentArrows = true;

    /// <summary>Spec 222: seconds that a player ship must hold the pass, after the campers are gone.</summary>
    [Serialize(Optional = true)] public float PassHoldSeconds = 120f;
    /// <summary>Spec 222: seconds the pass may stay empty after the hold started, before the mission fails.</summary>
    [Serialize(Optional = true)] public float PassLostSeconds = 15f;
    /// <summary>Spec 222: seconds of holding between raider waves. The first wave comes after one interval.</summary>
    [Serialize(Optional = true)] public float PassWaveSeconds = 35f;
    /// <summary>Spec 222: distance from the pass center at which a wave appears.</summary>
    [Serialize(Optional = true)] public float PassWaveDistance = 2500f;

    /// <summary>Spec 223: seconds a player ship must stay at the sundive wreck.</summary>
    [Serialize(Optional = true)] public float SundiveSalvageSeconds = 5f;
    /// <summary>Spec 223: heat zone depth (0 = edge, 1 = kill radius) above which the HUD line turns red.</summary>
    [Serialize(Optional = true)] public float HeatWarnDepth = 0.5f;

    /// <summary>Spec 224: a player ship this close to the lair finds it.</summary>
    [Serialize(Optional = true)] public float LairDiscoverDistance = 2000f;

    /// <summary>Spec 225: the chat line that names the system type and its hazard on the first arrival.</summary>
    [Serialize(Optional = true)] public bool ArrivalLine = true;
    /// <summary>Spec 225: seconds after the jump before the arrival line shows. The jump shows the system name first.</summary>
    [Serialize(Optional = true)] public float ArrivalLineDelaySeconds = 4f;
    /// <summary>
    /// Spec 220, 1983: the chance of each system type when the galaxy is made: its weight ÷ the sum of the weights that the system's
    /// tier allows (MinTier in mod/worlds/archetypes.rules). A type that this list leaves out does not appear. New runs only; in
    /// co-op the galaxy uses the defaults. With the defaults, about 3 of 4 systems are archetypes.
    /// </summary>
    [Serialize(Optional = true)] public SystemTypeWeight[] SystemTypeWeights =
    [
        new() { Type = "hc_trade_hub", Weight = 1f },
        new() { Type = "hc_mining_belt", Weight = 1f },
        new() { Type = "hc_solar_zone", Weight = 0.8f },
        new() { Type = "hc_deep_space", Weight = 0.8f },
        new() { Type = "basic", Weight = 0.5f },
        new() { Type = "binary", Weight = 0.25f },
        new() { Type = "high_density_nebula", Weight = 0.2f },
        new() { Type = "supernova", Weight = 0.1f },
        new() { Type = "electron", Weight = 0.15f },
    ];
}

[ReflectiveSerialization(Explicit = true)]
public sealed class SystemTypeWeight
{
    /// <summary>A sector type ID of mod/worlds/archetypes.rules, for example hc_trade_hub or basic.</summary>
    [Serialize] public string Type = "";
    /// <summary>The weight of the type. 0: it does not appear.</summary>
    [Serialize(Optional = true)] public float Weight = 1f;
}

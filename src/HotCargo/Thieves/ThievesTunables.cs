using Halfling.Serialization;

namespace HotCargo.Thieves;

/// <summary>Pirate thieves (spec 1770 to 1799). Live reload.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ThievesTunables
{
    /// <summary>
    /// Off: no thief joins a group or arrives. Thieves already in the system keep their behavior. The default is off in autotest games
    /// (like the Pursuit, spec 943), so random thieves never change other game tests; a test turns it on with th_set Enabled true.
    /// </summary>
    [Serialize(Optional = true)] public bool Enabled = !Dev.Autotest.IsActive;
    /// <summary>Spec 1780: chance that a pirate group, the first time it is live, gets a thief at its side.</summary>
    [Serialize(Optional = true)] public float JoinChance = 0.25f;
    /// <summary>Spec 1781: chance, on the first visit of a system from MinSector on, of a thief-only encounter (paced, kind Thieves).</summary>
    [Serialize(Optional = true)] public float EncounterChance = 0.15f;
    [Serialize(Optional = true)] public int MinSector = 2;
    /// <summary>Spec 1781: game seconds after the arrival before the encounter roll.</summary>
    [Serialize(Optional = true)] public float EncounterDelaySeconds = 90f;
    /// <summary>Spec 1781: thieves in an encounter, before the co-op scale.</summary>
    [Serialize(Optional = true)] public int EncounterMin = 2;
    [Serialize(Optional = true)] public int EncounterMax = 3;
    /// <summary>Spec 1781: the encounter arrives this far from the players' first ship.</summary>
    [Serialize(Optional = true)] public float EncounterDistanceMin = 3000f;
    [Serialize(Optional = true)] public float EncounterDistanceMax = 5000f;
    /// <summary>Spec 1778: loose resources within this distance of a thief's hull count for its group, and it collects within it.</summary>
    [Serialize(Optional = true)] public float LootRadius = 2000f;
    /// <summary>Spec 1778: a group stops fighting when the player drops near it are worth this share of its tier's value maximum.</summary>
    [Serialize(Optional = true)] public float TriggerShare = 0.04f;
    /// <summary>Spec 1779: the most game seconds a thief collects before it flees.</summary>
    [Serialize(Optional = true)] public float CollectSeconds = 60f;
    /// <summary>Spec 1779: game seconds that a thief flies away before it jumps out.</summary>
    [Serialize(Optional = true)] public float FleeSeconds = 12f;
    /// <summary>Spec 1776: the guns keep their part unless another part of the same rank is this many times nearer.</summary>
    [Serialize(Optional = true)] public float SwitchFactor = 2f;
}

using Halfling.Serialization;

namespace HotCargo.Core;

/// <summary>
/// All numbers that the C# code reads. Loaded from tunables.rules in the mod folder.
/// Each area owns its section class in its own folder.
/// Missing fields keep the defaults written here.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed partial class Tunables
{
    [Serialize(Optional = true)] public HeatTunables Heat = new();
    [Serialize(Optional = true)] public DebugTunables Debug = new();
}

/// <summary>How fast heat rises, the tech multiplier, the carry-over on a jump and the stage thresholds.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HeatTunables
{
    /// <summary>Spec 24: heat per game minute at multiplier 1. About a quarter of a sector's heat comes from time (BALANCE.md).</summary>
    [Serialize(Optional = true)] public float HeatPerGameMinute = 1.14f;
    /// <summary>
    /// Spec 25: heat per standard mission fame unit at multiplier 1. A typical mission pays about 3.4 units (kill fame plus reward fame),
    /// so it adds about 5.8 heat, and about six missions with the time and contract heat fill a sector.
    /// </summary>
    [Serialize(Optional = true)] public float HeatPerStandardMission = 1.425f;
    /// <summary>Heat multiplier for each fleet tech count, from 0 units up. A count above the table uses the last entry.</summary>
    [Serialize(Optional = true)] public float[] HeatMultiplierByUnits = [0.25f, 0.4f, 0.52f, 0.64f, 0.76f, 0.88f, 1.0f, 1.1f, 1.2f];
    /// <summary>Share of the heat that stays after a switch to a system where own ships wait, without a jump (0 to 1). Jumps use the Factions heat cuts.</summary>
    [Serialize(Optional = true)] public float HeatCarryOverFraction = 0.25f;
    /// <summary>
    /// Heat stages, ordered by Heat. Hunters.Stages has the response for each stage, matched by Name. The last stage repeats its waves.
    /// Warning is above the highest heat that a jump carries in (25), so its scouts always come after arrival. Hunt starts with the
    /// station markup (Factions.MarkupStartHeat).
    /// </summary>
    [Serialize(Optional = true)] public StageThreshold[] Stages =
    [
        new() { Name = "Warning", Heat = 40f },
        new() { Name = "Hunt", Heat = 75f },
        new() { Name = "Strike", Heat = 100f },
    ];

    // ---- Paid heat cuts (spec 2230): every comms choice that sells a heat cut for money shares one factor.
    /// <summary>
    /// Spec 2230: each recent paid heat cut makes the next one weaker: trust = 1 / (1 + recent buys × this). 0.5: the 2nd cut gives 67 %,
    /// the 3rd 50 %, the 4th 40 %.
    /// </summary>
    [Serialize(Optional = true)] public float HeatBuyFalloff = 0.5f;
    /// <summary>Spec 2230: game minutes in which the count of recent paid heat cuts halves (word of the buys fades).</summary>
    [Serialize(Optional = true)] public float HeatBuyHalfLifeMinutes = 20f;
    /// <summary>Spec 2230: a paid heat cut works fully at this heat or more, and less below it (heat ÷ this): a cold trail needs no cleaning.</summary>
    [Serialize(Optional = true)] public float HeatBuyFullHeat = 60f;
    /// <summary>Spec 2230: paid heat cuts close below this factor (trust × heat share): nobody sells a clean trail to a captain who bought too many.</summary>
    [Serialize(Optional = true)] public float HeatBuyCloseBelow = 0.25f;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class StageThreshold
{
    /// <summary>Stage name. Hunters.Stages and the tactic minimum stages refer to it.</summary>
    [Serialize] public string Name = "";
    /// <summary>Heat at which the stage starts (0 to 100).</summary>
    [Serialize] public float Heat;
}

/// <summary>Debug keys and test logs.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DebugTunables
{
    /// <summary>
    /// Single player only. F9 / Shift+F9 add or remove 10 heat, F10 starts the next stage, F11 / Shift+F11 add or remove 1 tech.
    /// With debug keys on, the heat panel also shows the heat value, the multiplier and the tunables hash.
    /// </summary>
    [Serialize(Optional = true)] public bool DebugKeys;
    /// <summary>Writes each hunter tactic decision to hot_cargo_log.txt. For tests and tuning.</summary>
    [Serialize(Optional = true)] public bool TacticsLog;
    /// <summary>
    /// After an error inside a game rule, that rule pauses for this many game seconds, then tries again. This stops a broken rule from
    /// running half-way many times a second. 0: no pause.
    /// </summary>
    [Serialize(Optional = true)] public float FailedStepRestSeconds = 60f;
}

public static class HeatTunablesExtensions
{
    public static float[] Thresholds(this HeatTunables t) => t.Stages.Select(s => s.Heat).ToArray();
}

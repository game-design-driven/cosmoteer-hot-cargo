using Halfling.Serialization;

namespace HotCargo.Storyteller;

/// <summary>The storyteller (spec 1720 to 1731): hunter pressure adapts to the run's losses.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class StorytellerTunables
{
    /// <summary>
    /// Spec 1720. Off: the pressure is 1. The default is off in autotest games, like the Pursuit (spec 943); a test turns it on with
    /// story_set Enabled true.
    /// </summary>
    [Serialize(Optional = true)] public bool Enabled = !Dev.Autotest.IsActive;
    /// <summary>Spec 1732: the pressure also scales the fights that are not hunter waves (rules spawns, mechanics waves, thieves, police).</summary>
    [Serialize(Optional = true)] public bool AllFightsEnabled = true;
    /// <summary>Spec 1721, 1724: the lowest adapt minutes; the pressure there is PressureMin.</summary>
    [Serialize(Optional = true)] public float AdaptMin = -30f;
    /// <summary>Spec 1721, 1724: the highest adapt minutes; the pressure there is PressureMax.</summary>
    [Serialize(Optional = true)] public float AdaptMax = 60f;
    /// <summary>Spec 1724: the pressure at AdaptMin.</summary>
    [Serialize(Optional = true)] public float PressureMin = 0.85f;
    /// <summary>Spec 1724: the pressure at AdaptMax.</summary>
    [Serialize(Optional = true)] public float PressureMax = 1.25f;
    /// <summary>Spec 1722: adapt minutes added per game minute (not above 0 in sector 1).</summary>
    [Serialize(Optional = true)] public float AdaptPerMinute = 1f;
    /// <summary>Spec 1723: adapt minutes taken for each lost human ship.</summary>
    [Serialize(Optional = true)] public float AdaptLossShip = 20f;
    /// <summary>Spec 1723: adapt minutes taken for each killed crew member of a human ship.</summary>
    [Serialize(Optional = true)] public float AdaptLossCrew = 1f;
    /// <summary>Spec 1726: the fleet factor × the pressure stays at or above this.</summary>
    [Serialize(Optional = true)] public float CombinedMin = 0.75f;
    /// <summary>Spec 1726: the fleet factor × the pressure stays at or below this.</summary>
    [Serialize(Optional = true)] public float CombinedMax = 1.4f;
    /// <summary>Spec 1725: the impact for each vanilla combat difficulty level (0 to 1). A level without an entry counts as 1.</summary>
    [Serialize(Optional = true)] public AdaptImpact[] Impact =
    [
        new() { Level = "no_death", Impact = 1f },
        new() { Level = "easy", Impact = 1f },
        new() { Level = "normal", Impact = 0.9f },
        new() { Level = "hard", Impact = 0.7f },
        new() { Level = "very_hard", Impact = 0.5f },
    ];

    public PressureCurve Curve => new(AdaptMin, AdaptMax, PressureMin, PressureMax);

    /// <summary>Called from TunablesFile.Validate (spec 53). isCombatLevel is null while the game rules are not loaded.</summary>
    public void Validate(Action<bool, string> require, Func<string, bool>? isCombatLevel)
    {
        require(AdaptMin < 0 && AdaptMax > 0, "Storyteller.AdaptMin must be below 0 and AdaptMax above 0");
        require(PressureMin is > 0 and <= 1 && PressureMax >= 1, "Storyteller.PressureMin must be above 0 and at most 1, PressureMax 1 or more");
        require(AdaptPerMinute >= 0 && AdaptLossShip >= 0 && AdaptLossCrew >= 0, "Storyteller.AdaptPerMinute, AdaptLossShip and AdaptLossCrew must be 0 or more");
        require(CombinedMin > 0 && CombinedMax >= CombinedMin, "Storyteller.CombinedMin must be above 0 and CombinedMax at least CombinedMin");
        foreach (var i in Impact)
        {
            require(i.Impact is >= 0 and <= 1, $"Storyteller.Impact '{i.Level}' must be 0 to 1");
            if (isCombatLevel is not null) require(isCombatLevel(i.Level), $"Storyteller.Impact names '{i.Level}', which is not a vanilla combat difficulty level");
        }
    }
}

/// <summary>Spec 1725: the adaptation impact of one vanilla combat difficulty level.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class AdaptImpact
{
    [Serialize(Optional = true)] public string Level = "";
    [Serialize(Optional = true)] public float Impact = 1f;
}

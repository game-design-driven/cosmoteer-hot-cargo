using Halfling.Serialization;

namespace HotCargo.Friendly;

/// <summary>Friendly encounter numbers (spec 1360 and later). Event text, costs and weights are in mod/events/events.rules.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FriendlyTunables
{
    /// <summary>Game seconds between a deal and the jump-out of a ship that leaves after it (effect leave, spec 1364).</summary>
    [Serialize(Optional = true)] public float LeaveDelaySeconds = 5f;
    /// <summary>Riddle ship (spec 1366): heat keeps this share after a right answer.</summary>
    [Serialize(Optional = true)] public float RiddleHeatKeep = 0.5f;
    /// <summary>Debtor (spec 1367): game minutes that it follows the players before it pays back the debt.</summary>
    [Serialize(Optional = true)] public float DebtorMinutes = 30f;
    /// <summary>
    /// Pay it forward (spec 1370): chance per qualifying fight (the first time the players are clearly outclassed in a system) that the
    /// Wayfarers come. They come at most once per run.
    /// </summary>
    [Serialize(Optional = true)] public float PayForwardChance = 0.15f;
    /// <summary>Pay it forward: "clearly outclassed" means enemy strength near the players is at least this many times the players' strength.</summary>
    [Serialize(Optional = true)] public float PayForwardOutclassRatio = 2f;
    /// <summary>
    /// Pay it forward: an actual fight means a player ship fired, or a near enemy fired and the players lost health, within this many
    /// game seconds. Outclassed without a fight (enemies near, nobody shooting) does not roll.
    /// </summary>
    [Serialize(Optional = true)] public float PayForwardFightSeconds = 5f;
    /// <summary>Pay it forward: enemies within this distance of a player ship count for the strength check.</summary>
    [Serialize(Optional = true)] public float PayForwardRadius = 3000f;
    /// <summary>Pay it forward: game seconds between checks.</summary>
    [Serialize(Optional = true)] public float PayForwardCheckSeconds = 2f;
    /// <summary>Pay it forward: game seconds with no enemy near the players before the Wayfarers hail and repair.</summary>
    [Serialize(Optional = true)] public float PayForwardQuietSeconds = 10f;
    /// <summary>Pay it forward: game seconds after the hail before the Wayfarers leave without an answer.</summary>
    [Serialize(Optional = true)] public float PayForwardLeaveSeconds = 60f;
    /// <summary>Pay it forward: the longest the Wayfarers fight for the players, in game minutes (their hire time).</summary>
    [Serialize(Optional = true)] public float PayForwardFightMinutes = 10f;
    /// <summary>Apprentice (spec 1362): game minutes that it follows the players before it decides.</summary>
    [Serialize(Optional = true)] public float ApprenticeMinutes = 6f;
    /// <summary>Apprentice: enemy ships that must die near it while it follows, so it joins the fleet. Fewer: it thanks the players and leaves.</summary>
    [Serialize(Optional = true)] public int ApprenticeJoinFights = 3;
    /// <summary>Apprentice: an enemy ship that dies within this distance of the apprentice counts as a fight that it saw.</summary>
    [Serialize(Optional = true)] public float ApprenticeWatchRadius = 2500f;
    /// <summary>Troublemaker (spec 481): the pay for taking them aboard, in event money units (a quarter of a standard mission's pay).</summary>
    [Serialize(Optional = true)] public float TroublemakerPay = 0.25f;
    /// <summary>Troublemaker (spec 482): chance that they are a thief.</summary>
    [Serialize(Optional = true)] public float TroublemakerThiefChance = 0.3f;
    /// <summary>Troublemaker (spec 483): a thief steals this many times the pay.</summary>
    [Serialize(Optional = true)] public float TroublemakerTheftFactor = 2f;
    /// <summary>Troublemaker (spec 483): a station visit is their ship within this distance of a trade station's hull.</summary>
    [Serialize(Optional = true)] public float TroublemakerStationDistance = 300f;
    /// <summary>Scientist (spec 488): game minutes after joining until the research roll.</summary>
    [Serialize(Optional = true)] public float ScientistMinutes = 10f;
    /// <summary>Scientist (spec 488): chance of a research; else they defect (spec 489).</summary>
    [Serialize(Optional = true)] public float ScientistResearchChance = 0.75f;
    /// <summary>Scientist (spec 488): the research picks at random from this many cheapest techs that the players can research.</summary>
    [Serialize(Optional = true)] public int ScientistCheapestTechs = 5;
    /// <summary>Scientist (spec 489): a defector leaves for an AI ship within this distance of their ship's hull.</summary>
    [Serialize(Optional = true)] public float ScientistDefectRange = 500f;
    /// <summary>Scientist (spec 489): the most processors that a defector takes.</summary>
    [Serialize(Optional = true)] public int ScientistStealProcessors = 5;
    /// <summary>Partner (spec 491): game minutes after the scientist's death until the partner comes.</summary>
    [Serialize(Optional = true)] public float PartnerMinutes = 10f;
    /// <summary>Partner (spec 494): the partner yields below this health.</summary>
    [Serialize(Optional = true)] public float PartnerYieldHealth = 0.35f;
    /// <summary>Partner (spec 493): another player ship or ally that fires within this distance of the partner's hull breaks the duel.</summary>
    [Serialize(Optional = true)] public float PartnerInterferenceRange = 400f;
    /// <summary>Partner (spec 496): research chance of the partner who joins (nothing happens on a failed roll).</summary>
    [Serialize(Optional = true)] public float PartnerResearchChance = 0.8f;
    /// <summary>Partner (spec 495): game minutes after the duel breaks or is declined until the revenge fleet comes.</summary>
    [Serialize(Optional = true)] public float PartnerRevengeMinutes = 15f;
}

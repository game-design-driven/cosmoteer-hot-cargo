using Halfling.Serialization;

namespace HotCargo.Journey;

[ReflectiveSerialization(Explicit = true)]
public sealed class JourneyTunables
{
    /// <summary>
    /// Spec 2550: steps from the start to the Buyer's system, both counted. It sets the size of the run map, its width, its tier ramp
    /// and the speed of the lockdown line. New runs only (the galaxy is made once per run); in co-op the galaxy uses the default.
    /// </summary>
    [Serialize(Optional = true)] public int RunSectorCount = 6;
    /// <summary>
    /// Spec 4: mission money and fame × this, on top of the economy difficulty (Fast pays 2× money and 1.5× fame).
    /// 1 = vanilla rates. On Fast, six missions a sector pay about twice what a fleet of even power needs to keep up, and about three
    /// times with MissionMoneyScale (BALANCE.md, findings 1 and 11).
    /// </summary>
    [Serialize(Optional = true)] public float RewardScale = 1.0f;
    /// <summary>
    /// Spec 4: mission money (not fame) × this, on top of RewardScale. It makes up for the salvage cut (spec 1247): missions pay
    /// more, so moving on beats salvaging (BALANCE.md, finding 11). Fees, event money and tech prices do not change.
    /// </summary>
    [Serialize(Optional = true)] public float MissionMoneyScale = 1.5f;
    /// <summary>Spec 7: the Buyer pays tech value × this, and the score values unsold units the same way.</summary>
    [Serialize(Optional = true)] public float BuyerPriceFactor = 4f;
    /// <summary>Spec 301: blueprint prices × this, so that unlocks stay a choice in a short run. The faction reputation factor applies too.</summary>
    [Serialize(Optional = true)] public float TechCostScale = 2.5f;
    /// <summary>
    /// Spec 2552: the low tier of the Buyer's system. The tier rises in even steps from 1 at the start. New runs only (the galaxy is
    /// made once per run); in co-op the galaxy uses the default.
    /// </summary>
    [Serialize(Optional = true)] public int FinalSectorTier = 12;
    /// <summary>Spec 2557: the corporate lockdown line on the run map. Off: no system closes.</summary>
    [Serialize(Optional = true)] public bool LineEnabled = true;
    /// <summary>Spec 2561: game seconds before a choice closes (at the current heat rate) when the first warning comes.</summary>
    [Serialize(Optional = true)] public float LineFirstWarningSeconds = 300f;
    /// <summary>
    /// Spec 2561: game seconds before a choice closes when the last warning comes. A heat jump over the closing heat gives this warning
    /// then, and the choice closes this many seconds later.
    /// </summary>
    [Serialize(Optional = true)] public float LineLastWarningSeconds = 60f;
    /// <summary>Spec 2560: the heat stage response (Hunters.Stages) that waits in a closed system when the players jump there.</summary>
    [Serialize(Optional = true)] public string ClosedWaveStage = "Hunt";
    /// <summary>Spec 2560: game seconds after the arrival in a closed system before its hunters attack.</summary>
    [Serialize(Optional = true)] public float ClosedWaveDelaySeconds = 6f;
}

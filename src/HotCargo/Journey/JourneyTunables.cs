using Halfling.Serialization;

namespace HotCargo.Journey;

[ReflectiveSerialization(Explicit = true)]
public sealed class JourneyTunables
{
    /// <summary>Sector index at which the Buyer station appears. The short map rules size the galaxy for this.</summary>
    [Serialize(Optional = true)] public int RunSectorCount = 8;
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
    /// Spec 2414: the highest low tier of a normal system; the main route reaches it after about 6 jumps. Faction home systems sit
    /// TierSpread + 1 tiers higher. New runs only (the galaxy is made once per run); in co-op the galaxy uses the default.
    /// </summary>
    [Serialize(Optional = true)] public int FinalSectorTier = 12;
    /// <summary>Spec 2414: star systems in the run galaxy. New runs only; in co-op the galaxy uses the default.</summary>
    [Serialize(Optional = true)] public int MapSystems = 50;
}

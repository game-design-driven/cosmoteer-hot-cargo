using Halfling.Serialization;

namespace HotCargo.Diplomacy;

/// <summary>Hails and deals: hunter demands, surrenders, friendly ships that hold tech or fight for pay, mercenaries (spec 560 to 589).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DiplomacyTunables
{
    /// <summary>Turns every Hot Cargo hail option and hunter demand off. Vanilla comms stay.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;

    // ---- Shared situation

    /// <summary>Radius around the hailed ship that counts as its area: ships inside it count for strength and numbers.</summary>
    [Serialize(Optional = true)] public float AreaRadius = 3000f;
    /// <summary>Reputation that counts as full trust (+1) or full hate (-1) in every chance. The vanilla "trusted" title starts at 2500.</summary>
    [Serialize(Optional = true)] public float ReputationScale = 2500f;
    /// <summary>Game seconds between the sim checks for hails, mercenaries and held tech.</summary>
    [Serialize(Optional = true)] public float CheckSeconds = 1f;

    // ---- Hunter demands (spec 561 to 566)

    /// <summary>Chance that a hunter that first comes into comms range hails the players to demand the tech. One open demand at a time.</summary>
    [Serialize(Optional = true)] public float DemandHailChance = 0.8f;
    /// <summary>Game seconds after a hunter demand hail before another hunter can hail.</summary>
    [Serialize(Optional = true)] public float DemandHailGapSeconds = 60f;
    /// <summary>Game seconds after the hunters accept a pay-off or a bluff, or a hunter surrenders, before they jump out of the system.</summary>
    [Serialize(Optional = true)] public float HunterLeaveSeconds = 8f;
    /// <summary>Heat lost when the hunters leave after a pay-off or a bluff: they believe the tech is gone.</summary>
    [Serialize(Optional = true)] public float HandOverHeatDrop = 40f;

    // ---- Pay them off (spec 594 to 597). Chance = min(PayChanceCap, crowd × (tech part + money part + crew part)).

    /// <summary>Pay-off chance part of the tech row when it offers the whole stock. Scales with the share of the stock offered.</summary>
    [Serialize(Optional = true)] public float PayTechChance = 0.9f;
    /// <summary>Most money on the pay-off money slider, in event money units (a standard mission's pay).</summary>
    [Serialize(Optional = true)] public float PayMoneyMaxUnits = 2f;
    /// <summary>Pay-off chance part of the money row at the full slider. Linear from 0.</summary>
    [Serialize(Optional = true)] public float PayMoneyChance = 0.6f;
    /// <summary>Most crew that the players can send to cryo jail in a pay-off.</summary>
    [Serialize(Optional = true)] public int PayCrewMax = 6;
    /// <summary>Pay-off chance part for each crew member sent to cryo jail.</summary>
    [Serialize(Optional = true)] public float PayCrewChancePer = 0.08f;
    /// <summary>The pay-off chance never goes above this.</summary>
    [Serialize(Optional = true)] public float PayChanceCap = 0.95f;
    /// <summary>
    /// Other hunters near the hailed one make a pay-off harder: the offer counts × (1 − others ÷ (this + 1)), and nothing with more others
    /// than this. Scales with co-op enemy scaling.
    /// </summary>
    [Serialize(Optional = true)] public int PayMaxOtherHunters = 6;

    // ---- Bluff (spec 565)

    /// <summary>Share of the vault tech that a bluff hands over (at least 1 unit, never all).</summary>
    [Serialize(Optional = true)] public float BluffShare = 0.25f;
    /// <summary>Base chance that a bluff works ("That's all we have").</summary>
    [Serialize(Optional = true)] public float BluffChanceBase = 0.6f;
    /// <summary>Bluff chance added when the bluff hands over all units. Scales with the share handed over (BluffShare).</summary>
    [Serialize(Optional = true)] public float BluffPerShare = 0.2f;
    /// <summary>Bluff chance lost when the largest pile left is as big as the whole stock. Scales with that pile's share. Spread the tech to bluff.</summary>
    [Serialize(Optional = true)] public float BluffVisiblePenalty = 1.2f;
    /// <summary>Heat added when the hunters see through a bluff. They keep the units and fight.</summary>
    [Serialize(Optional = true)] public float BluffCaughtHeat = 10f;
    /// <summary>Tech vaults closer than this (world units, part center to part center) count as one pile.</summary>
    [Serialize(Optional = true)] public float PileDistance = 5f;

    // ---- Surrender (spec 567 to 573)

    /// <summary>Base chance that an enemy accepts the players' demand to surrender.</summary>
    [Serialize(Optional = true)] public float SurrenderChanceBase = 0.1f;
    /// <summary>Surrender chance per point of leverage (-1 to +1).</summary>
    [Serialize(Optional = true)] public float SurrenderPerLeverage = 0.35f;
    /// <summary>Surrender chance per lost health share of the enemy ship (0 to 1).</summary>
    [Serialize(Optional = true)] public float SurrenderPerDamage = 0.6f;
    /// <summary>Surrender chance at the top fame title. Scales with the title.</summary>
    [Serialize(Optional = true)] public float SurrenderPerFame = 0.15f;
    /// <summary>Surrender chance per point of reputation with the enemy's faction (-1 to +1).</summary>
    [Serialize(Optional = true)] public float SurrenderPerReputation = 0.1f;
    /// <summary>Surrender chance lost for each ally of the enemy in the area.</summary>
    [Serialize(Optional = true)] public float SurrenderPerAlly = 0.1f;
    /// <summary>Surrender chance lost when the enemy's faction wants the players.</summary>
    [Serialize(Optional = true)] public float SurrenderWantedPenalty = 0.3f;
    /// <summary>Hunters only: surrender chance lost at heat 100. Scales with heat.</summary>
    [Serialize(Optional = true)] public float HunterSurrenderPerHeat = 0.3f;
    /// <summary>Surrender payout times the vanilla payout (ship value × the AI's AISurrenderPayoutMultiple).</summary>
    [Serialize(Optional = true)] public float SurrenderPayoutScale = 1f;
    /// <summary>Extra payout share per point of positive leverage. A stronger fleet gets more.</summary>
    [Serialize(Optional = true)] public float SurrenderPayoutPerLeverage = 0.5f;
    /// <summary>Share of the payout that a surrendering corporate hunter pays, on top of all its fuel and the chance at cargo. 1: the usual payout.</summary>
    [Serialize(Optional = true)] public float HunterSurrenderMoneyShare = 1f;
    /// <summary>Chance that a surrendering ship also gives one other resource that it carries (only what fits).</summary>
    [Serialize(Optional = true)] public float ExtraLootChance = 0.5f;
    /// <summary>Share of that other resource that the ship gives.</summary>
    [Serialize(Optional = true)] public float ExtraLootShare = 0.5f;
    /// <summary>A hunter below this health may hail to offer its surrender.</summary>
    [Serialize(Optional = true)] public float HunterYieldHealth = 0.35f;
    /// <summary>Chance, rolled once per hunter when it drops below HunterYieldHealth, that it offers its surrender.</summary>
    [Serialize(Optional = true)] public float HunterYieldChance = 0.4f;
    /// <summary>Spec 2220: an AI ship offers its surrender only after this many game seconds in the fight with the players, or after it lost SurrenderMinDamage.</summary>
    [Serialize(Optional = true)] public float SurrenderMinFightSeconds = 45f;
    /// <summary>Spec 2220: an AI ship offers its surrender only after it lost this share of its health (0 to 1), or after SurrenderMinFightSeconds in the fight.</summary>
    [Serialize(Optional = true)] public float SurrenderMinDamage = 0.3f;
    /// <summary>Base chance that an enemy accepts the players' surrender.</summary>
    [Serialize(Optional = true)] public float OfferChanceBase = 0.5f;
    /// <summary>Chance added per point of the enemy's leverage (they are stronger).</summary>
    [Serialize(Optional = true)] public float OfferPerLeverage = 0.3f;
    /// <summary>Chance per point of reputation with the enemy's faction.</summary>
    [Serialize(Optional = true)] public float OfferPerReputation = 0.2f;
    /// <summary>Chance lost when the enemy's faction wants the players.</summary>
    [Serialize(Optional = true)] public float OfferWantedPenalty = 0.2f;
    /// <summary>Price of the players' surrender in event money units (a standard mission's pay at the sector tier).</summary>
    [Serialize(Optional = true)] public float OfferPriceUnits = 1f;
    /// <summary>Price factor when the enemy's faction wants the players.</summary>
    [Serialize(Optional = true)] public float OfferWantedPriceFactor = 1.5f;

    // ---- Friendly ships hold tech (spec 574 to 578)

    /// <summary>Base chance that a friendly ship agrees to hold tech.</summary>
    [Serialize(Optional = true)] public float HoldChanceBase = 0.7f;
    /// <summary>Hold chance per point of reputation with the ship's faction.</summary>
    [Serialize(Optional = true)] public float HoldPerReputation = 0.3f;
    /// <summary>Hold chance lost at heat 100. Scales with heat.</summary>
    [Serialize(Optional = true)] public float HoldPerHeat = 0.5f;
    /// <summary>Hold chance lost while any hunter is in the system.</summary>
    [Serialize(Optional = true)] public float HoldHuntersPenalty = 0.3f;
    /// <summary>Hold chance lost per unit asked.</summary>
    [Serialize(Optional = true)] public float HoldPerUnit = 0.03f;
    /// <summary>No ship holds tech while a hunter is this close to it.</summary>
    [Serialize(Optional = true)] public float HoldHunterRadius = 4000f;
    /// <summary>Base chance that the ship asks for a fee first.</summary>
    [Serialize(Optional = true)] public float FeeChanceBase = 0.3f;
    /// <summary>Fee chance added at heat 100.</summary>
    [Serialize(Optional = true)] public float FeePerHeat = 0.4f;
    /// <summary>Fee chance per point of reputation (good reputation lowers it).</summary>
    [Serialize(Optional = true)] public float FeePerReputation = -0.2f;
    /// <summary>Fee per unit held, times the tech value.</summary>
    [Serialize(Optional = true)] public float FeePerUnit = 0.1f;
    /// <summary>Base chance that the ship keeps the tech when the players ask for it back. Rolled at the hand-over, judged at the return.</summary>
    [Serialize(Optional = true)] public float BetrayChanceBase = 0.1f;
    /// <summary>Betrayal chance added at heat 100 at the time of the return.</summary>
    [Serialize(Optional = true)] public float BetrayPerHeat = 0.3f;
    /// <summary>Betrayal chance added per hunter in the system at the time of the return.</summary>
    [Serialize(Optional = true)] public float BetrayPerHunter = 0.1f;
    /// <summary>Betrayal chance per point of reputation (good reputation lowers it).</summary>
    [Serialize(Optional = true)] public float BetrayPerReputation = -0.15f;
    /// <summary>Betrayal chance change when the players paid a fee.</summary>
    [Serialize(Optional = true)] public float BetrayFeePaid = -0.05f;

    // ---- Hired help and mercenaries (spec 579 to 583)

    /// <summary>Spec 579: the sure hire price, in event money units: the most that the offer slider goes to. Before trust, heat and reputation, it always works.</summary>
    [Serialize(Optional = true)] public float HireSureUnits = 0.8f;
    /// <summary>Spec 579: the lowest offer, as a share of the sure price. Its chance is this share before trust, heat and reputation.</summary>
    [Serialize(Optional = true)] public float HireMinShare = 0.6f;
    /// <summary>Spec 579: hire chance factor per point of reputation with the ship's faction (-1 to 1): 1 + this × reputation.</summary>
    [Serialize(Optional = true)] public float HirePerReputation = 0.3f;
    /// <summary>Spec 579: share of the hire chance lost at heat 100: the factor is 1 − this × heat ÷ 100.</summary>
    [Serialize(Optional = true)] public float HirePerHeat = 0.4f;
    /// <summary>Spec 2224: trust falloff per paid hire this run (help and new mercenary contracts). Trust = 1 ÷ (1 + hires × this).</summary>
    [Serialize(Optional = true)] public float HireUseFalloff = 0.15f;
    /// <summary>Spec 2224: trust falloff per whole ship of health that hired ships lost this run. Trust × 1 ÷ (1 + ships × this).</summary>
    [Serialize(Optional = true)] public float HireLossFalloff = 0.5f;
    /// <summary>Spec 2225: the paid hires, the ships lost and the clean returns halve every this many game minutes. 0 keeps them for the run.</summary>
    [Serialize(Optional = true)] public float HireHalfLifeMinutes = 20f;
    /// <summary>Spec 2226: hire and mercenary prices fall by this share for each paid hire that came back unhurt.</summary>
    [Serialize(Optional = true)] public float HireCleanDiscount = 0.05f;
    /// <summary>Spec 2226: the most that clean returns take off hire and mercenary prices (0 to 0.2, user rule: at most 20 %).</summary>
    [Serialize(Optional = true)] public float HireMaxDiscount = 0.2f;
    /// <summary>Seconds that a hired ship helps: about one mission.</summary>
    [Serialize(Optional = true)] public float HireSeconds = 300f;
    /// <summary>A hired ship below this health stops helping and runs.</summary>
    [Serialize(Optional = true)] public float HiredFleeHealth = 0.5f;
    /// <summary>A hired ship attacks enemies of the players within this distance of its employer's nearest ship.</summary>
    [Serialize(Optional = true)] public float HelpRadius = 2500f;
    /// <summary>A hired ship with no target stays within this distance of its employer's nearest ship.</summary>
    [Serialize(Optional = true)] public float FollowDistance = 700f;
    /// <summary>A fleeing hired ship runs until the nearest enemy is this far away, then goes back to its own business.</summary>
    [Serialize(Optional = true)] public float HiredFleeDistance = 2000f;
    /// <summary>Chance that an armed friendly ship near a station is a mercenary. Rolled once per ship.</summary>
    [Serialize(Optional = true)] public float MercChance = 0.25f;
    /// <summary>A ship counts as near a station within this distance.</summary>
    [Serialize(Optional = true)] public float MercStationRadius = 3000f;
    /// <summary>Chance, rolled once per mercenary, that it hails the players to offer its services when they come into comms range.</summary>
    [Serialize(Optional = true)] public float MercHailChance = 0.5f;
    /// <summary>Mercenary price per minute as a share of the hire rate (HireSureUnits for HireSeconds), before trust. Below 1: mercenaries sell their time cheaper.</summary>
    [Serialize(Optional = true)] public float MercPriceShare = 0.7f;
    /// <summary>Most minutes that the slider offers for a mercenary contract.</summary>
    [Serialize(Optional = true)] public int MercMaxMinutes = 10;
    /// <summary>A mercenary below this health stops helping and runs. Lower than HiredFleeHealth: they are braver.</summary>
    [Serialize(Optional = true)] public float MercFleeHealth = 0.25f;

    // ---- Risk (spec 1520 to 1529)

    /// <summary>The risk model decides when enemies demand, flee and surrender. Off: vanilla decides demands and surrenders, flee rolls FleeChance.</summary>
    [Serialize(Optional = true)] public bool RiskEnabled = true;
    /// <summary>Allies of the same team within this distance of a ship can join its fight soon and count on its side.</summary>
    [Serialize(Optional = true)] public float RiskJoinRadius = 2000f;
    /// <summary>How much an empty crew weakens a ship: strength × (1 − this × the empty share of its crew places).</summary>
    [Serialize(Optional = true)] public float RiskCrewWeight = 0.5f;
    /// <summary>How much outranging the enemy counts: (own range ÷ enemy range) ^ this, clamped to 0.5..2.</summary>
    [Serialize(Optional = true)] public float RiskRangeWeight = 0.5f;
    /// <summary>Bravery of each personality group (faction ID, pirates, hunters), 0 to 1. 0.5 judges the odds as they are.</summary>
    [Serialize(Optional = true)] public GroupBravery[] RiskBravery =
    [
        new("monolith", 0.6f), new("cabal", 0.75f), new("imperium", 0.55f), new("fringe", 0.35f), new("io", 0.45f),
        new("pirates", 0.4f), new("hunters", 0.7f),
    ];
    /// <summary>Bravery of a group without a RiskBravery entry.</summary>
    [Serialize(Optional = true)] public float RiskBraveryDefault = 0.5f;
    /// <summary>Each ship's bravery is its group's ± up to this, from a hash of its ID.</summary>
    [Serialize(Optional = true)] public float RiskBraverySpread = 0.25f;
    /// <summary>A ship demands the players' surrender when its confidence (odds × (0.5 + bravery)) is at least this.</summary>
    [Serialize(Optional = true)] public float RiskDemandAt = 2f;
    /// <summary>A ship flees (instead of the FleeChance roll) when its confidence is at most this.</summary>
    [Serialize(Optional = true)] public float RiskFleeAt = 0.6f;
    /// <summary>A ship offers its surrender when its confidence is at most this (corporate hunters flee instead).</summary>
    [Serialize(Optional = true)] public float RiskSurrenderAt = 0.3f;

    public StanceThresholds Stances => new(RiskDemandAt, RiskFleeAt, RiskSurrenderAt);

    /// <summary>Called from TunablesFile.Validate (spec 53).</summary>
    public void Validate(Action<bool, string> require)
    {
        require(RiskJoinRadius >= 0 && RiskCrewWeight is >= 0 and <= 1 && RiskRangeWeight >= 0 && RiskBraverySpread is >= 0 and <= 1
                && RiskBraveryDefault is >= 0 and <= 1, "Diplomacy.RiskJoinRadius, RiskRangeWeight must be 0 or more; RiskCrewWeight, RiskBraverySpread, RiskBraveryDefault 0 to 1");
        require(RiskSurrenderAt <= RiskFleeAt && RiskFleeAt < RiskDemandAt && RiskSurrenderAt >= 0,
            "Diplomacy.RiskSurrenderAt <= RiskFleeAt < RiskDemandAt, and RiskSurrenderAt 0 or more");
        require(SurrenderMinFightSeconds >= 0 && SurrenderMinDamage is >= 0 and <= 1, "Diplomacy.SurrenderMinFightSeconds must be 0 or more, SurrenderMinDamage 0 to 1");
        require(HireSureUnits > 0 && HireMinShare is > 0 and <= 1 && HirePerHeat >= 0 && HireUseFalloff >= 0 && HireLossFalloff >= 0,
            "Diplomacy.HireSureUnits must be above 0, HireMinShare above 0 to 1, HirePerHeat, HireUseFalloff and HireLossFalloff 0 or more");
        require(HireHalfLifeMinutes >= 0 && HireCleanDiscount >= 0 && HireMaxDiscount is >= 0 and <= 0.2f,
            "Diplomacy.HireHalfLifeMinutes and HireCleanDiscount must be 0 or more, HireMaxDiscount 0 to 0.2");
        var groups = new HashSet<string>();
        foreach (var g in RiskBravery)
            require(g.Group.Length > 0 && groups.Add(g.Group) && g.Bravery is >= 0 and <= 1, $"Diplomacy.RiskBravery '{g.Group}': empty, twice, or bravery outside 0 to 1");
    }
}

/// <summary>Spec 1523: the base bravery of a personality group.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class GroupBravery
{
    /// <summary>A personality group: a faction ID, "pirates" or "hunters".</summary>
    [Serialize] public string Group = "";
    [Serialize(Optional = true)] public float Bravery = 0.5f;

    public GroupBravery() { }

    public GroupBravery(string group, float bravery) => (Group, Bravery) = (group, bravery);
}

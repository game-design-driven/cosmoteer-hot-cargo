using Halfling.Serialization;

namespace HotCargo.Missions;

[ReflectiveSerialization(Explicit = true)]
public sealed class MissionTunables
{
    [Serialize(Optional = true)] public float ShadyRewardFactor = 2.0f;
    [Serialize(Optional = true)] public float ShadyTrapChanceBase = 0.2f;
    [Serialize(Optional = true)] public float ShadyTrapChancePerHeat = 0.006f;
    [Serialize(Optional = true)] public float ShadyTrapSurvivalReward = 0.5f;
    /// <summary>Spec 36: heat change when a Convoy Duty completes. Negative: a registered convoy hides the players' trail.</summary>
    [Serialize(Optional = true)] public float EscortHeatChange = -8f;
    /// <summary>
    /// Spec 36: Convoy Duty route length, as a share of the distance from the system center to the nearest jump gate.
    /// 0.65 gives about 9,000 in a Hot Cargo system. The route stays inside the gate ring, so a long value can drop offers.
    /// </summary>
    [Serialize(Optional = true)] public float EscortRouteShare = 0.65f;
    /// <summary>Spec 36: share of the route that the freighter covers before the raiders ambush it.</summary>
    [Serialize(Optional = true)] public float EscortAmbushAt = 0.3f;
    /// <summary>Spec 36: distance ahead of the freighter, on its route, where the raiders appear.</summary>
    [Serialize(Optional = true)] public float EscortAmbushLead = 1800f;
    /// <summary>
    /// Spec 36: how much convoy raiders want the freighter. They value it like a ship with this many times their own firepower.
    /// Vanilla AI ignores unarmed civilian ships, so without this they fight only the escort. Vanilla Normal AI weighs distance
    /// with exponent 1.5 and keeps its target until another scores 6 times higher, so the freighter needs a large value to win over a nearer player ship.
    /// </summary>
    [Serialize(Optional = true)] public float EscortRaiderFocus = 20f;
    /// <summary>Spec 1851: after the accept, the escort stops this far from the hull of the nearest player ship and waits.</summary>
    [Serialize(Optional = true)] public float EscortMeetGap = 300f;
    /// <summary>Spec 1851: the escort stops at least this far inside the comms circle of the player ship, so the player can call it.</summary>
    [Serialize(Optional = true)] public float EscortCommsMargin = 50f;
    /// <summary>Spec 1851: the waiting escort goes at most this far from where it started. Past it, the players come to the escort.</summary>
    [Serialize(Optional = true)] public float EscortMeetRange = 3000f;
    /// <summary>Spec 1852: the waiting escort hails when a player ship's hull is this close to its hull.</summary>
    [Serialize(Optional = true)] public float EscortHailGap = 800f;
    /// <summary>Spec 1853: seconds between two hails of a waiting escort while a player ship stays close.</summary>
    [Serialize(Optional = true)] public float EscortRehailSeconds = 60f;
    /// <summary>Spec 1854: radius of the map ring that marks a waiting escort.</summary>
    [Serialize(Optional = true)] public float EscortMarkerRadius = 600f;
    /// <summary>
    /// Spec 1881: an escort under way moves only while a player ship's hull is this close to its hull. Keep it well below the game's
    /// StasisLiveRange (2,500 from a player ship): then a moving escort and the raiders on its route are always live, never in stasis.
    /// </summary>
    [Serialize(Optional = true)] public float EscortGuardRadius = 1500f;
    /// <summary>Spec 1882: game seconds that an escort under way waits alone before raiders find it.</summary>
    [Serialize(Optional = true)] public float EscortRaidSeconds = 60f;
    /// <summary>
    /// Spec 1883, 1886: game seconds alone after which raiders take an escort. An escort's rules can set their own (the spawner's
    /// LostSeconds) to fit the trip.
    /// </summary>
    [Serialize(Optional = true)] public float EscortLostSeconds = 240f;
    /// <summary>Spec 1886: the second banner and chat line come this many seconds before raiders take a lone escort.</summary>
    [Serialize(Optional = true)] public float EscortLostWarnSeconds = 60f;
    /// <summary>Spec 1882: ships of one raid (scaled for co-op). Live raiders of the same escort count against EscortRaidMaxLive.</summary>
    [Serialize(Optional = true)] public int EscortRaidShips = 2;
    [Serialize(Optional = true)] public int EscortRaidMaxLive = 4;
    /// <summary>Spec 1882: raid ships have the escort mission's tier plus this, at least 1.</summary>
    [Serialize(Optional = true)] public int EscortRaidTierOffset = -1;
    /// <summary>Spec 1882: built-in ship tags of the raid pool.</summary>
    [Serialize(Optional = true)] public string[] EscortRaidShipTags = ["combat"];
    /// <summary>Spec 1882: raid ships jump in this far from the escort's center. Close, so they share its live area.</summary>
    [Serialize(Optional = true)] public float EscortRaidDistanceMin = 900f;
    [Serialize(Optional = true)] public float EscortRaidDistanceMax = 1300f;

    /// <summary>Spec 2430: the chance that a mayday is due in a system on arrival, and its delay after the arrival.</summary>
    [Serialize(Optional = true)] public float MaydayChance = 0.35f;
    [Serialize(Optional = true)] public float MaydayDelaySeconds = 45f;
    /// <summary>Spec 2431: the fight spawns this far from the nearest player ship, so it is live and in sight.</summary>
    [Serialize(Optional = true)] public float MaydayDistanceMin = 2000f;
    [Serialize(Optional = true)] public float MaydayDistanceMax = 2400f;
    /// <summary>Spec 2431: attackers (scaled for co-op), their tier offset to the sector tier, and the ship tags of victim and attackers.</summary>
    [Serialize(Optional = true)] public int MaydayRaiders = 2;
    [Serialize(Optional = true)] public int MaydayRaiderTierOffset = -1;
    [Serialize(Optional = true)] public string[] MaydayVictimTags = ["trade"];
    [Serialize(Optional = true)] public string[] MaydayRaiderTags = ["combat"];
    /// <summary>Spec 2431: the attackers jump in this far from the victim.</summary>
    [Serialize(Optional = true)] public float MaydayRaiderDistanceMin = 600f;
    [Serialize(Optional = true)] public float MaydayRaiderDistanceMax = 900f;
    /// <summary>Spec 2432: the pay, as multiples of the standard money and fame of the tier.</summary>
    [Serialize(Optional = true)] public float MaydayMoneyFactor = 2f;
    [Serialize(Optional = true)] public float MaydayFameFactor = 2f;
    /// <summary>Spec 1891: the Blind Courier waits this far from the station center, in a random direction.</summary>
    [Serialize(Optional = true)] public float LeadSpawnDistanceMin = 500f;
    [Serialize(Optional = true)] public float LeadSpawnDistanceMax = 800f;
    /// <summary>Spec 1892: the courier follows this far behind the hull of the nearest player ship.</summary>
    [Serialize(Optional = true)] public float LeadFollowGap = 250f;
    /// <summary>Spec 1892: the courier's move order follows a moving player ship only past this distance, so it does not re-plan every tick.</summary>
    [Serialize(Optional = true)] public float LeadFollowSlack = 150f;
    /// <summary>Spec 1893: the share of the route at which the one ambush comes.</summary>
    [Serialize(Optional = true)] public float LeadAmbushAt = 0.5f;
    /// <summary>Spec 1890: encounters within this distance of the straight route count for the jump point pick and the hint.</summary>
    [Serialize(Optional = true)] public float LeadEncounterRange = 2000f;
    /// <summary>Spec 1890: a jump point with a hostile ship this close does not fit.</summary>
    [Serialize(Optional = true)] public float LeadDestinationClear = 2000f;
    /// <summary>Spec 1890: weights of a hostile ship, a hostile station, and a nebula cell of LeadNebulaGridStep near the route.</summary>
    [Serialize(Optional = true)] public float LeadEncounterShipWeight = 1f;
    [Serialize(Optional = true)] public float LeadEncounterStationWeight = 3f;
    [Serialize(Optional = true)] public float LeadEncounterNebulaWeight = 0.25f;
    /// <summary>Spec 1890: nebulae count on a grid with this step.</summary>
    [Serialize(Optional = true)] public float LeadNebulaGridStep = 1000f;
    /// <summary>Spec 1893: ambush ships jump in this far ahead of the courier, toward the jump point.</summary>
    [Serialize(Optional = true)] public float LeadAmbushDistanceMin = 1300f;
    [Serialize(Optional = true)] public float LeadAmbushDistanceMax = 1700f;

    /// <summary>Called by TunablesFile.Validate, so a bad file keeps the last valid values (spec 53).</summary>
    public static void Validate(MissionTunables t)
    {
        static void Require(bool ok, string what) { if (!ok) throw new InvalidDataException($"Missions.{what}"); }
        Require(t.EscortGuardRadius > 0f, "EscortGuardRadius must be above 0");
        Require(t.EscortRaidSeconds > 0f && t.EscortLostSeconds > t.EscortRaidSeconds, "EscortLostSeconds must be above EscortRaidSeconds, and both above 0");
        Require(t.EscortLostWarnSeconds > 0f && t.EscortLostWarnSeconds < t.EscortLostSeconds, "EscortLostWarnSeconds must be above 0 and below EscortLostSeconds");
        Require(t.EscortRaidShips >= 0 && t.EscortRaidMaxLive >= 0 && t.EscortRaidShipTags.Length > 0, "EscortRaidShips and EscortRaidMaxLive must not be negative, EscortRaidShipTags must not be empty");
        Require(t.MaydayChance is >= 0f and <= 1f && t.MaydayDelaySeconds >= 0f, "MaydayChance must be 0 to 1, MaydayDelaySeconds not negative");
        Require(t.MaydayDistanceMin >= 0f && t.MaydayDistanceMax >= t.MaydayDistanceMin && t.MaydayRaiderDistanceMin >= 0f && t.MaydayRaiderDistanceMax >= t.MaydayRaiderDistanceMin,
            "Mayday distances: each Max must be at least its Min");
        Require(t.MaydayRaiders >= 1 && t.MaydayVictimTags.Length > 0 && t.MaydayRaiderTags.Length > 0 && t.MaydayMoneyFactor >= 0f && t.MaydayFameFactor >= 0f,
            "MaydayRaiders must be 1 or more, the Mayday tags not empty, the pay factors not negative");
        Require(t.EscortRaidDistanceMin >= 0f && t.EscortRaidDistanceMax >= t.EscortRaidDistanceMin, "EscortRaidDistanceMax must be at least EscortRaidDistanceMin");
        Require(t.LeadSpawnDistanceMin >= 0f && t.LeadSpawnDistanceMax >= t.LeadSpawnDistanceMin, "LeadSpawnDistanceMax must be at least LeadSpawnDistanceMin");
        Require(t.LeadFollowGap >= 0f && t.LeadFollowSlack > 0f, "LeadFollowGap must not be negative, LeadFollowSlack must be above 0");
        Require(t.LeadAmbushAt is > 0f and < 1f, "LeadAmbushAt must be between 0 and 1");
        Require(t.LeadEncounterRange > 0f && t.LeadDestinationClear >= 0f && t.LeadNebulaGridStep > 0f, "LeadEncounterRange and LeadNebulaGridStep must be above 0, LeadDestinationClear not negative");
        Require(t.LeadEncounterShipWeight >= 0f && t.LeadEncounterStationWeight >= 0f && t.LeadEncounterNebulaWeight >= 0f, "LeadEncounter weights must not be negative");
        Require(t.LeadAmbushDistanceMin >= 0f && t.LeadAmbushDistanceMax >= t.LeadAmbushDistanceMin, "LeadAmbushDistanceMax must be at least LeadAmbushDistanceMin");
    }
}

using Halfling.Serialization;

namespace HotCargo.Missions.Mechanics;

/// <summary>Numbers of the mechanics missions (spec 130 to 139). Rules values (part health, asteroid counts, rewards) are in mod/missions/mechanics.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class MechanicsTunables
{
    /// <summary>Spec 131: chance that a system offers each mission. MechanicsArea applies them to the spawner groups before a system generates.</summary>
    [Serialize(Optional = true)] public float DroneSiegeChance = 0.5f;
    [Serialize(Optional = true)] public float PrototypeChance = 0.5f;
    [Serialize(Optional = true)] public float SunMiningChance = 0.5f;
    [Serialize(Optional = true)] public float TowChance = 0.5f;
    [Serialize(Optional = true)] public float CaptureChance = 0.5f;
    [Serialize(Optional = true)] public float SurveyChance = 0.5f;
    [Serialize(Optional = true)] public float MiningGuardChance = 0.5f;
    [Serialize(Optional = true)] public float ListeningPostChance = 0.5f;
    /// <summary>Spec 1867: chance that a system offers a Mining Escort.</summary>
    [Serialize(Optional = true)] public float MiningEscortChance = 0.5f;

    /// <summary>Spec 1860 to 1867: Mining Escort.</summary>
    [Serialize(Optional = true)] public MiningEscortTunables MiningEscort = new();

    /// <summary>Spec 132: Drone Siege. Tier-1 to tier-3 drones, many of them, for five minutes.</summary>
    [Serialize(Optional = true)] public HoldOutTunables Siege = new()
    {
        Seconds = 300f, GuardRadius = 1500f, WaveSeconds = 35f, FirstWaveSeconds = 8f,
        ShipsBase = 2f, ShipsPerTier = 0.25f, MaxLiveShips = 10,
        ShipTierBase = 1f, ShipTierPerTier = 0.2f, ShipTierMax = 3, ShipTags = ["combat"],
    };

    /// <summary>Spec 137: Claim Jumpers. Few raiders one tier below the sector, for four minutes.</summary>
    [Serialize(Optional = true)] public HoldOutTunables Guard = new()
    {
        Seconds = 240f, GuardRadius = 1500f, WaveSeconds = 60f, FirstWaveSeconds = 15f,
        ShipsBase = 1f, ShipsPerTier = 0.1f, MaxLiveShips = 4,
        ShipTierBase = -1f, ShipTierPerTier = 1f, ShipTierMax = 99, ShipTags = ["combat"],
    };

    /// <summary>Spec 140: Listening Post. Four waves, one tier below the sector, from the base side.</summary>
    [Serialize(Optional = true)] public HoldOutTunables Scan = new()
    {
        Seconds = 180f, GuardRadius = 1500f, WaveSeconds = 45f, FirstWaveSeconds = 5f,
        ShipsBase = 1f, ShipsPerTier = 0.15f, MaxLiveShips = 6,
        ShipTierBase = -1f, ShipTierPerTier = 1f, ShipTierMax = 99, ShipTags = ["combat"],
    };

    /// <summary>Spec 140: the scan can start within this distance of the hostile base.</summary>
    [Serialize(Optional = true)] public float ScanRange = 3500f;
    /// <summary>Spec 140: each intensive scan adds this to the money factor, and the next wave gets extra ships and tiers.</summary>
    [Serialize(Optional = true)] public float IntensivePayBonus = 0.25f;
    [Serialize(Optional = true)] public float IntensiveExtraShips = 1f;
    [Serialize(Optional = true)] public float IntensiveExtraTier = 1f;
    [Serialize(Optional = true)] public int MaxIntensivePerWave = 2;

    /// <summary>Spec 134: a hulk counts as delivered when its hull is this close to the station hull.</summary>
    [Serialize(Optional = true)] public float DeliveryRadius = 800f;

    /// <summary>Spec 136: seconds that a ship with a working sensor must stay at each storm marker.</summary>
    [Serialize(Optional = true)] public float SurveyScanSeconds = 15f;

    /// <summary>Called by TunablesFile.Validate, so a bad file keeps the last valid values (spec 53).</summary>
    public static void Validate(MechanicsTunables t)
    {
        static void Require(bool ok, string what) { if (!ok) throw new InvalidDataException($"Mechanics.{what}"); }
        foreach (var (name, chance) in MechanicsArea.Kinds.Select(k => (k.Name, k.Chance(t))))
            Require(chance is >= 0f and <= 1f, $"chance of '{name}' must be 0 to 1");
        foreach (var (name, h) in new[] { ("Siege", t.Siege), ("Guard", t.Guard), ("Scan", t.Scan) })
        {
            Require(h.Seconds > 0f && h.GuardRadius > 0f && h.WaveSeconds > 0f && h.FirstWaveSeconds >= 0f, $"{name}: Seconds, GuardRadius and WaveSeconds must be above 0");
            Require(h.ShipsBase >= 0f && h.ShipsPerTier >= 0f && h.MaxLiveShips >= 0 && h.ShipTierMax >= 1, $"{name}: ship counts must not be negative and ShipTierMax must be 1 or more");
            Require(h.ShipTags.Length > 0, $"{name}.ShipTags must not be empty");
        }
        Require(t.DeliveryRadius > 0f, "DeliveryRadius must be above 0");
        Require(t.ScanRange > 0f && t.IntensivePayBonus >= 0f && t.IntensiveExtraShips >= 0f && t.MaxIntensivePerWave >= 0, "ScanRange must be above 0 and the intensive scan values must not be negative");
        Require(t.SurveyScanSeconds > 0f, "SurveyScanSeconds must be above 0");
        var m = t.MiningEscort;
        Require(m.WorkRange > 0f, "MiningEscort.WorkRange must be above 0");
        Require(m.SpawnDistanceMin >= 0f && m.SpawnDistanceMax >= m.SpawnDistanceMin, "MiningEscort: SpawnDistanceMax must be at least SpawnDistanceMin");
        Require(m.OreGoal > 0 && m.FieldRadius > 0f && m.HostileRadius >= 0f && m.CollectRadius > 0f && m.StepSeconds > 0f && m.DockGap >= 0f && m.DockedGap > m.DockGap,
            "MiningEscort: OreGoal, FieldRadius, CollectRadius and StepSeconds must be above 0, and DockedGap above DockGap");
        Require(m.WaveShares.Length > 0 && m.WaveShares.All(s => s is > 0f and < 1f), "MiningEscort.WaveShares must hold shares between 0 and 1");
        Require(m.ShipsBase >= 0f && m.ShipsPerTier >= 0f && m.MaxLiveShips >= 0 && m.ShipTierMax >= 1 && m.ShipTags.Length > 0,
            "MiningEscort: ship counts must not be negative, ShipTierMax must be 1 or more, ShipTags must not be empty");
    }
}

/// <summary>One hold-out mission profile (spec 132, 137). The clock runs only while a human ship is within GuardRadius of the target.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HoldOutTunables
{
    /// <summary>Guarded seconds needed to complete the mission.</summary>
    [Serialize(Optional = true)] public float Seconds = 300f;
    /// <summary>Distance from the target within which a human ship keeps the clock running.</summary>
    [Serialize(Optional = true)] public float GuardRadius = 1500f;
    /// <summary>Guarded seconds between waves.</summary>
    [Serialize(Optional = true)] public float WaveSeconds = 40f;
    /// <summary>Guarded seconds before the first wave.</summary>
    [Serialize(Optional = true)] public float FirstWaveSeconds = 10f;
    /// <summary>Ships per wave = ShipsBase + ShipsPerTier × mission tier, rounded, capped so that live wave ships stay at MaxLiveShips or below.</summary>
    [Serialize(Optional = true)] public float ShipsBase = 2f;
    [Serialize(Optional = true)] public float ShipsPerTier;
    [Serialize(Optional = true)] public int MaxLiveShips = 8;
    /// <summary>Tier of each wave ship = ShipTierBase + ShipTierPerTier × mission tier, rounded, in 1..ShipTierMax.</summary>
    [Serialize(Optional = true)] public float ShipTierBase = 1f;
    [Serialize(Optional = true)] public float ShipTierPerTier;
    [Serialize(Optional = true)] public int ShipTierMax = 99;
    /// <summary>Built-in ship tags of the wave pool.</summary>
    [Serialize(Optional = true)] public string[] ShipTags = ["combat"];
}

/// <summary>Mining Escort (spec 1860 to 1867): a miner fills its hold in an asteroid field while raiders come in waves.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class MiningEscortTunables
{
    /// <summary>The miner works only while a player ship is within this distance; else it holds still, like a ship in stasis (vanilla live range 2,500).</summary>
    [Serialize(Optional = true)] public float WorkRange = 2500f;
    /// <summary>The miner waits this far from the station center, in a random direction.</summary>
    [Serialize(Optional = true)] public float SpawnDistanceMin = 500f;
    [Serialize(Optional = true)] public float SpawnDistanceMax = 800f;
    /// <summary>The most ore the miner must bring back. A smaller hold sets a smaller goal. About 2.5 min of mining for the miner alone.</summary>
    [Serialize(Optional = true)] public int OreGoal = 35;
    /// <summary>The miner works on asteroids within this distance of the field center. Its ore type is the most common ore there.</summary>
    [Serialize(Optional = true)] public float FieldRadius = 3000f;
    /// <summary>
    /// A field with a ship hostile to the players within this distance of its center offers no job. Vanilla field pirates and platforms
    /// sit within 750 of it; wanderers and bounty targets nearby do not block the offer.
    /// </summary>
    [Serialize(Optional = true)] public float HostileRadius = 1000f;
    /// <summary>The miner orders loose ore of its type within this distance of its hull to be collected, also ore that players drop.</summary>
    [Serialize(Optional = true)] public float CollectRadius = 1500f;
    /// <summary>Game seconds between two work steps of the miner (move order, salvage and collect orders).</summary>
    [Serialize(Optional = true)] public float StepSeconds = 0.5f;
    /// <summary>Shares of the goal at which a wave comes, one wave each.</summary>
    [Serialize(Optional = true)] public float[] WaveShares = [0.25f, 0.65f];
    /// <summary>Ships per wave = ShipsBase + ShipsPerTier × mission tier, rounded, capped so that live wave ships stay at MaxLiveShips or below.</summary>
    [Serialize(Optional = true)] public float ShipsBase = 1f;
    [Serialize(Optional = true)] public float ShipsPerTier = 0.15f;
    [Serialize(Optional = true)] public int MaxLiveShips = 5;
    /// <summary>Tier of each wave ship = ShipTierBase + ShipTierPerTier × mission tier, rounded, in 1..ShipTierMax.</summary>
    [Serialize(Optional = true)] public float ShipTierBase = -1f;
    [Serialize(Optional = true)] public float ShipTierPerTier = 1f;
    [Serialize(Optional = true)] public int ShipTierMax = 99;
    /// <summary>Built-in ship tags of the wave pool.</summary>
    [Serialize(Optional = true)] public string[] ShipTags = ["combat"];
    /// <summary>On the way back, the miner stops this far from the station's hull.</summary>
    [Serialize(Optional = true)] public float DockGap = 200f;
    /// <summary>The miner counts as docked, and the job is done, when its hull is this close to the station's hull.</summary>
    [Serialize(Optional = true)] public float DockedGap = 500f;
}


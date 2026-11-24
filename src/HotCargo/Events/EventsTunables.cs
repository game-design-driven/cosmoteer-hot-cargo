using Halfling.Serialization;

namespace HotCargo.Events;

/// <summary>Event frequency, cooldowns and global scales (live reload). Event content is in mod/events/events.rules (restart).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class EventsTunables
{
    /// <summary>Events that open by themselves (Start, Arrival, Station, Signal, Wanted). Off: none of them opens.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>
    /// The run's first system (spec 69): the Arrival and Station chances × this, at most 1, so system 1 very likely has an event.
    /// Its Arrival roll comes when the start cooldown ends. 1: like every other system.
    /// </summary>
    [Serialize(Optional = true)] public float FirstSystemChanceFactor = 1.5f;
    /// <summary>Chance that the first arrival in a system opens an Arrival event.</summary>
    [Serialize(Optional = true)] public float ArrivalEventChance = 0.6f;
    /// <summary>Game seconds after arrival before the Arrival event opens, so the players see the system first.</summary>
    [Serialize(Optional = true)] public float ArrivalDelaySeconds = 6f;
    /// <summary>Chance, once per system visit, that the first trade station a player ship comes near opens a Station event.</summary>
    [Serialize(Optional = true)] public float StationEventChance = 0.5f;
    [Serialize(Optional = true)] public float StationTriggerDistance = 1500f;
    /// <summary>Chance, once per system for the run, that the first trade station a player ship comes near offers a Courier Job.</summary>
    [Serialize(Optional = true)] public float CourierChance = 0.5f;
    /// <summary>Heat that each Tracer Beacon in a player ship adds once a minute (spec 1116).</summary>
    [Serialize(Optional = true)] public float TracerHeatPerMinute = 3f;
    [Serialize(Optional = true)] public float SignalTriggerDistance = 900f;
    /// <summary>Arrival and Station events wait this many game seconds after the last triggered event. A new run starts it. Signal events ignore it but start it.</summary>
    [Serialize(Optional = true)] public float CooldownSeconds = 120f;
    /// <summary>Arrival and Station events in one system, all visits together. Signal events do not count, because the player chose to go there.</summary>
    [Serialize(Optional = true)] public int MaxEventsPerSystem = 2;
    /// <summary>The wanted-and-hunted warning shows when the players are wanted and heat reached this stage (1 = the first stage).</summary>
    [Serialize(Optional = true)] public int WantedWarningMinStage = 1;
    /// <summary>Chance that the wanted-and-hunted warning also brings a Wanted event (a way to clear wanted).</summary>
    [Serialize(Optional = true)] public float WantedEventChance = 0.7f;
    /// <summary>Research trade: one tech unit buys up to this many times its tech value in research.</summary>
    [Serialize(Optional = true)] public float ResearchRate = 1.5f;
    /// <summary>Research trade: the research must be worth at least this many times the tech value of the units. Above 1, so it beats buying.</summary>
    [Serialize(Optional = true)] public float ResearchMinRate = 1.2f;
    /// <summary>Research trade: the most tech units one offer asks for.</summary>
    [Serialize(Optional = true)] public int ResearchMaxUnits = 2;
    /// <summary>Research offers: the most techs in one offer.</summary>
    [Serialize(Optional = true)] public int ResearchMaxTechs = 3;
    /// <summary>Research sale: the buyer pays this many times the research cost of the techs.</summary>
    [Serialize(Optional = true)] public float ResearchSalePremium = 1.3f;
    /// <summary>Research sale: the highest price, in event money units (standard mission pay at the tier).</summary>
    [Serialize(Optional = true)] public float ResearchSaleCap = 1.5f;
    /// <summary>Multiplies the trigger weight of research events: 0 turns them off, 2 doubles them.</summary>
    [Serialize(Optional = true)] public float ResearchWeightScale = 1f;
    /// <summary>Chance that a vanilla abandoned ship holds Hot Cargo loot (spec 1124). Else it keeps its vanilla cargo and gives no message.</summary>
    [Serialize(Optional = true)] public float HulkLootChance = 0.6f;
    /// <summary>Distance at which a hulk with loot shows its message (once per hulk).</summary>
    [Serialize(Optional = true)] public float AbandonedTriggerDistance = 800f;
    /// <summary>Patrol listen (spec 1122): game seconds that a sensor array listens to a faction ship.</summary>
    [Serialize(Optional = true)] public float ListenSeconds = 8f;
    /// <summary>Patrol listen: the most hull-to-hull distance between the listening ship and the faction ship.</summary>
    [Serialize(Optional = true)] public float ListenRange = 1500f;
    /// <summary>Patrol listen: success chance at heat 0. The chance falls in a line to ListenChanceAtMaxHeat at heat 100.</summary>
    [Serialize(Optional = true)] public float ListenChanceAtNoHeat = 1f;
    /// <summary>Patrol listen: success chance at heat 100.</summary>
    [Serialize(Optional = true)] public float ListenChanceAtMaxHeat = 0.6f;
    /// <summary>Patrol listen: heat change on a success (they give away the patrol routes).</summary>
    [Serialize(Optional = true)] public float ListenSuccessHeat = -4f;
    /// <summary>Patrol listen: heat change on a failure (they log your question).</summary>
    [Serialize(Optional = true)] public float ListenFailHeat = 5f;
    /// <summary>Patrol listen: weapons fire within this distance of the listening ship breaks the listen.</summary>
    [Serialize(Optional = true)] public float ListenCombatRadius = 800f;
    /// <summary>Patrol listen: a shot counts as combat for this many game seconds.</summary>
    [Serialize(Optional = true)] public float ListenCombatSeconds = 5f;
    /// <summary>A hulk whose tanks blow (spec 1126) damages player ships within this hull-to-hull distance.</summary>
    [Serialize(Optional = true)] public float HulkBlastRadius = 300f;
    /// <summary>Rescue Beacon (spec 2351): its price in a station's trade window, in event money units (before the station's heat markup).</summary>
    [Serialize(Optional = true)] public float RescueBeaconPrice = 0.3f;
    /// <summary>Rescue Beacon (spec 2351): the tug's fee in event money units (standard mission pay at the tier). Too little money: it takes what there is.</summary>
    [Serialize(Optional = true)] public float RescueFee = 0.4f;
    /// <summary>Rescue Beacon: heat that the broadcast adds.</summary>
    [Serialize(Optional = true)] public float RescueHeat = 10f;
    /// <summary>Rescue Beacon: hull-to-hull length of the tow line between the tug and the towed ship.</summary>
    [Serialize(Optional = true)] public float RescueTowLength = 150f;
    /// <summary>Rescue Beacon: how hard the tow line pulls the towed ship back to its length, per game second.</summary>
    [Serialize(Optional = true)] public float RescueTowPull = 1f;
    /// <summary>Rescue Beacon: the tug keeps this far outside the heat zone of a star on its way to the station.</summary>
    [Serialize(Optional = true)] public float RescueSunMargin = 300f;
    /// <summary>Rescue Beacon: game seconds that the tug flies off before it leaves the sim.</summary>
    [Serialize(Optional = true)] public float RescueTugLeaveSeconds = 20f;
    /// <summary>Station attack (spec 90): chance on the first visit of a system, from this sector on, while the Pursuit pacer allows one.</summary>
    [Serialize(Optional = true)] public float StationAttackChance = 0.2f;
    [Serialize(Optional = true)] public int StationAttackMinSector = 2;
    /// <summary>Station attack: the roll waits this many game seconds after the arrival, so it does not land on top of the Arrival event.</summary>
    [Serialize(Optional = true)] public float StationAttackDelaySeconds = 40f;
    /// <summary>Station attack: then it runs once, when a player ship's hull first comes within this distance of a trade station's hull.</summary>
    [Serialize(Optional = true)] public float StationAttackTriggerDistance = 1500f;
    /// <summary>Station attack: the share of the weapon parts of the station and its defense platforms that the raiders' first strike destroys, so the players' help matters.</summary>
    [Serialize(Optional = true)] public float StationAttackGunsDown = 0.9f;
    /// <summary>Station attack (spec 93): a player ship within this hull-to-hull distance of a raider is in its way.</summary>
    [Serialize(Optional = true)] public float StationAttackInTheWayRange = 150f;
    /// <summary>Station attack (spec 94): the raiders win when the station's health falls below this fraction.</summary>
    [Serialize(Optional = true)] public float StationAttackFallHealth = 0.8f;
    /// <summary>Station attack: game seconds after which the raiders give up and leave (the station holds).</summary>
    [Serialize(Optional = true)] public float StationAttackMaxSeconds = 240f;
    /// <summary>Station attack: the pay for help in event money units, and the reputation with the station's faction in reputation units.</summary>
    [Serialize(Optional = true)] public float StationAttackReward = 1f;
    [Serialize(Optional = true)] public float StationAttackReputation = 1f;
    /// <summary>Station rebuild (spec 96): the station pulls in debris within this distance of its hull.</summary>
    [Serialize(Optional = true)] public float StationRebuildRadius = 1000f;
    /// <summary>Station rebuild: the speed of the debris piece that the station pulls in.</summary>
    [Serialize(Optional = true)] public float StationRebuildPullSpeed = 40f;
    /// <summary>Station rebuild: the station takes in parts of a piece within this hull-to-hull distance.</summary>
    [Serialize(Optional = true)] public float StationRebuildReach = 20f;
    /// <summary>Station rebuild: game seconds between two steps; each step takes in StationRebuildPartsPerStep debris parts.</summary>
    [Serialize(Optional = true)] public float StationRebuildStepSeconds = 1f;
    [Serialize(Optional = true)] public int StationRebuildPartsPerStep = 3;
    /// <summary>Station rebuild: destroyed station parts that one debris part rebuilds.</summary>
    [Serialize(Optional = true)] public float StationRebuildPerDebrisPart = 2f;
    /// <summary>Station rebuild (spec 97): with no debris in reach for this many game seconds, the station rebuilds from its own stock.</summary>
    [Serialize(Optional = true)] public float StationRebuildWaitSeconds = 60f;
    /// <summary>Off the Books (spec 1802): the price of a crate as a share of the tech value, before the situation.</summary>
    [Serialize(Optional = true)] public float OffBooksPriceBase = 0.85f;
    /// <summary>Off the Books: the price share per 100 heat (buyers fear hot cargo).</summary>
    [Serialize(Optional = true)] public float OffBooksPricePerHeat = -0.15f;
    /// <summary>Off the Books: the price share per hunter in the system.</summary>
    [Serialize(Optional = true)] public float OffBooksPricePerHunter = -0.05f;
    /// <summary>Off the Books: the price share per reputation (−1 to 1) with the station's faction.</summary>
    [Serialize(Optional = true)] public float OffBooksPricePerReputation = 0.1f;
    [Serialize(Optional = true)] public float OffBooksPriceMin = 0.5f;
    [Serialize(Optional = true)] public float OffBooksPriceMax = 0.95f;
    /// <summary>Off the Books (spec 1803): the informant chance per crate at heat 0 and neutral standing.</summary>
    [Serialize(Optional = true)] public float OffBooksRiskBase = 0.04f;
    /// <summary>Off the Books: the informant chance per crate per 100 heat.</summary>
    [Serialize(Optional = true)] public float OffBooksRiskPerHeat = 0.1f;
    /// <summary>Off the Books: the informant chance per crate falls this much per reputation (−1 to 1) with the station's faction.</summary>
    [Serialize(Optional = true)] public float OffBooksRiskPerReputation = 0.04f;
    /// <summary>Off the Books (spec 1805): the sting chance per corporate ship (a hunter or a military ship of the corporation's ally) near the meet.</summary>
    [Serialize(Optional = true)] public float OffBooksRiskPerCorporate = 0.15f;
    /// <summary>Off the Books: corporate ships within this distance of the fence's hull count.</summary>
    [Serialize(Optional = true)] public float OffBooksCorporateRadius = 3000f;
    [Serialize(Optional = true)] public float OffBooksRiskCap = 0.9f;
    /// <summary>Off the Books (spec 1806): heat change per crate on a clean sale.</summary>
    [Serialize(Optional = true)] public float OffBooksHeatPerCrate = -10f;
    /// <summary>Off the Books (spec 1807): heat change of a sting (instead of the cut).</summary>
    [Serialize(Optional = true)] public float OffBooksStingHeat = 15f;
    /// <summary>Off the Books (spec 1804): game seconds that the fence waits at the meet.</summary>
    [Serialize(Optional = true)] public float OffBooksMeetSeconds = 300f;
    /// <summary>One relic ship per run holds 1 tech unit. Off: new runs get none.</summary>
    [Serialize(Optional = true)] public bool RelicEnabled = true;
    /// <summary>The relic ship appears in a random sector from this one up to the sector before the Buyer.</summary>
    [Serialize(Optional = true)] public int RelicMinSector = 2;
    /// <summary>
    /// Distance at which the vault senses the relic ship and the special message shows. Below the relic spawn distance
    /// (5000 x SystemScale, mod/events/spawns.rules), so the message never shows at the spawn.
    /// </summary>
    [Serialize(Optional = true)] public float RelicMessageDistance = 2000f;
    /// <summary>Distance at which the relic event opens.</summary>
    [Serialize(Optional = true)] public float RelicBoardDistance = 900f;
    /// <summary>Events of the Unavoidable category (every choice costs something) per run.</summary>
    [Serialize(Optional = true)] public int MaxUnavoidablePerRun = 1;
    /// <summary>Global scales on event outcomes, on top of the per-event amounts.</summary>
    [Serialize(Optional = true)] public float MoneyScale = 1f;
    [Serialize(Optional = true)] public float FameScale = 1f;
    [Serialize(Optional = true)] public float HeatScale = 1f;
    /// <summary>Event money amounts round to this many credits.</summary>
    [Serialize(Optional = true)] public int MoneyRoundTo = 100;
    /// <summary>Single player: an event dialog pauses the game until the players choose.</summary>
    [Serialize(Optional = true)] public bool PauseOnEvent = true;
}

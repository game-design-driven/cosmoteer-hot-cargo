using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Events;

/// <summary>Event state of the run. Saved inside RunState, so a save with a pending event loads with it.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class EventsState
{
    /// <summary>How often each event opened in this run, by event ID.</summary>
    [Serialize(Optional = true)] public Dictionary<string, int> Seen = new();
    /// <summary>Story flags that outcomes set and needs read, for event chains across systems.</summary>
    [Serialize(Optional = true)] public HashSet<string> Flags = new();
    /// <summary>Sector index at which each flag was last set, for FlagAges needs.</summary>
    [Serialize(Optional = true)] public Dictionary<string, int> FlagSectors = new();
    /// <summary>True while the wanted-and-hunted warning is up; it fires again only after the players stop being both.</summary>
    [Serialize(Optional = true)] public bool WantedWarned;
    /// <summary>Hail lines already used, as "eventId@shipId": each ship offers each line once per run (spec 769).</summary>
    [Serialize(Optional = true)] public HashSet<string> HailUsed = new();
    /// <summary>Vanilla abandoned ships that already had their loot roll (spec 1124), stocked or not.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> AbandonedSeen = new();
    /// <summary>Stocked hulks whose hold has not broken open yet (spec 1124, 1125).</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, HulkState> Hulks = new();
    /// <summary>Stocked hulks per kind in this run, for MaxPerRun.</summary>
    [Serialize(Optional = true)] public Dictionary<string, int> HulkKinds = new();
    /// <summary>The sector of the run's relic ship (spec 765), or -1 when the run has none.</summary>
    [Serialize(Optional = true)] public int RelicSector = -1;
    [Serialize(Optional = true)] public bool RelicSpawned;
    [Serialize(Optional = true)] public bool RelicMessaged;
    /// <summary>True while a human ship is within boarding distance of the relic. The relic event opens on the way in.</summary>
    [Serialize(Optional = true)] public bool RelicInside;
    /// <summary>Systems where the Courier Job roll already ran. Once per system for the whole run.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> CourierNodes = new();
    /// <summary>Game seconds of the next tracer beacon ping, or -1 while no beacon is aboard (spec 1116).</summary>
    [Serialize(Optional = true)] public double TracerPingSeconds = -1;
    /// <summary>The running patrol listen (spec 1122), or null. One at a time for the fleet.</summary>
    [Serialize(Optional = true)] public PatrolListenState? Listen;
    /// <summary>Faction ships that a patrol listen already finished on: once per ship (spec 1122).</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> Listened = new();
    /// <summary>Running rescue tows (spec 2352), in start order.</summary>
    [Serialize(Optional = true)] public List<RescueTowState> Rescues = [];
    /// <summary>The running station attack or rebuild (spec 90 to 98), or null. One at a time.</summary>
    [Serialize(Optional = true)] public StationAttackState? Attack;
    /// <summary>Game seconds at which the station attack roll runs, or -1 when none is due.</summary>
    [Serialize(Optional = true)] public double AttackDueSeconds = -1;
    /// <summary>The running Off the Books meet (spec 1804), or null. One at a time.</summary>
    [Serialize(Optional = true)] public OffBooksMeet? OffBooks;
    /// <summary>Game seconds of the last triggered event, or -1.</summary>
    [Serialize(Optional = true)] public double LastEventSeconds = -1;
    /// <summary>Game seconds at which the Arrival roll runs, or -1 when none is due.</summary>
    [Serialize(Optional = true)] public double ArrivalDueSeconds = -1;
    /// <summary>Arrival and Station events per system.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, int> NodeEvents = new();
    /// <summary>Systems where the Station roll already ran in the current visit. Cleared on each system switch.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> StationRolled = new();
    /// <summary>The run's first system (spec 69): its Arrival and Station chances are higher. Null until a run starts.</summary>
    [Serialize(Optional = true)] public ObjectID? StartNode;
    /// <summary>Last serial handed out. Each opened page gets a new one, so a stale choice input does nothing.</summary>
    [Serialize(Optional = true)] public int LastSerial;
    /// <summary>The event page that waits for a choice, or null.</summary>
    [Serialize(Optional = true)] public PendingEvent? Pending;
    /// <summary>
    /// Spec 2383: the page that waited when a player opened a hand-over line. It waits on, hidden, and comes back when the hand-over page
    /// closes (EventRunner.RestoreParked). Null: no page parked.
    /// </summary>
    [Serialize(Optional = true)] public PendingEvent? Parked;
    /// <summary>The result of the last choice that did not lead to a follow-up page. The dialog shows it once.</summary>
    [Serialize(Optional = true)] public EventResult? LastResult;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class PendingEvent
{
    [Serialize] public string EventId = "";
    [Serialize] public int Serial;
    /// <summary>Seed of the outcome rolls, from sim.Rand when the page opened.</summary>
    [Serialize] public ulong Seed;
    /// <summary>Sector tier and tech value when the event opened. Money amounts use these, so the shown costs do not drift.</summary>
    [Serialize] public int Tier = 1;
    [Serialize] public long TechValue;
    /// <summary>Heat when the page opened, for costs that grow with heat (Cost.MoneyPerHeat).</summary>
    [Serialize(Optional = true)] public float Heat;
    /// <summary>The ship that carries the page (spec 767): its comms show the page. Null: the event window shows it.</summary>
    [Serialize(Optional = true)] public ObjectID? Carrier;
    /// <summary>The system of the carrier when the page opened. Another system now: the players left it behind (spec 770).</summary>
    [Serialize(Optional = true)] public ObjectID? CarrierNode;
    /// <summary>True until the carrier hails: it waits until a player ship is in comms range, like a vanilla AI hail.</summary>
    [Serialize(Optional = true)] public bool HailDue;
    /// <summary>The page's computed offer (spec 760-762): tech IDs, the tech units it asks for, its research value, the sale price.</summary>
    [Serialize(Optional = true)] public string[] OfferTechs = [];
    [Serialize(Optional = true)] public int OfferUnits;
    [Serialize(Optional = true)] public long OfferValue;
    [Serialize(Optional = true)] public int OfferPrice;
    /// <summary>The units of the options picked with the choice, set just before its outcome applies, for C# effects (Off the Books).</summary>
    [Serialize(Optional = true)] public float PickedUnits;
    /// <summary>Outcome text and result lines of the choice that led to this page, or empty.</summary>
    [Serialize(Optional = true)] public string Lead = "";
}

public enum AttackPhase { Attack, Rebuild }

[ReflectiveSerialization(Explicit = true)]
public sealed class StationAttackState
{
    [Serialize] public ObjectID Station;
    [Serialize] public AttackPhase Phase;
    /// <summary>The raiders that are still in the attack: live, not junk.</summary>
    [Serialize] public List<ObjectID> Raiders = [];
    /// <summary>Game seconds at which the phase started.</summary>
    [Serialize] public double Since;
    /// <summary>True once a human player's shot damaged a raider during the attack (spec 95).</summary>
    [Serialize(Optional = true)] public bool Helped;
    /// <summary>Rebuild: game seconds at which debris was last in reach.</summary>
    [Serialize(Optional = true)] public double DebrisSeen;
    /// <summary>Rebuild: debris parts taken in and station parts rebuilt, for the log and the tests.</summary>
    [Serialize(Optional = true)] public int DebrisTaken;
    [Serialize(Optional = true)] public int Rebuilt;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class EventResult
{
    [Serialize] public int Serial;
    [Serialize] public string Title = "";
    [Serialize] public string Text = "";
    /// <summary>One XML line per effect, for example the money gained.</summary>
    [Serialize(Optional = true)] public string[] Lines = [];
}

/// <summary>An Off the Books meet (spec 1804): the quote from the station and the fence that waits.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class OffBooksMeet
{
    /// <summary>The station that set it up; its faction gives the standing.</summary>
    [Serialize] public ObjectID Station;
    /// <summary>The fence ship, once it spawned.</summary>
    [Serialize(Optional = true)] public ObjectID? Fence;
    /// <summary>The quote: credits per crate and the informant chance per crate.</summary>
    [Serialize] public int PricePerCrate;
    [Serialize] public float RiskPerCrate;
    /// <summary>Run game seconds at which the fence leaves.</summary>
    [Serialize] public double Until;
}


using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Friendly;

/// <summary>Friendly area state, saved in RunState. Changes only in sim logic (ticks, choice inputs, the ship-junked hook).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FriendlyState
{
    [Serialize(Optional = true)] public List<ApprenticeState> Apprentices = [];
    [Serialize(Optional = true)] public List<DebtorState> Debtors = [];
    /// <summary>The Wayfarers in this system (spec 1369), by ship ID. Empty while none help.</summary>
    [Serialize(Optional = true)] public List<ObjectID> Wayfarers = [];
    /// <summary>Systems where the Wayfarers' help roll already ran (spec 1370): one roll per system for the run.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> HelpRolled = new();
    /// <summary>Troublemakers aboard that have not reached a station yet (spec 480 to 485).</summary>
    [Serialize(Optional = true)] public List<TroublemakerState> Troublemakers = [];
    /// <summary>Scientists aboard whose research time has not come, or who are on their way out (spec 486 to 499).</summary>
    [Serialize(Optional = true)] public List<ScientistState> Scientists = [];
    /// <summary>The partner of a scientist who died aboard (spec 491 to 497). Phase None while there is none.</summary>
    [Serialize(Optional = true)] public PartnerState Partner = new();
    /// <summary>Total part health of the players' ships at the last help check (spec 1370), or -1 before the first.</summary>
    [Serialize(Optional = true)] public long PlayerHealth = -1;
    /// <summary>True once the Wayfarers came in this run (spec 1370): they come at most once per run.</summary>
    [Serialize(Optional = true)] public bool PayForwardDone;
    /// <summary>Run game seconds since no enemy is near the players while the Wayfarers help, or -1.</summary>
    [Serialize(Optional = true)] public double QuietSince = -1;
    /// <summary>Run game seconds of the Wayfarers' closing hail, or -1 before it.</summary>
    [Serialize(Optional = true)] public double HailedAt = -1;
}

/// <summary>A ship whose debt a player bought (spec 1367).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DebtorState
{
    [Serialize] public ObjectID Ship;
    [Serialize] public int Player;
    /// <summary>The price that the player paid, in credits. The debtor pays it back in full.</summary>
    [Serialize] public int Credits;
    /// <summary>Run game seconds at which it pays.</summary>
    [Serialize] public double Until;
    /// <summary>The system of the ship. In another system it cannot die, so it pays at its time.</summary>
    [Serialize] public ObjectID Node;
    /// <summary>XML ship name, for messages after the ship is gone.</summary>
    [Serialize] public string Name = "";
}

/// <summary>An apprentice that follows a player (spec 1362).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ApprenticeState
{
    [Serialize] public ObjectID Ship;
    [Serialize] public int Player;
    /// <summary>Run game seconds at which it decides.</summary>
    [Serialize] public double Until;
    /// <summary>Enemy ships that died near it.</summary>
    [Serialize] public int Fights;
}

/// <summary>A troublemaker that a player took aboard (spec 481).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class TroublemakerState
{
    [Serialize] public ObjectID Soul;
    [Serialize] public int Player;
    /// <summary>The pay that the player got, in credits. A thief steals TroublemakerTheftFactor times this.</summary>
    [Serialize] public int Pay;
    /// <summary>The result of the roll when they joined (spec 482).</summary>
    [Serialize] public bool Thief;
    [Serialize] public string Name = "";
}

public enum ScientistPhase { Aboard, Defecting, Flying }

/// <summary>A scientist in the players' crew (spec 487).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ScientistState
{
    [Serialize] public ObjectID Soul;
    [Serialize] public int Player;
    [Serialize] public string Name = "";
    [Serialize] public ScientistPhase Phase;
    /// <summary>Run game seconds of the research roll.</summary>
    [Serialize] public double Due;
    /// <summary>Seed of the research roll and the tech pick, from the page seed.</summary>
    [Serialize] public ulong Seed;
    [Serialize] public float ResearchChance;
    /// <summary>True: a failed research roll means they defect (spec 489). False (the partner, spec 496): nothing happens.</summary>
    [Serialize] public bool CanDefect;
    /// <summary>True: their death aboard brings their partner (spec 491). The partner who joins has no partner of their own.</summary>
    [Serialize] public bool HasPartner;
    /// <summary>The ship they defect to, and the processors they took.</summary>
    [Serialize(Optional = true)] public ObjectID? Target;
    [Serialize(Optional = true)] public int Stolen;
}

public enum PartnerPhase { None, Due, Challenge, Duel, Yield, RevengeDue, Revenge }

/// <summary>The partner of a dead scientist (spec 491 to 497).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class PartnerState
{
    [Serialize] public PartnerPhase Phase;
    /// <summary>Run game seconds at which the partner, or the revenge fleet, comes.</summary>
    [Serialize] public double At;
    /// <summary>The player whose scientist died.</summary>
    [Serialize] public int Player;
    /// <summary>The dead scientist's name, for the pages and messages.</summary>
    [Serialize] public string Name = "";
    [Serialize(Optional = true)] public ObjectID? Ship;
    /// <summary>The player ship that fights the duel.</summary>
    [Serialize(Optional = true)] public ObjectID? Champion;
    /// <summary>True once the yield page opened.</summary>
    [Serialize(Optional = true)] public bool Hailed;
    [Serialize(Optional = true)] public List<ObjectID> Fleet = [];
}


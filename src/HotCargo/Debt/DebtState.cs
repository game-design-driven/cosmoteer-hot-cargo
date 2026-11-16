using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Debt;

/// <summary>One player's debt to one lender (spec 1401). Times are <see cref="Core.RunState.GameSeconds"/>.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class Debt
{
    [Serialize] public string Faction = "";
    /// <summary>The human player index of the player who owes it.</summary>
    [Serialize(Optional = true)] public int Player;
    [Serialize] public long Principal;
    [Serialize] public long Balance;
    [Serialize] public double NextInterestAt;
    [Serialize] public double NextCollectorAt;
    /// <summary>The highest stage that was announced. It falls with a payment, so a new rise is announced again.</summary>
    [Serialize] public DebtStage Stage;
    /// <summary>Spec 1410: the lender wanted the players at the last tick.</summary>
    [Serialize] public bool WasWanted;
    /// <summary>Spec 1421: the interest of this debt is InterestRate × this. A refinanced debt has a lower factor.</summary>
    [Serialize(Optional = true)] public float RateFactor = 1f;
    /// <summary>Spec 1418: when the lender of an overdraft debt gets in touch, or null when no hail is due.</summary>
    [Serialize(Optional = true)] public double? HailAt;

    public ID<FactionRules> FactionId => new(Faction);
}

/// <summary>A collector ship in this system (spec 1407).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class Collector
{
    [Serialize] public ObjectID Ship;
    [Serialize] public string Faction = "";
    /// <summary>The player who owes the debt that this collector asks for.</summary>
    [Serialize(Optional = true)] public int Player;
    /// <summary>Spec 1418: the collector came because an overdraft debt crossed the hail limit.</summary>
    [Serialize(Optional = true)] public bool Overdraft;
    [Serialize] public bool Hailed;
    [Serialize] public double LeaveAt;
}

/// <summary>Spec 1430 to 1436: the ships that came to help in this system and the damage counts.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HelpGroup
{
    [Serialize] public string Faction = "";
    [Serialize] public List<ObjectID> Ships = [];
    [Serialize] public long HelperDamage;
    [Serialize] public long PlayerDamage;
    /// <summary>When the fight went quiet, or -1 while it goes on.</summary>
    [Serialize] public double CalmSince = -1;
    /// <summary>The helper that asks for pay, set when the fight ended with enough share.</summary>
    [Serialize(Optional = true)] public ObjectID? Hailer;
    [Serialize] public bool Hailed;
    [Serialize] public double LeaveAt;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class DebtState
{
    /// <summary>One debt for each player and lender, in the order they started (a stable order for the tick).</summary>
    [Serialize] public List<Debt> Debts = [];
    [Serialize] public List<Collector> Collectors = [];
    /// <summary>Spec 1422: the systems (map node IDs) that offered a Collect a Debt job.</summary>
    [Serialize(Optional = true)] public List<ObjectID> CollectSystems = [];
    /// <summary>Spec 1430: the systems that rolled for help.</summary>
    [Serialize(Optional = true)] public List<ObjectID> HelpSystems = [];
    [Serialize(Optional = true)] public HelpGroup? Help;

    public Debt? Of(string faction, int player) => Debts.FirstOrDefault(d => d.Faction == faction && d.Player == player);

    public IEnumerable<Debt> OfPlayer(int player) => Debts.Where(d => d.Player == player);

    public long Total => Debts.Sum(d => d.Balance);
}

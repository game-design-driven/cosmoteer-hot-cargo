namespace HotCargo.ShipAi;

/// <summary>Spec 2324. A fact: a yes or no condition, some with a parameter.</summary>
public enum Cond
{
    Always, PartLost, TargetedByEnemy, AllyUnderAttack, EnemyClassNear, CarryingTech, VaultDamaged,
    // Hot Cargo (spec 413)
    HuntersInSystem, LossTimerRunning, EnemyHoldsTech, EnemyCalling,
    // Autonomous ships (spec 1066)
    CargoToSell, StationInRange,
    // Formations (spec 424)
    AllyInLineOfFire,
    // Build ships (spec 1086)
    CanAffordDesign, CanAffordRefit,
    // Flags (spec 2326)
    Flag,
}

/// <summary>Spec 2325. A value that a comparison reads.</summary>
public enum Value
{
    Health, Shields, Ammo, Battery, Cargo, OthersHealth,
    NearestEnemy, Enemies, NearestAlly, NearestFleet, FleetSize,
    NearestLoot, NearestStation, NearestAsteroid, NearestWreck, NearestCrate,
    HeatStage, StrikeIn, Money,
    Resource, Cost, RefitCost, ShipsOf,
}

/// <summary>Spec 402. An action.</summary>
public enum Act
{
    TargetNearest, TargetWeakest, TargetStrongest, TargetAllyAttacker, TargetVaultAttacker, TargetTechHolder, TargetCaller, TargetMyAttacker,
    Kite, CloseIn, HoldPosition, RetreatToAlly, RetreatRepair, FleeJump, FollowLeader, Guard, CollectTech,
    CollectLoot, GoToStation, Mine, Salvage, ReturnToFleet, HoldFormation, KeepSpacing,
    FireAtWill, FocusTarget, TargetWeapons, TargetEngines, PointDefenseOnly, HoldFire, FireDisruptors,
    Face,
    Sell, Buy, Unload, BuildShip, RefitShip,
    SetFlag, ClearFlag,
}

/// <summary>Spec 403: the first matching row of each kind runs; every matching flag row runs.</summary>
public enum ActKind { Target, Move, Weapons, Facing, Trade, Build, Flag }

/// <summary>Spec 2323: the unit of a value. Comparisons of two values need the same unit.</summary>
public enum Unit { Percent, Distance, Seconds, Count, Stage, Money, Amount }

/// <summary>Spec 2322: what a value takes in square brackets.</summary>
public enum ArgKind { None, Resource, Design, DesignResource }

/// <summary>Spec 419: the groups of the condition list and the action list.</summary>
public enum Group { Always, Ship, Enemies, Fleet, World, Run, Cargo, Flags, Target, Move, Fire, Facing, Trade, Build }

/// <summary>What the parameter of a fact or an action means. An enum kind writes the entry's name in the text form (spec 405).</summary>
public enum ParamKind { None, PartKind, ShipClass, WeaponGroup, GuardWho, Side, Station, Spacing, Design, Flag }

public enum PartKind { Weapon, Thruster, Shield, Vault }

/// <summary>Small and Large are relative to the ship's own blueprint cost. Station = a ship without thrusters.</summary>
public enum ShipClass { Small, Large, Station }

public enum GuardWho { Flagship, VaultShip, WeakestAlly }

/// <summary>Spec 415. The side of the ship that faces the target.</summary>
public enum Side { Nose, BroadsideLeft, BroadsideRight, Stern }

/// <summary>Spec 1069. Which trading station "go to station" picks.</summary>
public enum StationPick { Nearest, BestPrice, Cheapest }

public sealed record ValueDef(Value Value, Unit Unit, ArgKind Arg, Group Group);

public sealed record FactDef(Cond Cond, ParamKind Param, Group Group);

public sealed record ActDef(Act Act, ActKind Kind, ParamKind Param, Group Group);

/// <summary>
/// Spec 401, 2325: every fact, value and action, with what it is. The text form, the editor lists, the tooltips (string keys
/// "Cond_", "Value_", "Act_" plus the name, and "Desc_" plus the name) and the controller read only these tables.
/// </summary>
public static class ShipAiRegistry
{
    public static readonly IReadOnlyList<FactDef> Facts =
    [
        new(Cond.Always, ParamKind.None, Group.Always),
        new(Cond.PartLost, ParamKind.PartKind, Group.Ship),
        new(Cond.CarryingTech, ParamKind.None, Group.Ship),
        new(Cond.VaultDamaged, ParamKind.None, Group.Ship),
        new(Cond.TargetedByEnemy, ParamKind.None, Group.Enemies),
        new(Cond.EnemyClassNear, ParamKind.ShipClass, Group.Enemies),
        new(Cond.EnemyHoldsTech, ParamKind.None, Group.Enemies),
        new(Cond.EnemyCalling, ParamKind.None, Group.Enemies),
        new(Cond.AllyUnderAttack, ParamKind.None, Group.Fleet),
        new(Cond.AllyInLineOfFire, ParamKind.None, Group.Fleet),
        new(Cond.StationInRange, ParamKind.None, Group.World),
        new(Cond.HuntersInSystem, ParamKind.None, Group.Run),
        new(Cond.LossTimerRunning, ParamKind.None, Group.Run),
        new(Cond.CargoToSell, ParamKind.None, Group.Cargo),
        new(Cond.CanAffordDesign, ParamKind.Design, Group.Cargo),
        new(Cond.CanAffordRefit, ParamKind.Design, Group.Cargo),
        new(Cond.Flag, ParamKind.Flag, Group.Flags),
    ];

    public static readonly IReadOnlyList<ValueDef> Values =
    [
        new(Value.Health, Unit.Percent, ArgKind.None, Group.Ship),
        new(Value.Shields, Unit.Percent, ArgKind.None, Group.Ship),
        new(Value.Ammo, Unit.Percent, ArgKind.None, Group.Ship),
        new(Value.Battery, Unit.Percent, ArgKind.None, Group.Ship),
        new(Value.Cargo, Unit.Percent, ArgKind.None, Group.Ship),
        new(Value.NearestEnemy, Unit.Distance, ArgKind.None, Group.Enemies),
        new(Value.Enemies, Unit.Count, ArgKind.None, Group.Enemies),
        new(Value.NearestAlly, Unit.Distance, ArgKind.None, Group.Fleet),
        new(Value.NearestFleet, Unit.Distance, ArgKind.None, Group.Fleet),
        new(Value.OthersHealth, Unit.Percent, ArgKind.None, Group.Fleet),
        new(Value.FleetSize, Unit.Count, ArgKind.None, Group.Fleet),
        new(Value.NearestLoot, Unit.Distance, ArgKind.None, Group.World),
        new(Value.NearestStation, Unit.Distance, ArgKind.None, Group.World),
        new(Value.NearestAsteroid, Unit.Distance, ArgKind.None, Group.World),
        new(Value.NearestWreck, Unit.Distance, ArgKind.None, Group.World),
        new(Value.NearestCrate, Unit.Distance, ArgKind.None, Group.World),
        new(Value.HeatStage, Unit.Stage, ArgKind.None, Group.Run),
        new(Value.StrikeIn, Unit.Seconds, ArgKind.None, Group.Run),
        new(Value.Money, Unit.Money, ArgKind.None, Group.Run),
        new(Value.Resource, Unit.Amount, ArgKind.Resource, Group.Cargo),
        new(Value.Cost, Unit.Amount, ArgKind.DesignResource, Group.Cargo),
        new(Value.RefitCost, Unit.Amount, ArgKind.DesignResource, Group.Cargo),
        new(Value.ShipsOf, Unit.Count, ArgKind.Design, Group.Cargo),
    ];

    public static readonly IReadOnlyList<ActDef> Acts =
    [
        new(Act.TargetNearest, ActKind.Target, ParamKind.None, Group.Target),
        new(Act.TargetWeakest, ActKind.Target, ParamKind.None, Group.Target),
        new(Act.TargetStrongest, ActKind.Target, ParamKind.None, Group.Target),
        new(Act.TargetAllyAttacker, ActKind.Target, ParamKind.None, Group.Target),
        new(Act.TargetVaultAttacker, ActKind.Target, ParamKind.None, Group.Target),
        new(Act.TargetTechHolder, ActKind.Target, ParamKind.None, Group.Target),
        new(Act.TargetCaller, ActKind.Target, ParamKind.None, Group.Target),
        new(Act.TargetMyAttacker, ActKind.Target, ParamKind.None, Group.Target),
        new(Act.Kite, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.CloseIn, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.HoldPosition, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.RetreatToAlly, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.RetreatRepair, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.FleeJump, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.FollowLeader, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.Guard, ActKind.Move, ParamKind.GuardWho, Group.Move),
        new(Act.CollectTech, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.CollectLoot, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.GoToStation, ActKind.Move, ParamKind.Station, Group.Move),
        new(Act.Mine, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.Salvage, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.ReturnToFleet, ActKind.Move, ParamKind.None, Group.Move),
        new(Act.HoldFormation, ActKind.Move, ParamKind.Spacing, Group.Move),
        new(Act.KeepSpacing, ActKind.Move, ParamKind.Spacing, Group.Move),
        new(Act.FireAtWill, ActKind.Weapons, ParamKind.WeaponGroup, Group.Fire),
        new(Act.FocusTarget, ActKind.Weapons, ParamKind.WeaponGroup, Group.Fire),
        new(Act.TargetWeapons, ActKind.Weapons, ParamKind.WeaponGroup, Group.Fire),
        new(Act.TargetEngines, ActKind.Weapons, ParamKind.WeaponGroup, Group.Fire),
        new(Act.PointDefenseOnly, ActKind.Weapons, ParamKind.WeaponGroup, Group.Fire),
        new(Act.HoldFire, ActKind.Weapons, ParamKind.WeaponGroup, Group.Fire),
        new(Act.FireDisruptors, ActKind.Weapons, ParamKind.WeaponGroup, Group.Fire),
        new(Act.Face, ActKind.Facing, ParamKind.Side, Group.Facing),
        new(Act.Sell, ActKind.Trade, ParamKind.None, Group.Trade),
        new(Act.Buy, ActKind.Trade, ParamKind.None, Group.Trade),
        new(Act.Unload, ActKind.Trade, ParamKind.None, Group.Trade),
        new(Act.BuildShip, ActKind.Build, ParamKind.Design, Group.Build),
        new(Act.RefitShip, ActKind.Build, ParamKind.Design, Group.Build),
        new(Act.SetFlag, ActKind.Flag, ParamKind.Flag, Group.Flags),
        new(Act.ClearFlag, ActKind.Flag, ParamKind.Flag, Group.Flags),
    ];

    static readonly Dictionary<Cond, FactDef> FactBy = Facts.ToDictionary(f => f.Cond);
    static readonly Dictionary<Value, ValueDef> ValueBy = Values.ToDictionary(v => v.Value);
    static readonly Dictionary<Act, ActDef> ActBy = Acts.ToDictionary(a => a.Act);

    public static FactDef Of(Cond cond) => FactBy.TryGetValue(cond, out var d) ? d : throw new ArgumentOutOfRangeException(nameof(cond), cond, "not in the registry");

    public static ValueDef Of(Value value) => ValueBy.TryGetValue(value, out var d) ? d : throw new ArgumentOutOfRangeException(nameof(value), value, "not in the registry");

    public static ActDef Of(Act act) => ActBy.TryGetValue(act, out var d) ? d : throw new ArgumentOutOfRangeException(nameof(act), act, "not in the registry");

    /// <summary>Spec 418: comparisons of these units get hysteresis.</summary>
    public static bool IsThreshold(Unit unit) => unit is Unit.Percent or Unit.Distance or Unit.Seconds;

    /// <summary>Spec 2323: the allowed numbers of a unit. A number outside is an error in the text form.</summary>
    public static (int Min, int Max) RangeOf(Unit unit) => unit switch
    {
        Unit.Percent => (0, 100),
        Unit.Distance => (0, 20000),
        Unit.Seconds => (0, 3600),
        Unit.Count => (0, 1000),
        Unit.Stage => (0, ShipAiModel.MaxStage),
        Unit.Money => (0, 100_000_000),
        Unit.Amount => (0, 1_000_000),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>Spec 2323: the numbers that the editor offers. stages: the number of heat stages. A row with another number shows it too.</summary>
    public static int[] Choices(Unit unit, int stages) => unit switch
    {
        Unit.Percent => [5, 10, 20, 25, 30, 40, 50, 60, 75, 90, 95],
        Unit.Distance => [100, 150, 200, 300, 500, 800, 1000, 1500, 2000, 3000, 5000, 10000, 20000],
        Unit.Seconds => [10, 20, 30, 60, 90, 120],
        Unit.Count => [0, 1, 2, 3, 4, 5, 6, 8, 10],
        Unit.Stage => Enumerable.Range(0, Math.Clamp(stages, 1, ShipAiModel.MaxStage) + 1).ToArray(),
        Unit.Money => [0, 1000, 5000, 10000, 20000, 50000, 100000],
        Unit.Amount => [0, 1, 5, 10, 20, 50, 100, 200, 500, 1000],
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>The number that a new comparison of a unit starts with.</summary>
    public static int Default(Unit unit) => unit switch
    {
        Unit.Percent => 30,
        Unit.Distance => 1000,
        Unit.Seconds => 30,
        Unit.Count => 2,
        Unit.Stage => 1,
        Unit.Money => 10000,
        Unit.Amount => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>The enum type of an enum parameter kind, or null for a number kind.</summary>
    public static Type? EnumOf(ParamKind kind) => kind switch
    {
        ParamKind.PartKind => typeof(PartKind),
        ParamKind.ShipClass => typeof(ShipClass),
        ParamKind.GuardWho => typeof(GuardWho),
        ParamKind.Side => typeof(Side),
        ParamKind.Station => typeof(StationPick),
        _ => null,
    };

    /// <summary>The allowed values of a parameter kind (an enum kind: 0 to its last entry).</summary>
    public static (int Min, int Max) RangeOf(ParamKind kind) => kind switch
    {
        ParamKind.None => (0, 0),
        ParamKind.WeaponGroup => (0, ShipAiModel.MaxGroup),
        ParamKind.Spacing => (0, 20000),
        ParamKind.Design => (1, Autonomy.BuildModel.MaxDesigns),
        ParamKind.Flag => (1, ShipAiModel.MaxFlags),
        _ when EnumOf(kind) is { } type => (0, Enum.GetValues(type).Length - 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The parameter values that the editor offers.</summary>
    public static int[] Choices(ParamKind kind) => kind switch
    {
        ParamKind.Spacing => [100, 150, 200, 300, 500, 800, 1000],
        _ => Enumerable.Range(RangeOf(kind).Min, RangeOf(kind).Max - RangeOf(kind).Min + 1).ToArray(),
    };

    /// <summary>The parameter that a new fact or action starts with.</summary>
    public static int Default(ParamKind kind) => kind switch
    {
        ParamKind.Spacing => 150,
        _ => RangeOf(kind).Min,
    };
}

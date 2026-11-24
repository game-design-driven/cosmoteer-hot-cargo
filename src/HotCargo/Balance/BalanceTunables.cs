using Halfling.Serialization;

namespace HotCargo.Balance;

/// <summary>Balance numbers that no other area owns (BALANCE.md, spec 300 to 304).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class BalanceTunables
{
    /// <summary>
    /// Spec 304: every station keeps this much fuel per million credits of the next tier's ship value for sale, at least
    /// StationFuelMinimum. The default is one 10 LY relay jump for a fleet of the next tier, so a run never gets stuck on fuel.
    /// </summary>
    [Serialize(Optional = true)] public float StationFuelPerMillion = 100f;
    /// <summary>Spec 304: the smallest fuel floor of a station, for the first tiers.</summary>
    [Serialize(Optional = true)] public int StationFuelMinimum = 10;

    // Spec 1980 to 1982: values that the vanilla rules files hold. Balance/FuelRules.cs writes them into the game rules.
    /// <summary>Spec 300: the price of one hyperium at a station. Stations pay half of it for fuel that players sell. Vanilla: 50.</summary>
    [Serialize(Optional = true)] public int FuelPrice = 150;
    /// <summary>Spec 300: hyperium in one storage tile or one crew trip. Applies at the next new or loaded game. Vanilla: 20.</summary>
    [Serialize(Optional = true)] public int FuelStackSize = 10;
    /// <summary>Spec 1836: units of steel and coil (BuildStackResources) in one storage tile or factory buffer tile. Applies at the next new or loaded game. Vanilla: 40.</summary>
    [Serialize(Optional = true)] public int BuildStackSize = 80;
    /// <summary>Spec 1836: the resources whose stack is BuildStackSize: the build materials that fights drop the most.</summary>
    [Serialize(Optional = true)] public string[] BuildStackResources = ["steel", "coil"];
    /// <summary>Spec 300: hyperium of a jump between systems, per ship mass and light year, at 100 % drive efficiency. Vanilla: 0.001.</summary>
    [Serialize(Optional = true)] public float MapJumpFuelCostPerMassDistance = 0.002f;
    /// <summary>Spec 300: hyperium of a jump inside a system, per ship mass and distance. Vanilla: 0.0000004.</summary>
    [Serialize(Optional = true)] public float LocalJumpFuelCostPerMassDistance = 0.0000008f;
    /// <summary>Spec 300: share of a station's hyperium tiles that hold fuel for sale, at least and at most. Vanilla: 0.8 to 0.9.</summary>
    [Serialize(Optional = true)] public float StationFuelTypedMin = 0.25f, StationFuelTypedMax = 0.4f;
    /// <summary>Spec 300: share of a station's other storage tiles that hold fuel for sale, at least and at most. Vanilla: 0 to 0.1.</summary>
    [Serialize(Optional = true)] public float StationFuelUntypedMin = 0f, StationFuelUntypedMax = 0.02f;
}

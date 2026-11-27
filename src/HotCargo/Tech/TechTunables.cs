using Halfling.Serialization;

namespace HotCargo.Tech;

[ReflectiveSerialization(Explicit = true)]
public sealed class TechTunables
{
    /// <summary>Spec 17, 1541: stolen tech units of a new run. The vaults that a starter ship needs come from it: StartingTechUnits / units per vault, rounded up.</summary>
    [Serialize(Optional = true)] public int StartingTechUnits = 8;
    /// <summary>Spec 1015: research (career tech IDs) that a new run starts with. Default: the small FTL drive.</summary>
    [Serialize(Optional = true)] public string[] StartingResearch = ["cosmoteer.hyperdrive_small"];
    /// <summary>
    /// Spec 19: tech value in fractions of the highest ship value of the peak tier (vanilla TierValueMaximums).
    /// Defaults: 0.25 of a ship at the start, about 0.5 mid-run, 0.8 at the Buyer of a 6-step run (BALANCE.md, finding 12), so 2 units
    /// buy a full ship of the tier.
    /// </summary>
    [Serialize(Optional = true)] public float TechShipFraction = 0.25f;
    [Serialize(Optional = true)] public float TechShipFractionPerGameHour = 0.14f;
    [Serialize(Optional = true)] public float TechShipFractionPer100Notoriety = 0.07f;
    /// <summary>The price changes in steps of this many game minutes, so that a sale in flight sees the same price on all clients.</summary>
    [Serialize(Optional = true)] public float PriceStepMinutes = 1f;
    /// <summary>The price changes in steps of this much notoriety, for the same reason.</summary>
    [Serialize(Optional = true)] public float NotorietyStep = 5f;
    /// <summary>Station buy-back price = sell price × this factor. Must be above 1 to stop a sell-and-buy-back money loop.</summary>
    [Serialize(Optional = true)] public float BuyBackFactor = 1.5f;
    /// <summary>Spec 12, 443: game seconds of the loss timer. When the fleet holds no tech, the players have this long to get tech back.</summary>
    [Serialize(Optional = true)] public float LossGraceSeconds = 180f;
    /// <summary>Spec 445: chance that a destroyed vault burns its tech instead of dropping it as crates.</summary>
    [Serialize(Optional = true)] public float VaultTechLossChance = 0.1f;
    /// <summary>Spec 446: chance that a hunter goes for a tech crate it sees. Rolled once for each hunter and crate.</summary>
    [Serialize(Optional = true)] public float HunterGrabChance = 0.8f;
    /// <summary>Spec 446: hunters see tech crates within this distance.</summary>
    [Serialize(Optional = true)] public float HunterGrabRange = 3000f;
    /// <summary>Spec 446: a hunter takes a crate within this distance of its hull.</summary>
    [Serialize(Optional = true)] public float HunterGrabReach = 40f;
    /// <summary>Spec 444: map markers for tech in the system (tech held on ship blips, crates in space).</summary>
    [Serialize(Optional = true)] public bool TechMarkers = true;
    /// <summary>Spec 1547: game seconds that tech lasts outside a vault (normal storage, crew hands). Then it breaks down in air and heat. 0 turns decay off.</summary>
    [Serialize(Optional = true)] public float DecaySeconds = 360f;
    /// <summary>Spec 1548: what a vault does to the decay of tech that comes back into it. Pause: the decay stops and goes on later. Reset: the tech recovers fully.</summary>
    [Serialize(Optional = true)] public DecayInVault DecayInVault = DecayInVault.Pause;
}

public static class TechTunablesExtensions
{
    /// <summary>
    /// Spec 19 and 683: the tech value of one unit now, times the difficulty tech factor. Every input is saved sim state or a confirmed
    /// difficulty level, so all clients compute the same value.
    /// </summary>
    public static long TechValue(this Core.Run run) => (long)run.TechValueTerms().Total;

    /// <summary>Spec 19, 683, 2583: the steps of the tech value: the model's steps, then the difficulty tech factor and the round to 100.</summary>
    public static Core.Breakdown TechValueTerms(this Core.Run run)
    {
        var (t, s) = (run.T.Tech, run.State);
        return TechModel.ValueTerms(Core.GameData.TierValueMaximum(s.PeakTier), s.PeakTier, t.TechShipFraction, t.TechShipFractionPerGameHour,
                t.TechShipFractionPer100Notoriety, t.PriceStepMinutes, t.NotorietyStep, s.GameSeconds, s.Notoriety)
            .Times("TechDifficulty", RunModes.Difficulty.Pressure(run).TechValue, RunModes.Difficulty.LevelNames(run.Mode))
            .Round("Round", 100);
    }
}

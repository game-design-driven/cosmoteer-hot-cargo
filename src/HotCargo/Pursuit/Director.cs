using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Pursuit;

/// <summary>Spec 900 to 904: hunter waves come weaker and less often early, and the fleet value moves their strength a little.</summary>
public static class Director
{
    static float Progress(Run run) => WaveModel.Progress(run.State.SectorIndex, run.T.Journey.RunSectorCount);

    /// <summary>Spec 901: the ramp factor on hunter ships for the sector index.</summary>
    public static float RampFactor(Run run) => PursuitModel.Lerp(run.T.Pursuit.EarlyShipFactor, run.T.Pursuit.LateShipFactor, Progress(run));

    /// <summary>Spec 901: the factor on the repeat Strike wave interval.</summary>
    public static float IntervalFactor(Run run) =>
        run.T.Pursuit.Enabled
            // Spec 1727: a storyteller pressure above 1 shortens the interval, below 1 lengthens it.
            ? PursuitModel.Lerp(run.T.Pursuit.EarlyIntervalFactor, run.T.Pursuit.LateIntervalFactor, Progress(run)) / Storyteller.StorytellerArea.Pressure(run)
            : 1f;

    /// <summary>
    /// Spec 902: the value of the human fleet in this system per human player (vanilla BasePhysicalValue, so damage counts), against
    /// FleetReferenceShare × the ship value maximum of the hunter tier. Co-op already scales the count (spec 334), so the value is per player.
    /// </summary>
    public static float FleetFactor(Run run, SimRoot sim)
    {
        var t = run.T.Pursuit;
        long value = 0;
        foreach (var s in sim.Ships.HumanPlayerSignificantShips) value += s.BasePhysicalValue;
        int players = Math.Max(1, Scaling.EnemyScale.Players(run.Game));
        double reference = GameData.TierValueMaximum(HunterSpawner.BaseTier(run)) * (double)t.FleetReferenceShare;
        return PursuitModel.FleetFactor(value / (double)players, reference, t.FleetFactorPerDoubling, t.FleetFactorMax);
    }

    /// <summary>For tests and logs: the fleet factor × the storyteller pressure of the last stage response. Not saved.</summary>
    internal static float LastMix { get; private set; } = float.NaN;

    /// <summary>
    /// Spec 901, 902, 1726: hunter ships of a stage response × ramp × (fleet factor × storyteller pressure, clamped). Sim logic: it rolls
    /// sim.Rand for a fraction.
    /// </summary>
    internal static int ScaleShips(Run run, SimRoot sim, int ships)
    {
        if (!run.T.Pursuit.Enabled) return ships;
        var st = run.T.Storyteller;
        float ramp = RampFactor(run), fleet = FleetFactor(run, sim), story = Storyteller.StorytellerArea.Pressure(run);
        float mix = Storyteller.StorytellerModel.Combined(fleet, story, st.CombinedMin, st.CombinedMax);
        LastMix = mix;
        int scaled = PursuitModel.ScaleCount(ships, ramp * mix, sim.Rand.Double);
        Log.Info($"pursuit: hunter ships {ships} x ramp {ramp:0.00} x fleet {fleet:0.00} x story {story:0.00} (together {mix:0.00}) = {scaled}");
        return scaled;
    }
}

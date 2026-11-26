using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Diplomacy;

namespace HotCargo.Events;

/// <summary>
/// Spec 2500 to 2502: the run remembers past acts of the players, so a later callback page can name them. Sim logic only: records come
/// from the shared choice inputs (diplomacy) and the sim tick (missions).
/// </summary>
public static class Memories
{
    internal static void Register()
    {
        Deals.Spared += (run, sim, ai) => Record(run, sim, MemoryModel.SparedKind(Situation.IsHunter(ai), Situation.Faction(ai) is not null), ai);
        Deals.Decided += OnDecided;
    }

    /// <summary>Spec 2501: a pay-off or a bluff that a hunter took.</summary>
    static void OnDecided(Run run, SimRoot sim, Deals.DealResult d)
    {
        if (d.Accepted != true) return;
        var kind = d.Offer switch
        {
            Offer.PayOff => MemoryKind.PaidOff,
            Offer.Bluff => MemoryKind.Bluffed,
            _ => MemoryKind.None,
        };
        Record(run, sim, kind, d.Callee);
    }

    /// <summary>Remembers an act with the other ship. Kind None remembers nothing (a hunter's surrender, an offer that leaves no trace).</summary>
    public static void Record(Run run, SimRoot sim, MemoryKind kind, Ship subject)
    {
        if (kind == MemoryKind.None) return;
        // The team, not the player index: a truce (a surrender) negates the player index of the ship.
        Record(run, kind, subject.Metadata.PlainShipName, Situation.Faction(subject)?.ID.ToString() ?? "",
            sim.MapNode?.Name ?? throw new InvalidOperationException("sim has no map node"), run.State.SectorIndex);
    }

    internal static void Record(Run run, MemoryKind kind, string ship, string faction, string system, int sector)
    {
        if (kind == MemoryKind.None) throw new ArgumentOutOfRangeException(nameof(kind), kind, "a memory needs a kind");
        run.State.Events.Memories.Add(new Memory { Kind = kind, Ship = ship, Faction = faction, System = system, Sector = sector });
        Log.Info($"memory: {kind} {ship} ({(faction.Length > 0 ? faction : "no faction")}) in {system}, sector {sector}");
    }

    /// <summary>Spec 2503: the index of the memory that a callback of this kind recalls now, or -1.</summary>
    internal static int Pick(Run run, MemoryKind kind) =>
        MemoryModel.Pick([.. run.State.Events.Memories.Select(m => m.Facts)], kind, run.State.SectorIndex, run.T.Events.CallbackMinSectors);
}

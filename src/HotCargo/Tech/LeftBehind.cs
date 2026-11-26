using Cosmoteer.Crew;
using Cosmoteer.Localization;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 2520 to 2529: tech that stays in a system when the players move to another system is lost. The system switch removes it
/// from the old sim (Run.Hooks.SystemLeaving) and archives that sim again, so a return finds none. Warnings read <see cref="Split"/>.
/// </summary>
public static class LeftBehind
{
    /// <summary>
    /// Spec 2520: the players' tech in this system that stays, and the tech that comes along, when only the ships in <paramref name="jumps"/>
    /// go. Stays: tech of the other human and derelict ships, crew hands outside the jumping ships, crates, and tech that friendly ships and
    /// hunters in this system hold. Reads sim and run state only, so the GUI may call it.
    /// </summary>
    public static (int Stays, int Goes) Split(Run run, SimRoot sim, Func<Ship, bool> jumps)
    {
        int stays = 0, goes = 0;
        foreach (var ship in sim.Ships.AllShips.Where(s => Vaults.HoldsForPlayers(sim, s)))
            if (jumps(ship)) goes += Vaults.ShipTech(ship); else stays += Vaults.ShipTech(ship);
        foreach (var crew in sim.Crew._allSouls.Where(c => Vaults.CarriesForPlayers(sim, c)))
        {
            int units = crew.CarriedResource.QuantityOfType(Vaults.TechId);
            if (crew.ShipCrew?.Ship is { } ship && jumps(ship)) goes += units; else stays += units;
        }
        var node = sim.NodeId();
        stays += Vaults.CrateUnits(sim)
            + run.State.Diplomacy.Held.Where(h => h.Node == node).Sum(h => h.Units)
            + run.State.Tech.HunterHeld.Where(h => h.Node == node).Sum(h => h.Units);
        return (stays, goes);
    }

    /// <summary>Spec 2522: the tech that a switch without a jump (the vanilla map switch, the recall) leaves in this system.</summary>
    public static int OnSwitch(Run run, SimRoot sim) => Split(run, sim, _ => false).Stays;

    /// <summary>Units lost by this rule since start. For autotests.</summary>
    internal static int Lost;

    /// <summary>
    /// Spec 2521: removes every unit of <see cref="Split"/> that stays from the old sim and the run state, in a stable order, and
    /// archives the old sim again when it changed. Runs on every machine at the same point of the switch (Run.Hooks.SystemLeaving).
    /// </summary>
    internal static void OnSystemLeaving(Run run, SimRoot old)
    {
        var node = old.NodeId();
        string system = (old.MapNode ?? throw new InvalidOperationException("the old sim has no map node")).Name;
        int units = 0;
        foreach (var ship in old.Ships.AllShips.Where(s => Vaults.HoldsForPlayers(old, s)).OrderBy(s => s.UniqueID))
        {
            foreach (var grid in Vaults.OnShip(ship))
            {
                units += grid.Resources;
                grid.AddResources(-grid.Resources);
            }
            foreach (var tile in Vaults.StorageTiles(ship).Where(t => t.ResourceType == Vaults.TechId))
                units += ((IResourceSource)tile).SubtractResources(tile.Resources);
        }
        foreach (var crew in old.Crew._allSouls.Where(c => Vaults.CarriesForPlayers(old, c)).OrderBy(c => c.UniqueID))
        {
            int carried = crew.CarriedResource.QuantityOfType(Vaults.TechId);
            if (carried == 0) continue;
            units += carried;
            crew.CarriedResource = default;
        }
        for (int i = old.Nuggets.Count - 1; i >= 0; i--)
        {
            var n = old.Nuggets[i];
            if (n.ResourceType != Vaults.TechId) continue;
            units += n.Quantity;
            old.Nuggets.RemoveNugget(n);
        }
        bool simChanged = units > 0;
        // Waiting tech of a ship that is still in the old sim stays with it. A ship that jumped is in the new sim, and its entry goes along.
        units += Take(run.State.Tech.Waiting, w => old.Ships[w.Ship] is not null, w => w.Units);
        units += Take(run.State.Tech.HunterHeld, h => h.Node == node, h => h.Units);
        units += Take(run.State.Diplomacy.Held, h => h.Node == node, h => h.Units);
        if (units == 0) return;

        if (simChanged)
        {
            // The switch archived the old sim before this hook (GameRoot.JumpToNode), so the copy without the tech replaces it.
            // Limit: vanilla archives on a worker thread, this second archive runs on the main thread, so such a switch takes a little
            // longer. Revisit if a jump that leaves tech behind stutters in a large system.
            run.Game.SimArchive.RemoveSim(old.MapNode);
            run.Game.SimArchive.SaveSim(old.MapNode, old);
        }
        Lost += units;
        Log.Info($"left behind: {units} tech stayed in {system} and is lost");
        if (Telemetry.Tele.On(Telemetry.Cat.Tech)) Telemetry.Session.Current?.AddTechHint("left_behind");
        string line = Strings.FormatText("HotCargoTech/LeftBehindFmt", units, Halfling.StringTools.EscapeForXml(system));
        Onboarding.RunLog.Add(run, Onboarding.RunLogKind.Loss, line);
        // The new sim shows after the switch, so the toast and the chat line wait for the next frame.
        Run.OnNextFrame(() =>
        {
            Ux.UxKit.CenterText(run.Game, line);
            Ux.UxKit.ChatLine(run.Game, line);
        });
    }

    /// <summary>Removes the entries that match and returns their units.</summary>
    static int Take<T>(List<T> entries, Func<T, bool> stays, Func<T, int> units)
    {
        int sum = entries.Where(stays).Sum(units);
        entries.RemoveAll(e => stays(e));
        return sum;
    }
}

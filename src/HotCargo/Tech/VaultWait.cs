using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>Spec 1554: tech of a vault that crew took apart, waiting on its ship for room in a vault or normal storage.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class WaitingTech
{
    [Serialize] public ObjectID Ship;
    [Serialize] public string ShipName = "";
    /// <summary>The system where the ship was last seen. A move to another system loses the entries of the ships that stay (spec 2520).</summary>
    [Serialize] public ObjectID Node;
    [Serialize] public int Units;
}

/// <summary>
/// Spec 1554: a vault taken apart (a move or a rebuild) never drops its tech. Tech with no room waits on the ship and goes into
/// the first vault of the ship with room, usually the rebuilt vault. While no vault is pending (Vaults.VaultPending), it goes into
/// normal storage with room too. Waiting tech counts as the ship's tech (Vaults.ShipTech) and does not decay. Sim state only: changed in the sim tick and in the take-apart (VaultDropPatch).
/// Limit: no time limit. A player who never lets crew build the pending vault keeps the tech safe from vault hits. Revisit if a playtest shows it.
/// </summary>
public static class VaultWait
{
    /// <summary>Waiting units of one ship. Reads the run state, so the GUI may call it.</summary>
    public static int Units(Ship ship) =>
        Run.Current is { } run ? run.State.Tech.Waiting.Where(w => w.Ship == ship.UniqueID).Sum(w => w.Units) : 0;

    internal static void Add(Run run, Ship ship, int units)
    {
        if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
        var waiting = run.State.Tech.Waiting;
        var entry = waiting.FirstOrDefault(w => w.Ship == ship.UniqueID);
        if (entry is null)
            waiting.Add(entry = new WaitingTech { Ship = ship.UniqueID, ShipName = ship.Metadata.PlainShipName });
        entry.Node = ship.Sim!.NodeId();
        entry.Units += units;
    }

    /// <summary>
    /// Waiting tech goes aboard when its ship has room. A ship that became a wreck spills it as crates; a ship that is gone from its
    /// system (destroyed to the last part) takes it. A ship in jump limbo or in stasis keeps it. LeftBehind removes the entries of the ships that stay when the players leave the system.
    /// Both cases warn in red: only there tech leaves the ship.
    /// </summary>
    internal static void OnTick(Run run, SimRoot sim, double dt)
    {
        var waiting = run.State.Tech.Waiting;
        if (waiting.Count == 0) return;
        var node = sim.NodeId();
        for (int i = 0; i < waiting.Count; i++)
        {
            var w = waiting[i];
            var ship = sim.Ships[w.Ship];
            if (ship is not null && ship.ExistsInSim(sim) && !ship.IsJunk)
            {
                w.Node = node;
                var open = Vaults.LiveVaults(ship).Where(g => g.Resources < g.MaxResources).ToList();
                int moved = Vaults.Add(open, w.Units);
                if (!Vaults.VaultPending(ship)) moved += Vaults.AddToStorage([ship], w.Units - moved);
                if (moved == 0) continue;
                w.Units -= moved;
                Log.Info($"{moved} waiting tech went aboard {w.ShipName} (vaults with room: {string.Join(" ", open.Select(g => g.Part!.Location))}), {w.Units} still wait");
                if (w.Units == 0) waiting.RemoveAt(i--);
                continue;
            }
            if (ship is not null && ship.IsJunk)
            {
                HunterGrab.Drop(sim, ship.DetWorldCenter, w.Units);
                Log.Info($"{w.ShipName} is a wreck: {w.Units} waiting tech dropped as crates");
                Warn(run, Strings.FormatText("HotCargoTech/VaultWaitWreckFmt", w.Units, w.ShipName));
                waiting.RemoveAt(i--);
                continue;
            }
            if (!w.Node.Equals(node) || run.Game.ShipsInJumpLimbo.Any(s => s.UniqueID == w.Ship) || sim.Stasis.Any(st => st.ReservedObjectID == w.Ship)) continue;
            Log.Info($"{w.ShipName} is gone with {w.Units} waiting tech");
            Warn(run, Strings.FormatText("HotCargoTech/VaultWaitGoneFmt", w.Units, w.ShipName));
            waiting.RemoveAt(i--);
        }
    }

    /// <summary>The sim tick can run on a worker thread, so the toast and the chat line wait for the next frame.</summary>
    static void Warn(Run run, string text) => Run.OnNextFrame(() =>
    {
        Ux.UxKit.CenterText(run.Game, text);
        Ux.UxKit.ChatLine(run.Game, text);
    });
}

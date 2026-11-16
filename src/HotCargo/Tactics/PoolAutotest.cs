using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Halfling.Geometry;
using HotCargo.Dev;

namespace HotCargo.Tactics;

/// <summary>Autotest commands and fields for co-op resource sharing (spec 1318-1320). Change commands run in the sim tick ("mp").</summary>
static class PoolAutotest
{
    static readonly ID<ResourceRules> Steel = new("steel");
    static int _marked;

    public static void Register()
    {
        Autotest.Fields["share_on"] = () => Autotest.RequireRun().State.SharedResources ? 1 : 0;
        // 1 while the local switch shows ON, 0 while it shows OFF, -1 while the players cannot see it (hidden, off the screen, or under a
        // vanilla panel or an open window). share_hud_report logs where it is and what covers it.
        Autotest.Fields["share_hud"] = () => ShareResourcesHud.Label is not { } label ? -1 : label == Ai.AiText.Text("ShareButtonOn") ? 1 : 0;
        Autotest.Commands["share_hud_report"] = _ => Log.Info($"AUTOTEST share switch: {ShareResourcesHud.Report()}");
        foreach (int p in new[] { 0, 1 })
        {
            int player = p;
            Autotest.Fields[$"share_p{player}_steel"] = () => First(player).Resources.GetResourceTotal(Steel, includeStored: true, includeCarried: false);
            Autotest.Fields[$"share_p{player}_usable_steel"] = () => First(player).Resources.GetUsableConstructionResourceTotal(Steel, includeAnticipated: false);
            // Queued repairs and rebuilds: queuing a rebuild removes the destroyed-part mark at once, so this shows when the work is done.
            Autotest.Fields[$"share_p{player}_queued"] = () => Resupply.QueuedRepairs(First(player));
            Autotest.Fields[$"share_p{player}_blocked"] = () => First(player).OptionalConstructionManager?.HasAnyBlocked == true ? 1 : 0;
        }
        // share_mark: remembers player 1's usable steel; share_p1_usable_gain is the change since. Loose resources near the ship
        // count as usable too, so the step compares before and after the switch.
        Autotest.Commands["share_mark"] = _ => _marked = First(1).Resources.GetUsableConstructionResourceTotal(Steel, includeAnticipated: false);
        Autotest.Fields["share_p1_usable_gain"] = () => First(1).Resources.GetUsableConstructionResourceTotal(Steel, includeAnticipated: false) - _marked;
        // share on|off: the local player flips the switch with the real MP input.
        Autotest.Commands["share"] = arg => Autotest.RequireRun().Game.NetManager.EnqueueInput(new ShareResourcesInput(arg.Trim() == "on"));
        // share_steel <player> <n>: the player's first ship holds exactly n steel in storage.
        Autotest.Commands["share_steel"] = arg =>
        {
            var p = Args(arg, 2);
            var ship = First(int.Parse(p[0], CultureInfo.InvariantCulture));
            ship.Resources.RemoveResources(Steel, ship.Resources.GetResourceTotal(Steel, includeStored: true, includeCarried: false));
            int n = int.Parse(p[1], CultureInfo.InvariantCulture);
            int added = ship.Resources.AddResources(Steel, n, excludeConstructionSinks: true);
            if (added != n) throw new InvalidOperationException($"{ship.Metadata.PlainShipName} took {added} of {n} steel");
        };
        // share_clear: removes the loose steel within 1,000 of player 0's first ship, so a build can only use ship stock.
        Autotest.Commands["share_clear"] = _ => ClearSteel();
        // share_near <player>: moves the player's first ship next to player 0's first ship, well inside the pool range (400).
        Autotest.Commands["share_near"] = arg =>
        {
            var ship = First(int.Parse(arg.Trim(), CultureInfo.InvariantCulture));
            var host = First(0);
            Autotest.Teleport(ship, Autotest.ClearSpotNear(ship.Sim!, host.DetWorldCenter, host.WorldBoundingRadius + ship.WorldBoundingRadius + 100f, ship.WorldBoundingRadius));
        };
        // share_break <player>: destroys the outermost part of the player's first ship that costs steel (not a control room, reactor,
        // crew part or the tech vault) and queues its rebuild, like a player's repair order. The outermost part is a leaf, so the
        // ship does not split.
        Autotest.Commands["share_break"] = arg =>
        {
            var ship = First(int.Parse(arg.Trim(), CultureInfo.InvariantCulture));
            var center = ship.DetWorldCenter;
            var keep = new[] { "command", "reactor", "crew" }.Select(c => new ID<Cosmoteer.Ships.Parts.PartCategory>(c)).ToList();
            var part = ship.Parts.Where(x => x.Rules.Resources.Any(r => r.Type == Steel) && x.Rules.ID != Tech.Vaults.VaultId
                    && !keep.Any(c => x.Rules.TypeCategories.Contains(c)))
                .OrderByDescending(x => x.DetWorldCenter.DistanceSquaredTo(center)).ThenBy(x => x.Location.X).ThenBy(x => x.Location.Y).FirstOrDefault()
                ?? throw new InvalidOperationException($"{ship.Metadata.PlainShipName} has no part to break");
            var at = part.Location;
            part.Health = 0;
            // The destroyed part drops its steel beside the ship; the rebuild would take that first.
            ClearSteel();
            var destroyed = ship.DestroyedParts.FirstOrDefault(d => d.Location == at) ?? throw new InvalidOperationException($"no destroyed part at {at}");
            using var info = ShipUpdateInfo.Alloc(ship);
            if (!info.TryRebuildPart(destroyed, rebuildDoors: true, hireCrew: false, out _) || !info.CanQueuePhysical())
                throw new InvalidOperationException($"cannot queue the rebuild at {at}");
            info.QueuePhysical(updateDestroyed: true, isRepair: true, allowPartialConstruction: true);
            Log.Info($"AUTOTEST share_break: {ship.Metadata.PlainShipName} rebuilds {part.Rules.ID} at {at}");
        };
    }

    static void ClearSteel()
    {
        var sim = Autotest.RequireRun().Game.Sim;
        var found = new List<Cosmoteer.Resources.Nugget>();
        sim.Nuggets.DetFindIntersectingWith(Steel, new Circle(First(0).DetWorldCenter, 1000f), found);
        foreach (var n in found.OrderBy(n => n.UniqueID)) sim.Nuggets.RemoveNugget(n);
        Log.Info($"AUTOTEST share_clear: {found.Count} steel nuggets removed");
    }

    /// <summary>The first ship (lowest ID) of a human player index.</summary>
    static Ship First(int player) => Autotest.RequireRun().Game.Sim.Ships.HumanPlayerShips.Where(s => s.Metadata.PlayerIndex == player)
        .OrderBy(s => s.UniqueID).FirstOrDefault() ?? throw new InvalidOperationException($"player {player} has no ship");

    static string[] Args(string arg, int count)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == count ? p : throw new InvalidOperationException($"needs {count} arguments");
    }
}

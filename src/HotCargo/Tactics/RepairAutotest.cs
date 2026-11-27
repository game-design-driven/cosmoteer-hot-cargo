using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships.Parts.UI;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using HotCargo.Ai;
using HotCargo.Dev;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

/// <summary>Autotest commands and fields for the repair plans of fall back (spec 1305-1313). Test only: single player.</summary>
static class RepairAutotest
{
    public static void Register()
    {
        foreach (var plan in Enum.GetValues<RepairPlan>().Where(p => p != RepairPlan.None))
            Autotest.Fields[$"ai_fb_{plan.ToString().ToLowerInvariant()}"] = () => Modules().Count(m => m.Plan == plan);
        foreach (string name in new[] { Resupply.SupplyEvent, Resupply.RepairHoldEvent })
            Autotest.Fields[$"tactic_{name}"] = () => TacticsLog.Count(name);
        Autotest.Fields["ai_fb_active"] = () => Modules().Count(m => m.Plan != RepairPlan.None);
        Autotest.Fields["ai_fb_in_cover"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<FallBackStrategy>()?.InCover(s) == true);
        // Fleeing test ships that checked for a station (spec 1314), and those that picked one, also one lost since.
        Autotest.Fields["ai_flee_checked"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<Ai.FleeStrategy>() is { Fleeing: true, Checked: true });
        Autotest.Fields["ai_flee_picked"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<Ai.FleeStrategy>() is { PickedStation: true });
        Autotest.Fields["ai_flee_to_station"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<Ai.FleeStrategy>()?.StationCenter(s) is not null);
        Autotest.Fields["ai_flee_supplied"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<Ai.FleeStrategy>() is { Supplied: true });
        Autotest.Fields["ai_fb_supplied"] = () => Modules().Count(m => m.Supplied);
        // The lowest health fraction (spec 47) and the lowest crew count of the ships of the last aispawn.
        // ai_spawned_diag: logs what became of each ship that aispawn spawned: live, a wreck, in stasis or gone. Test only.
        Autotest.Commands["ai_spawned_diag"] = _ =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            foreach (var id in PersonalityAutotest.LastSpawned)
            {
                string state = sim.Ships[id] is { } ship
                    ? $"{(ship.IsJunk ? "wreck" : "live")} {ship.Metadata.PlainShipName} parts={ship.Parts.Count} crew={ship.Crew.Assigned.Count} health={HunterOrders.HealthFraction(ship):0.00}"
                    : sim.Stasis.Any(st => st.ReservedObjectID == id) ? "in stasis" : "gone";
                Log.Info($"AUTOTEST spawned {id}: {state}");
                if (sim.Ships[id] is not { } at) continue;
                foreach (var near in sim.Ships.Where(o => o != at && !o.IsJunk && o.DetWorldCenter.DistanceTo(at.DetWorldCenter) < 2000f).OrderBy(o => o.UniqueID))
                    Log.Info($"AUTOTEST   near: {near.Metadata.PlainShipName} player={near.Metadata.PlayerIndex} at {near.DetWorldCenter.DistanceTo(at.DetWorldCenter):0} " +
                             $"target={(near.Commands.CurConfirmedCommand as Cosmoteer.Ships.Commands.AttackCommand)?.Target?.Metadata.PlainShipName ?? "-"} enemy={near.IsEnemiesWith(at)}");
            }
        };
        Autotest.Fields["ai_new_min_health"] = () => New().Select(s => (double)HunterOrders.HealthFraction(s)).DefaultIfEmpty(-1).Min();
        Autotest.Fields["ai_new_first_health"] = () => New().Select(s => (double)HunterOrders.HealthFraction(s)).DefaultIfEmpty(-1).First();
        Autotest.Fields["ai_new_first_jobs"] = () => New().Select(s => (double)Resupply.QueuedRepairs(s)).DefaultIfEmpty(-1).First();
        // aimark_jobs: remembers the queued repairs of the first aispawn ship; ai_new_first_jobs_done counts those done since.
        Autotest.Commands["aimark_jobs"] = _ => _markedJobs = Resupply.QueuedRepairs(At("0"));
        Autotest.Fields["ai_new_first_jobs_done"] = () => _markedJobs - Resupply.QueuedRepairs(At("0"));
        Autotest.Fields["ai_new_first_in_combat"] = () => New().Select(s => Resupply.InCombat(s) ? 1.0 : 0.0).DefaultIfEmpty(-1).First();
        // aialone: the ships of the last aispawns are alone in the system. Every other ship, live or in stasis, that is an enemy of the
        // players or of a test ship, or shares a player index with a test ship, leaves the system; stations stay. aispawn of a faction starts a war, so
        // every patrol of that faction turns hostile, and where the patrols sit depends on the test world (CI full suites, #184).
        // Sim state, so in co-op run it with "mp". Test only.
        Autotest.Commands["aialone"] = _ =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            var mode = sim.Mode;
            var test = PersonalityAutotest.LastSpawned.ToHashSet();
            var sides = test.Select(id => sim.Ships[id]).OfType<Ship>().Select(s => s.Metadata.PlayerIndex).ToHashSet();
            var humans = Autotest.RequireRun().Game.HumanPlayersIndexes;
            // Enemies of the test ships too: on Linux CI a ship of a third side, at war with the test's faction, shot the test ship while it
            // repaired and made it a wreck (the pool test with an Iota, #188).
            bool Other(int index) => sides.Contains(index) || humans.Any(h => mode.AreEnemies(h, index)) || sides.Any(t => mode.AreEnemies(t, index));
            var live = sim.Ships.Where(s => !test.Contains(s.UniqueID) && !s.IsHumanPlayerShip && !s.IsJunk && !Diplomacy.Situation.IsStation(s)
                && Other(s.Metadata.PlayerIndex)).OrderBy(s => s.UniqueID).ToList();
            foreach (var s in live) sim.Ships.Remove(s, dispose: true, handleLostShip: false);
            var stasis = sim.Stasis.OfType<Cosmoteer.Simulation.Stasis.StasisShipSpawner>()
                .Where(s => !test.Contains(s.ReservedObjectID) && (s.Features & (Cosmoteer.Generators.Simulation.SpawnedShipFeatures.Station | Cosmoteer.Generators.Simulation.SpawnedShipFeatures.OperationalStation
                        | Cosmoteer.Generators.Simulation.SpawnedShipFeatures.Landmark)) == 0 && Other(s.ObjectAllegiance))
                .OrderBy(s => s.ReservedObjectID).ToList();
            foreach (var s in stasis) sim.Stasis.RemoveStasisSpawner(s);
            Log.Info($"AUTOTEST aialone: {live.Count} live and {stasis.Count} stasis ships left the system");
        };
        // 2: the first aispawn ship is alive, 1: it is a wreck (junk), 0: it left the sim.
        Autotest.Fields["ai_new_first_state"] = () =>
            PersonalityAutotest.LastSpawned.Count == 0 || Autotest.RequireRun().Game.Sim.Ships[PersonalityAutotest.LastSpawned[0]] is not { } s ? 0 : s.IsJunk ? 1 : 2;
        Autotest.Fields["ai_new_min_crew"] = () => New().Select(s => (double)s.Crew.Assigned.Count).DefaultIfEmpty(-1).Min();

        // aidamage <index>|new <fraction>: sets every part of ship <index> of the last aispawn to this fraction of its health.
        Autotest.Commands["aidamage"] = arg =>
        {
            var (ships, value) = Target(arg);
            float fraction = float.Parse(value, CultureInfo.InvariantCulture);
            foreach (var ship in ships)
                foreach (var part in ship.Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * fraction));
        };
        // aistrip <index>|new resources|crew <keep>: removes every stored resource, or the crew down to <keep>, of ship <index> of the last aispawn.
        // A ship without crew runs no AI at all (ShipAI.FixedUpdate needs Commands.CanExecuteCommands), so a test keeps a few.
        Autotest.Commands["aistrip"] = arg =>
        {
            var (ships, what) = Target(arg);
            foreach (var ship in ships)
            {
                if (what.StartsWith("crew ", StringComparison.Ordinal))
                    ship.Crew.DeleteRandomAssigned(Math.Max(0, ship.Crew.Assigned.Count - int.Parse(what[5..], CultureInfo.InvariantCulture)));
                else if (what == "resources")
                    foreach (var type in GameApp.Rules.Resources.Select(r => r.ID))
                        ship.Resources.RemoveResources(type, ship.Resources.GetResourceTotal(type, includeStored: true, includeCarried: false));
                else throw new InvalidOperationException("aistrip needs: index|new resources|crew <keep>");
                Log.Info($"AUTOTEST stripped {what} of {ship.Metadata.PlainShipName}: crew {ship.Crew.Assigned.Count}");
            }
        };
        // ainew_wreck <i> <distance>: a junk copy of the player ship's design at a clear spot this far from ship i (spec 1321 tests).
        Autotest.Commands["ainew_wreck"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("ainew_wreck needs: i distance");
            var ship = At(parts[0]);
            var sim = ship.Sim!;
            using var stream = new MemoryStream();
            Tech.TechAutotest.PlayerShip().SaveDesign(stream);
            var wreck = Ship.Load(stream.ToArray(), "Test wreck", -3, spawnWithCrew: false, randSeed: 4242UL);
            wreck.WorldCenter = Autotest.ClearSpotNear(sim, ship.DetWorldCenter, ship.WorldBoundingRadius + float.Parse(parts[1], CultureInfo.InvariantCulture), wreck.WorldBoundingRadius);
            sim.Ships.Add(wreck);
            Log.Info($"AUTOTEST ainew_wreck id={wreck.UniqueID} junk={wreck.IsJunk} parts={wreck.Parts.Count}");
        };
        // Targets that the scavenge pick sees for the first aispawn ship (spec 1321).
        Autotest.Fields["ai_new_first_scav_targets"] = () => New().Count == 0 ? -1 : Scavenge.Scavenger.Targets(Autotest.RequireRun(), New()[0]).Count;
        // Units of steel in the storage of the first aispawn ship.
        Autotest.Fields["ai_new_first_steel"] = () => New().Select(s => (double)s.Resources.GetResourceTotal(new ID<ResourceRules>("steel"), includeStored: true, includeCarried: false)).DefaultIfEmpty(-1).First();
        // aigivelacking <from> <to>: puts into ship <from> twice what ship <to> (indexes of the last aispawn) lacks for its repairs.
        Autotest.Commands["aigivelacking"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("aigivelacking needs: from to");
            var (from, to) = (At(parts[0]), At(parts[1]));
            foreach (var (id, n) in Resupply.Lacking(to))
            {
                int added = from.Resources.AddResources(new ID<ResourceRules>(id), 2 * n);
                Log.Info($"AUTOTEST {from.Metadata.PlainShipName} gets {added} of {2 * n} {id}");
            }
        };
        // ainear <i> <j> <distance>: moves ship <i> of the aispawn list to this distance from the hull of ship <j>.
        Autotest.Commands["ainear"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new InvalidOperationException("ainear needs: i j distance");
            var (ship, to) = (At(parts[0]), At(parts[1]));
            float d = float.Parse(parts[2], CultureInfo.InvariantCulture);
            Autotest.Teleport(ship, Autotest.ClearSpotNear(ship.Sim!, to.DetWorldCenter, to.WorldBoundingRadius + ship.WorldBoundingRadius + d, ship.WorldBoundingRadius));
        };
        // ainew_camera <i> <size>: the camera stops following ships and shows ship <i> of the aispawn list (screenshots).
        Autotest.Commands["ainew_camera"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("ainew_camera needs: i size");
            var at = At(parts[0]).WorldCenter;
            Autotest.Commands["camera"](string.Create(CultureInfo.InvariantCulture, $"{at.X} {at.Y} {parts[1]}"));
        };
        // ainew_watch <i> <distance>: moves the player ship to this distance from ship <i> of the aispawn list, so the ship is in
        // sight for screenshots. Test only.
        Autotest.Commands["ainew_watch"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("ainew_watch needs: i distance");
            var ship = At(parts[0]);
            var player = Tech.TechAutotest.PlayerShip();
            float d = float.Parse(parts[1], CultureInfo.InvariantCulture);
            Autotest.Teleport(player, Autotest.ClearSpotNear(ship.Sim!, ship.DetWorldCenter, ship.WorldBoundingRadius + player.WorldBoundingRadius + d, player.WorldBoundingRadius));
        };
        // playerweapons on|off [ship name]: turns every weapon of the player ship (or of the human ship with that name) on or off with
        // its on/off part setting, like a click. For screenshots next to an enemy that must not be shot. Test only.
        Autotest.Commands["playerweapons"] = arg =>
        {
            var words = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) throw new InvalidOperationException("playerweapons needs: on|off [ship name]");
            var player = words.Length == 1 ? Tech.TechAutotest.PlayerShip()
                : Autotest.RequireRun().Game.Sim.Ships.HumanPlayerSignificantShips.Where(s => s.Metadata.PlainShipName == words[1]).MinBy(s => s.UniqueID)
                  ?? throw new InvalidOperationException($"playerweapons: no human ship named '{words[1]}'");
            var toggles = player.Weapons.Select(w => w.Part).OfType<Cosmoteer.Ships.Parts.Part>().Distinct()
                .SelectMany(part => part.Components.OfType<PartUIToggle>()).Where(t => t.Rules.ToggleID.ToString() == "on_off")
                .ToDictionary(t => t, _ => words[0] == "on" ? 1 : 0);
            if (toggles.Count == 0) throw new InvalidOperationException("the player ship has no weapon with an on/off setting");
            player.Sim!.RequireGame().NetManager.EnqueueInput(new MPSetUITogglesInput(toggles));
        };
        // aiplan_near <distance>: moves each hostile ship that falls back to a station or faction mate, or flees to a station, to this distance from its target's hull.
        Autotest.Commands["aiplan_near"] = arg =>
        {
            float d = float.Parse(arg, CultureInfo.InvariantCulture);
            foreach (var ship in Hostile())
            {
                var at = ship.AI?.GetStrategyModule<FallBackStrategy>()?.TargetCenter(ship) ?? ship.AI?.GetStrategyModule<Ai.FleeStrategy>()?.StationCenter(ship);
                if (at is not { } target) continue;
                // On the side of the target away from the nearest player ship: a fleeing ship flees again from an enemy within FleeDistance
                // before it docks (#336: placed between the station and the player, it never docked).
                var player = ship.Sim!.Ships.HumanPlayerSignificantShips.MinBy(h => h.DetWorldCenter.DistanceTo(target.Center));
                var away = player is null || player.DetWorldCenter.DistanceTo(target.Center) < 1f ? new Halfling.Geometry.Vector2(1f, 0f)
                    : Halfling.Geometry.Vector2.FromAngle(player.DetWorldCenter.DirectionTo(target.Center));
                Autotest.Teleport(ship, Autotest.ClearSpotNear(ship.Sim!, target.Center + away * (target.Radius + d), 0f, ship.WorldBoundingRadius));
                Log.Info($"AUTOTEST {ship.Metadata.PlainShipName} moved {d:0} from its repair target");
            }
        };
    }

    static int _markedJobs;

    static List<Ship> Hostile() => PersonalityAutotest.Hostile().OrderBy(s => s.UniqueID).ToList();

    /// <summary>The live ships of the last aispawn, in spawn order. Index i of the commands counts in this list.</summary>
    static List<Ship> New()
    {
        var sim = Autotest.RequireRun().Game.Sim;
        return PersonalityAutotest.LastSpawned.Select(id => sim.Ships[id]).OfType<Ship>().Where(s => !s.IsJunk).ToList();
    }

    static IEnumerable<FallBackStrategy> Modules() => Hostile().Select(s => s.AI?.GetStrategyModule<FallBackStrategy>()).OfType<FallBackStrategy>();

    static Ship At(string index)
    {
        var all = New();
        int i = int.Parse(index, CultureInfo.InvariantCulture);
        return i >= 0 && i < all.Count ? all[i] : throw new InvalidOperationException($"no ship {i} of the last aispawn; {all.Count} live");
    }

    static (List<Ship> Ships, string Value) Target(string arg)
    {
        var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new InvalidOperationException("needs: index|new value");
        return (parts[0] == "new" ? New() : [At(parts[0])], parts[1].Trim());
    }
}

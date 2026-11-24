using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Thieves;

/// <summary>
/// Entry point of the Thieves area (spec 1770 to 1799): pirate thieves that fight for the players' cargo, collect what spills and escape.
/// </summary>
public static class ThievesArea
{
    public static void Register()
    {
        Run.Hooks.SystemEntered += Thieves.OnSystemEntered;
        Run.Hooks.Tick += Thieves.OnTick;
        AddAutotest();
    }

    static Run R() => Autotest.RequireRun();
    static SimRoot Sim() => R().Game.Sim;
    static Ship Me() => SimQueries.FirstHumanShip(Sim()) ?? throw new InvalidOperationException("no player ship");
    static List<Ship> Live() => R().State.Thieves.Thieves.Keys.Select(id => Sim().Ships[id]).OfType<Ship>().Where(s => !s.IsJunk).OrderBy(s => s.UniqueID).ToList();
    static ThiefState? First() => R().State.Thieves.Thieves.OrderBy(kv => kv.Key).Select(kv => kv.Value).FirstOrDefault();
    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Value of the loose resources (not tech crates) within the radius of the first player ship, with or without the player-drop flag.</summary>
    static long LooseNearMe(bool fromPlayer, float radius)
    {
        var sim = Sim();
        var me = Me();
        long sum = 0;
        for (int i = 0; i < sim.Nuggets.Count; i++)
        {
            var n = sim.Nuggets[i];
            if (n.ResourceType == Tech.Vaults.TechId || ((n.Flags & NuggetFlags.BelongsToPlayer) != 0) != fromPlayer) continue;
            if (Autonomy.AutonomyWorld.EdgeDistance(me, n.DetWorldLocation) <= radius) sum += (long)n.Quantity * Autonomy.Funnel.UnitValue(sim, n.ResourceType);
        }
        return sum;
    }

    static int _escaped, _died;

    static void AddAutotest()
    {
        // th_designs: builds the design of every tier and logs its size, value and parts (design check).
        Autotest.Commands["th_designs"] = _ =>
        {
            for (int tier = 1; tier <= ThiefShip.TierCount; tier++)
            {
                var d = ThiefShip.Design(tier);
                Log.Info($"AUTOTEST thief design tier {tier}: target {ThiefShip.ValueShare * GameData.TierValueMaximum(tier):0} parts {d.Count} " +
                         $"rows {d.Min(p => p.Y)}..{d.Max(p => p.Y)} storage {d.Count(p => p.Part.Contains("storage"))} " +
                         $"weapons {d.Count(p => p.Part.Contains("laser") || p.Part.Contains("ion_beam"))} armor {d.Count(p => p.Part == "cosmoteer.armor")}");
            }
        };
        // th_layout <tier>: logs every part of the tier's design.
        Autotest.Commands["th_layout"] = arg => Log.Info($"AUTOTEST layout {arg}: " + string.Join(" ", ThiefShip.Design(int.Parse(arg)).Select(p => $"{p.Part.Replace("cosmoteer.", "")}@{p.X},{p.Y}r{p.Rot}")));
        // th_spawn <tier> [distance]: one thief group of one ship, with the Normal AI and a new pirate index, this far (default 800) from the first player ship.
        Autotest.Commands["th_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var at = Me().DetWorldCenter + new Vector2(p.Length > 1 ? F(p[1]) : 800f, 0f);
            int index = Diplomacy.Situation.Career(Sim()).AllocUniquePlayerIndex();
            var ship = ThiefShip.Spawn(Sim(), int.Parse(p[0]), index, new ID<ShipAIRules>("Normal"), Thieves.Parameters(at), at, at.DirectionTo(Me().DetWorldCenter));
            Thieves.Add(R(), ship, index, int.Parse(p[0]));
        };
        // th_camera <tier> <size>: the camera on the first live thief of the tier, this many world units across (screenshots).
        Autotest.Commands["th_camera"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var at = Live().First(s => R().State.Thieves.Thieves[s.UniqueID].Tier == int.Parse(p[0])).WorldCenter;
            Autotest.Commands["camera"](string.Create(CultureInfo.InvariantCulture, $"{at.X} {at.Y} {p[1]}"));
        };
        // th_lineup <tier> ...: one thief of each tier in a row 120 apart beside the first player ship, the AI off (screenshots).
        Autotest.Commands["th_lineup"] = arg =>
        {
            var me = Me();
            float x = 300f;
            foreach (var t in arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse))
            {
                int index = Diplomacy.Situation.Career(Sim()).AllocUniquePlayerIndex();
                var at = me.DetWorldCenter + new Vector2(x, 0f);
                var ship = ThiefShip.Spawn(Sim(), t, index, new ID<ShipAIRules>("Normal"), Thieves.Parameters(at), at, new Direction(-MathF.PI / 2f));
                ship.AI = null;
                Thieves.Add(R(), ship, index, t);
                x += 120f;
            }
        };
        // th_encounter: the thief-only encounter now (no roll, no pacer check). th_near <distance>: the thieves move this far from the first
        // player ship, and their patrol origin moves with them, as if they arrived there. The vanilla AI acquires no target while the ship
        // is more than TargetAcquireRangeFromOrigin (6,000) from its origin. The encounter places the origin from the lowest-ID player ship,
        // which can be the client's, so without the move a thief next to the host could stay out of reach of every target there.
        Autotest.Commands["th_encounter"] = _ => Thieves.Arrive(R(), Sim(), R().T.Thieves);
        Autotest.Commands["th_near"] = arg =>
        {
            float offset = F(arg);
            foreach (var s in Live())
            {
                Autotest.Teleport(s, Autotest.ClearSpotNear(Sim(), Me().DetWorldCenter, Me().WorldBoundingRadius + offset + s.WorldBoundingRadius, s.WorldBoundingRadius));
                var parameters = s.AI?.Parameters ?? throw new InvalidOperationException($"thief {s.Metadata.PlainShipName} has no AI parameters");
                parameters.PatrolOrigin = s.DetWorldCenter;
                offset += 150f;
            }
        };
        // th_cargo <me|thieves> <resource> <units>: puts the units into the storage bays of the first player ship, or of every thief.
        Autotest.Commands["th_cargo"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var type = new ID<ResourceRules>(p[1]);
            var ships = p[0] switch { "me" => [Me()], "thieves" => Live(), _ => throw new InvalidOperationException("th_cargo needs me or thieves") };
            foreach (var s in ships)
            {
                int added = s.Resources.AddResources(type, int.Parse(p[2]), excludeConstructionSinks: true);
                Log.Info($"AUTOTEST th_cargo: {added} {type} into {s.Metadata.PlainShipName}");
            }
        };
        // th_spill <resource> <units>: loose resources with the vanilla player-drop flag next to the first player ship, as a broken player part drops them.
        Autotest.Commands["th_spill"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var me = Me();
            var rect = new RotRect(me.DetWorldCenter + new Vector2(0f, me.WorldBoundingRadius + 60f), new Vector2(80f, 80f), default);
            Sim().Nuggets.DropNuggets(new ID<ResourceRules>(p[0]), int.Parse(p[1]), rect, default, randomVelocity: false, belongsToPlayer: true);
        };
        // th_kill: every part of the first live thief is destroyed (its storage drops).
        Autotest.Commands["th_kill"] = _ =>
        {
            var s = Live().FirstOrDefault() ?? throw new InvalidOperationException("no live thief");
            foreach (var part in s.Parts.ToList()) part.Health = 0;
            Autotest.Wait(1);
        };
        Autotest.Commands["th_set"] = arg => Autotest.SetField(R().T.Thieves, arg);
        // th_report: logs each thief's command, AI modules that are on, distance and relation to the first player ship.
        Autotest.Commands["th_report"] = _ =>
        {
            foreach (var s in Live())
                Log.Info($"AUTOTEST {s.Metadata.PlainShipName}: cmd {s.Commands.CurConfirmedCommand?.GetType().Name ?? "none"} enemy {s.IsEnemiesWith(Me())} " +
                         $"dist {Hunters.HunterOrders.EdgeDistance(s, Me()):0} ai {s.AI?.Rules.ID} params {s.AI?.Parameters is not null} modules {string.Join(",", s.AI?.StrategyModules.Select(m => m.GetType().Name) ?? [])} " +
                         $"crew {s.Crew.Assigned.Count} control {s.Commands.CanExecuteCommands} sight {s.Sensors.DetWorldSightCircle.Radius:0}");
            foreach (var s in Live().Where(x => !x.Commands.CanExecuteCommands))
                Log.Info($"AUTOTEST {s.Metadata.PlainShipName} crew: " + string.Join(" ", s.Crew.Assigned.Select(c => $"{c.ShipCrew?.CurrentCell.ToString() ?? "space"}:{c.Job?.GetType().Name ?? "idle"}")));
            foreach (var s in Live().Where(x => !x.Commands.CanExecuteCommands))
                foreach (var part in s.Parts.Where(p => p.Rules.TypeCategories.Contains(new("command"))))
                    Log.Info($"AUTOTEST {s.Metadata.PlainShipName} control room at {part.Location}: " + string.Join(" ", part.Components.OfType<Cosmoteer.Ships.Parts.OperationalPartComponent>()
                        .Where(o => !o.IsOperational).Select(o => o.Rules.ID.ToString())) + $" battery {string.Join(",", part.Components.OfType<Cosmoteer.Ships.Parts.Resources.BaseResourceStorage>().Select(b => b.Resources))}");
        };
        // Thieves that the state tracks: live or in stasis.
        // th_roll: the encounter roll is due now, as after a first arrival (spec 1781).
        Autotest.Commands["th_roll"] = _ => R().State.Thieves.EncounterDue = R().State.GameSeconds;
        // th_allied: 1 when every thief shares the player index of a live pirate ship that is not a thief (it joined that group).
        Autotest.Fields["th_allied"] = () =>
        {
            var thieves = Live();
            var others = Sim().Ships.Where(s => !s.IsJunk && !R().State.Thieves.Thieves.ContainsKey(s.UniqueID)).Select(s => s.Metadata.PlayerIndex).ToHashSet();
            return thieves.Count > 0 && thieves.All(t => others.Contains(t.Metadata.PlayerIndex)) ? 1 : 0;
        };
        Autotest.Fields["th_count"] = () => R().State.Thieves.Thieves.Count;
        Autotest.Fields["th_phase"] = () => First() is { } t ? (int)t.Phase : -1;
        Autotest.Fields["th_attacking"] = () => Live().Count(s => s.Commands.CurConfirmedCommand is AttackCommand { Target.IsHumanPlayerShip: true });
        // 1 when a gun of a fighting thief aims at a storage part of a player ship.
        Autotest.Fields["th_aim_storage"] = () => Live().Any(s => s.Weapons.Any(w => w.ExplicitTargets.CurConfirmedTarget?.Target is Cosmoteer.Ships.Parts.Part { Ship.IsHumanPlayerShip: true } part
            && part.Rules.TypeCategories.Contains(new("storage")))) ? 1 : 0;
        Autotest.Fields["th_cargo_value"] = () => Live().Sum(s => Thieves.CargoValue(Sim(), s));
        Autotest.Fields["th_player_loot"] = () => Live() is { Count: > 0 } l ? Thieves.PlayerLoot(Sim(), l, R().T.Thieves.LootRadius) : 0;
        Autotest.Fields["th_my_hull"] = () => SimQueries.FirstHumanShip(Sim()) is { } me ? Hunters.HunterOrders.HealthFraction(me) : 0;
        Autotest.Fields["th_loose_mine"] = () => LooseNearMe(true, 3000f);
        Autotest.Fields["th_loose_other"] = () => LooseNearMe(false, 3000f);
        // th_mark: notes the escape and death counts; th_escaped_since and th_died_since count from there.
        Autotest.Commands["th_mark"] = _ => (_escaped, _died) = (R().State.Thieves.Escaped, R().State.Thieves.Died);
        Autotest.Fields["th_escaped_since"] = () => R().State.Thieves.Escaped - _escaped;
        Autotest.Fields["th_died_since"] = () => R().State.Thieves.Died - _died;
        Autotest.Fields["th_pacer"] = () => Pursuit.Pacer.Count(R(), Pursuit.PacedKinds.Thieves);
    }
}

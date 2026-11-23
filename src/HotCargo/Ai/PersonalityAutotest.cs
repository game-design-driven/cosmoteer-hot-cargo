using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;
using HotCargo.Tactics;

namespace HotCargo.Ai;

/// <summary>Autotest commands and fields for personalities, cover and teams (spec 360-369). Test only, single player.</summary>
internal static class PersonalityAutotest
{
    static readonly ID<BuiltinShipTag> Combat = new("combat");

    /// <summary>The hostile ship that kiteplayer kites, and whether the kite runs now (counter kiting, spec 1907).</summary>
    static ObjectID _kitee;
    static bool _kiting, _kiteHooked;

    static Ship Kitee() => Sim().Ships[_kitee] ?? throw new InvalidOperationException("kiteplayer: the kited ship is gone");

    /// <summary>The human ship with the longest weapon range (ties: the lowest ID): the kiter of kiteplayer.</summary>
    static Ship? Kiter(SimRoot sim) => sim.Ships.HumanPlayerSignificantShips.OrderByDescending(s => HunterOrders.MaxWeaponRange(s, onlyActive: false))
        .ThenBy(s => s.UniqueID).FirstOrDefault();

    /// <summary>Each sim tick while the kite runs: the kiter keeps halfway between the two weapon ranges, edge to edge.</summary>
    static void KiteTick(Run run, SimRoot sim)
    {
        if (!_kiting || sim != run.Game.Sim || Kiter(sim) is not { } player || sim.Ships[_kitee] is not { IsJunk: false } enemy) return;
        float edge = (HunterOrders.MaxWeaponRange(enemy, onlyActive: true) + CounterKiteStrategy.Reach(player)) * 0.5f;
        float center = edge + enemy.WorldBoundingRadius + player.WorldBoundingRadius;
        Autotest.Teleport(player, enemy.DetWorldCenter + Vector2.FromAngle(enemy.DetWorldCenter.DirectionTo(player.DetWorldCenter)) * center);
    }

    public static void Register()
    {
        // kiteplayer <index> [away <distance>]|off: from now on the human ship with the longest range keeps halfway between the longest
        // weapon range of hostile ship <index> (Hostile order) and its own, edge to edge, each sim tick: a perfect kite. away: first moves
        // the hostile ship this far from the first human ship, out of reach of the start system's guards. off stops the kite, the ship
        // stays. Test only, single player.
        Autotest.Commands["kiteplayer"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("kiteplayer is single player only");
            if (!_kiteHooked) Run.Hooks.Tick += (run, sim, _) => KiteTick(run, sim);
            _kiteHooked = true;
            if (arg.Trim() == "off")
            {
                _kiting = false;
                return;
            }
            var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is not (1 or 3) || words.Length == 3 && words[1] != "away") throw new InvalidOperationException("kiteplayer needs: <index> [away <distance>] | off");
            var enemy = Hostile()[int.Parse(words[0], CultureInfo.InvariantCulture)];
            var player = Kiter(Sim()) ?? throw new InvalidOperationException("no human ship");
            if (words.Length == 3)
            {
                var first = SimQueries.FirstHumanShip(Sim()) ?? throw new InvalidOperationException("no human ship");
                float away = float.Parse(words[2], CultureInfo.InvariantCulture);
                Autotest.Teleport(enemy, Autotest.ClearSpotNear(Sim(), first.DetWorldCenter + new Vector2(away, 0f), enemy.WorldBoundingRadius, enemy.WorldBoundingRadius));
            }
            float own = HunterOrders.MaxWeaponRange(enemy, onlyActive: true), theirs = CounterKiteStrategy.Reach(player);
            Log.Info($"AUTOTEST kiteplayer: {player.Metadata.PlainShipName} (range {theirs:0}) kites {enemy.Metadata.PlainShipName} id={enemy.UniqueID} (range {own:0})");
            if (theirs <= own + 200f) throw new InvalidOperationException($"kiteplayer: the player range {theirs:0} must be 200 more than the enemy range {own:0}");
            (_kitee, _kiting) = (enemy.UniqueID, true);
        };
        // The answer of the kited ship (KiteAnswer: 0 none, 1 close in, 2 call, 3 team, 4 break off), and 1 when it is out of the
        // player ship's reach.
        Autotest.Fields["ai_kite_answer"] = () => (int)(Kitee().AI?.GetStrategyModule<CounterKiteStrategy>()?.Answer ?? KiteAnswer.None);
        Autotest.Fields["ai_kite_out_of_reach"] = () => Kiter(Sim()) is { } p
            && HunterOrders.EdgeDistance(Kitee(), p) > CounterKiteStrategy.Reach(p) ? 1 : 0;
        Autotest.Fields["ai_hostile"] = () => Hostile().Count;
        foreach (var trait in Enum.GetValues<Trait>())
            Autotest.Fields[$"ai_has_{trait.ToString().ToLowerInvariant()}"] = () => Hostile().Count(s => PersonalityStrategy.Traits(s, T()).Has(trait));
        Autotest.Fields["ai_personalities"] = () => Hostile().Select(s => PersonalityStrategy.Pick(s, T())?.Name).Where(n => n is not null).Distinct().Count();
        Autotest.Fields["ai_groups"] = () => Hostile().Select(PersonalityStrategy.Group).Distinct().Count();
        Autotest.Fields["ai_heavy"] = () => Hostile().Count(s => PersonalityStrategy.Scores(s, T()).Heavy);
        // Heavy ships that kite or bait. The contract (spec 363) says 0, in every check.
        Autotest.Fields["ai_heavy_kiters"] = () => Hostile().Count(s => PersonalityStrategy.Scores(s, T()).Heavy
            && (TacticGate.Active(Tactic.Kite, s) is not null || TacticGate.Active(Tactic.Bait, s) is not null));
        Autotest.Fields["ai_hiding"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<CoverStrategy>() is { Hiding: true });
        // Hiding ships whose line to their target passes through their rock now.
        Autotest.Fields["ai_covered"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<CoverStrategy>() is { Hiding: true, Rock: { } rock, Target: { } target }
            && CoverStrategy.Covered(s, target, rock));
        Autotest.Fields["ai_team_members"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<TeamStrategy>() is { Role: not TeamRole.None });
        // Distinct attack targets among attacking team members, and members that attack another ship than their leader (spec 364: 0).
        Autotest.Fields["ai_team_targets"] = () => TeamAttackers().Select(m => m.Target.UniqueID).Distinct().Count();
        Autotest.Fields["ai_team_off_target"] = () => TeamAttackers()
            .Count(m => m.Team.Leader is not { } leader || (leader.Commands.CurConfirmedCommand as AttackCommand)?.Target != m.Target);
        // aigroup <group>: the group that the ai_g_* fields count. Test only.
        Autotest.Commands["aigroup"] = arg => _group = arg;
        Autotest.Fields["ai_g_count"] = () => Hostile().Count(s => PersonalityStrategy.Group(s) == _group);
        foreach (var trait in Enum.GetValues<Trait>())
            Autotest.Fields[$"ai_g_{trait.ToString().ToLowerInvariant()}"] = () =>
                Hostile().Count(s => PersonalityStrategy.Group(s) == _group && PersonalityStrategy.Traits(s, T()).Has(trait));
        Autotest.Fields["ai_humanships"] = () => Sim().Ships.HumanPlayerSignificantShips.Count();

        // Weak sides (spec 1030-1033).
        Autotest.Fields["ai_weak_back"] = () => Hostile().Count(s => PersonalityStrategy.Scores(s, T()).WeakBack);
        // Ships with a weak back that kite. The contract (spec 1032) says 0.
        Autotest.Fields["ai_weakback_kiters"] = () => Hostile().Count(s => PersonalityStrategy.Scores(s, T()).WeakBack && TacticGate.Active(Tactic.Kite, s) is not null);
        // Fleeing ships with a weak side that strafe away (forced rotation, spec 1033).
        Autotest.Fields["ai_flee_guarded"] = () => FleeingWeak().Count(s => s.Commands.CurConfirmedCommand is MoveCommand { ForceDestinationRotation: true });
        // Fleeing ships with a weak side, near their threat, whose strong side points more than 60° away from it. The control check:
        // with weak sides switched off, the ships no longer keep the strong side to the threat.
        Autotest.Fields["ai_flee_off_strong"] = () => FleeingWeak().Count(s =>
        {
            var threat = AiGate.NearestEnemy(s);
            if (threat is null || HunterOrders.EdgeDistance(s, threat) >= T().FleeDistance) return false;
            float bearing = (float)s.DetWorldCenter.DirectionTo(threat.DetWorldCenter) - (float)s.DetRotation;
            float off = MathF.Abs(MathF.IEEERemainder(bearing - SideProfile.Angle(PersonalityStrategy.Profile(s, T()).Sides.Strong), MathF.Tau));
            return off > MathF.PI / 3f;
        });
        // Fleeing ships with a weak side, near their threat, that turn a weak side to it (spec 1033). A side that is not weak may face
        // the threat. The strong side gets 60° of slack, because a strafing ship turns while it flies.
        Autotest.Fields["ai_flee_weak_to_threat"] = () => FleeingWeak().Count(s =>
        {
            var threat = AiGate.NearestEnemy(s);
            if (threat is null || HunterOrders.EdgeDistance(s, threat) >= T().FleeDistance) return false;
            var sides = PersonalityStrategy.Profile(s, T()).Sides;
            float bearing = (float)s.DetWorldCenter.DirectionTo(threat.DetWorldCenter) - (float)s.DetRotation;
            float off = MathF.Abs(MathF.IEEERemainder(bearing - SideProfile.Angle(sides.Strong), MathF.Tau));
            return sides.IsWeak(SideProfile.Index(bearing)) && off > MathF.PI / 3f;
        });
        // Hostile ships whose mouseover names their personality. The contract (spec 368) says 0.
        Autotest.Fields["ai_mouseover_tells"] = () => Hostile().Count(s => PersonalityStrategy.Pick(s, T()) is { } p
            && s.IndicatorSource.GetMouseoverText().Contains(p.Name, StringComparison.Ordinal));
        // aiweakback: destroys every part on the line behind each control room of each hostile AI ship (opposite its flight
        // direction), so the control room shows at the back. Test only.
        Autotest.Commands["aiweakback"] = _ =>
        {
            var command = new ID<Cosmoteer.Ships.Parts.PartCategory>("command");
            foreach (var ship in Hostile())
            {
                var parts = ship.Parts.ToList();
                var back = Vector2.FromAngle((float)ship.FlightDirection + MathF.PI);
                var doomed = new HashSet<Cosmoteer.Ships.Parts.Part>();
                foreach (var room in parts.Where(p => p.Rules.TypeCategories.Contains(command)))
                    for (float step = 0.5f; step < 200f; step += 0.5f)
                    {
                        var at = room.LocalCenter + back * step;
                        int x = (int)MathF.Floor(at.X), y = (int)MathF.Floor(at.Y);
                        foreach (var p in parts)
                            if (p != room && !p.Rules.TypeCategories.Contains(command) && x >= p.Rect.X && x < p.Rect.X + p.Rect.Width
                                && y >= p.Rect.Y && y < p.Rect.Y + p.Rect.Height) doomed.Add(p);
                    }
                foreach (var p in doomed) p.Health = 0;
                Log.Info($"AUTOTEST weak back {ship.Metadata.PlainShipName} id={ship.UniqueID}: {doomed.Count} parts removed");
            }
        };

        // aispawn <group> <count> [tier] [tag]: hostile AI ships near the player. group: a faction ID (military ships with the Normal AI;
        // the players become wanted by it) or "pirates" (one unique hostile index for the run). tier: default the sector tier.
        // tag: a builtin ship tag, default "combat" ("trade": ships with cargo bays, on the faction's civilian player). Test only.
        Autotest.Commands["aispawn"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 2 or > 4) throw new InvalidOperationException("aispawn needs: group count [tier] [tag | hull=<ship name>]");
            var run = Autotest.RequireRun();
            int tier = parts.Length >= 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : run.CurrentTier;
            // hull=<ship name>: that built-in combat ship of the group, so a test does not depend on the random hull pick.
            string? hull = parts.Length == 4 && parts[3].StartsWith("hull=", StringComparison.Ordinal) ? parts[3]["hull=".Length..] : null;
            var tag = parts.Length == 4 && hull is null ? new ID<BuiltinShipTag>(parts[3]) : Combat;
            Spawn(run, parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), tier, tag, hull);
        };
        // aitrait <personality name> <trait> <weight>: sets one trait weight of a personality for this run. Test only.
        Autotest.Commands["aitrait"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) throw new InvalidOperationException("aitrait needs: name trait weight");
            string name = string.Join(' ', parts[..^2]);
            var personality = T().Personalities.FirstOrDefault(p => p.Name == name) ?? throw new InvalidOperationException($"no personality '{name}'");
            var trait = Enum.Parse<Trait>(parts[^2], ignoreCase: true);
            personality.Weights.Set(trait, float.Parse(parts[^1], CultureInfo.InvariantCulture));
            Log.Info($"AUTOTEST {name}.{trait} = {personality.Weights.Get(trait)}");
        };
        // aijitter <value>: sets the jitter of every personality for this run. 0 makes ships of one personality alike. Test only.
        Autotest.Commands["aijitter"] = arg =>
        {
            float jitter = float.Parse(arg, CultureInfo.InvariantCulture);
            foreach (var p in T().Personalities) p.Jitter = jitter;
        };
        // aiheavy: destroys every thruster part of each hostile AI ship, so each is heavy (agility 0). Test only.
        Autotest.Commands["aiheavy"] = _ =>
        {
            foreach (var ship in Hostile())
                foreach (var part in ship.Thrusters.Select(th => th.Part).OfType<Cosmoteer.Ships.Parts.Part>().Distinct().ToList()) part.Health = 0;
        };
        // airocks <count> <distance>: large asteroids on a ring around the player ship. Test only.
        Autotest.Commands["airocks"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("airocks needs: count distance");
            var sim = Sim();
            var center = (SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no human ship")).DetWorldCenter;
            int count = int.Parse(parts[0], CultureInfo.InvariantCulture);
            float distance = float.Parse(parts[1], CultureInfo.InvariantCulture);
            var rock = GameApp.Rules.GetDoodadRules(new ID<DoodadRules>("cosmoteer.asteroid_xl"));
            for (int i = 0; i < count; i++)
                rock.SpawnAt(sim, center + Vector2.FromAngle(i * MathF.Tau / count) * distance, Direction.Zero, sim.Rand, null, null, null, loadInstantly: true);
            Log.Info($"AUTOTEST {count} rocks at {distance:0} from the player");
        };
        // aiplayership [distance] [hull=<ship name>]: one more human ship next to the player ship, so a team has two targets to choose
        // from. With a distance: at least that far (center to center) from the first live ship of the last aispawn instead. With a hull:
        // that built-in combat ship of any tier, else a random tier 1-2 combat ship. Test only.
        Autotest.Commands["aiplayership"] = arg =>
        {
            var sim = Sim();
            var player = SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no human ship");
            var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? hull = words.FirstOrDefault(w => w.StartsWith("hull=", StringComparison.Ordinal))?["hull=".Length..];
            string? distance = words.FirstOrDefault(w => !w.StartsWith("hull=", StringComparison.Ordinal));
            var pick = hull is not null
                ? GameApp.Rules.BuiltinShips.FirstOrDefault(b => b.File.ShipName == hull && b.Tags.Contains(Combat)) ?? throw new InvalidOperationException($"no built-in combat ship '{hull}'")
                : GameApp.Rules.BuiltinShips.GetRandomMatchingShip(sim.Rand, null, new Range<int>(1, 2), null, [Combat]) ?? throw new InvalidOperationException("no tier 1-2 combat ship");
            var loc = distance is null ? Autotest.ClearSpotNear(sim, player.DetWorldCenter, 300f, 100f)
                : Autotest.ClearSpotNear(sim, (LastSpawned.Select(id => sim.Ships[id]).OfType<Ship>().FirstOrDefault(s => !s.IsJunk)
                    ?? throw new InvalidOperationException("no live ship of the last aispawn")).DetWorldCenter,
                    float.Parse(distance, CultureInfo.InvariantCulture), 100f);
            var spawner = new StasisShipSpawner(sim, pick.ID, loc, SpawnedShipFeatures.None, sim.Rand.UInt64())
                { ObjectAllegiance = player.Metadata.PlayerIndex };
            spawner.Spawn(sim);
            Log.Info($"AUTOTEST player ship {pick.File.ShipName} at {loc}");
        };
        // aiprofiles <label>: logs the group, personality, traits, build and role of every hostile AI ship. Test only.
        Autotest.Commands["aiprofiles"] = arg =>
        {
            foreach (var s in Hostile())
            {
                var b = PersonalityStrategy.Scores(s, T());
                Log.Info($"AUTOTEST AI {arg}: {s.Metadata.PlainShipName} id={s.UniqueID} group={PersonalityStrategy.Group(s)} " +
                         $"personality={PersonalityStrategy.Pick(s, T())?.Name} traits=[{PersonalityStrategy.Traits(s, T())}] " +
                         $"agility={b.Agility:0.00} turn={b.Turn:0.00} range={b.Range:0.00} protection={b.Protection:0.00} size={b.Size:0.00} " +
                         $"role={s.AI?.GetStrategyModule<TeamStrategy>()?.Role} cmd={s.Commands.CurConfirmedCommand?.GetType().Name} {PersonalityStrategy.Profile(s, T())}");
            }
        };
        // aicensus <ships per faction and tier band>: spawns built-in combat ships far away, logs their build profile, removes them.
        // For calibrating Tactics.Profile. Test only.
        Autotest.Commands["aicensus"] = arg => Census(int.Parse(arg, CultureInfo.InvariantCulture));
    }

    static string _group = "";

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    /// <summary>Team members (role set) with an attack command, and their target.</summary>
    static List<(TeamStrategy Team, Ship Target)> TeamAttackers() =>
        Hostile().Select(s => (Team: s.AI?.GetStrategyModule<TeamStrategy>(), Target: (s.Commands.CurConfirmedCommand as AttackCommand)?.Target))
            .Where(m => m.Team is { Role: not TeamRole.None } && m.Target is not null)
            .Select(m => (m.Team!, m.Target!)).ToList();
    static TacticsTunables T() => Autotest.RequireRun().T.Tactics;

    /// <summary>Fleeing hostile ships with at least one weak side, measured even with WeakSideEnabled off.</summary>
    static IEnumerable<Ship> FleeingWeak() => Hostile().Where(s => s.AI?.GetStrategyModule<FleeStrategy>() is { Fleeing: true }
        && PersonalityStrategy.Profile(s, T()).Sides.Weak != 0);

    /// <summary>Hostile AI ships with the personality module, in sim order.</summary>
    /// <summary>The ships of every aispawn of this test process, in spawn order.</summary>
    internal static readonly List<Cosmoteer.Game.ObjectID> LastSpawned = [];
    static (Run Run, int Index)? _pirates;

    internal static List<Ship> Hostile() =>
        Sim().Ships.Where(s => !s.IsJunk && s.AI?.GetStrategyModule<PersonalityStrategy>() is not null && AiGate.HostileToPlayers(s)).ToList();

    static void Spawn(Run run, string group, int count, int tier, ID<BuiltinShipTag> tag, string? hull = null)
    {
        var sim = run.Game.Sim;
        var career = (CareerSimModeManager)sim.Mode;
        var player = SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no human ship");
        FactionRules? faction = group == PersonalityStrategy.Pirates ? null
            : GameApp.Rules.Factions.FirstOrDefault(f => f.ID.ToString() == group) ?? throw new InvalidOperationException($"no faction '{group}'");
        // Pirates of one test share one index, so ships of two spawns are allies.
        if (faction is null && _pirates?.Run != run) _pirates = (run, career.AllocUniquePlayerIndex());
        // Faction ships that are not combat ships (trade ships) belong to the faction's civilian player, like the game's traders.
        int allegiance = faction is null ? _pirates!.Value.Index : tag == Combat ? faction.MilitaryPlayerIndex : faction.CivilianPlayerIndex;
        float direction = sim.Rand.Direction();
        for (int i = 0; i < count; i++)
        {
            BuiltinShipRules? pick = hull is null ? null
                : GameApp.Rules.BuiltinShips.FirstOrDefault(b => b.File.ShipName == hull && b.Tags.Contains(tag) && (faction is null || b.Faction == faction.ID))
                  ?? throw new InvalidOperationException($"no built-in {tag} ship '{hull}' for {group}");
            for (int wanted = Math.Max(1, tier); wanted >= 1 && pick is null; wanted--)
                pick = GameApp.Rules.BuiltinShips.GetRandomMatchingShip(sim.Rand, faction is null ? null : [faction.ID], new Range<int>(wanted, wanted), null, [tag]);
            if (pick is null) throw new InvalidOperationException($"no {tag} ship for {group} at tier {tier} or below");
            var near = player.DetWorldCenter + Vector2.FromAngle(direction + i * 0.3f) * 1500f;
            var loc = Autotest.ClearSpotNear(sim, near, 0f, 150f);
            var spawner = new StasisShipSpawner(sim, pick.ID, loc, SpawnedShipFeatures.None, sim.Rand.UInt64())
            {
                AI = new ID<ShipAIRules>("Normal"),
                Direction = loc.DirectionTo(player.DetWorldCenter),
                ObjectAllegiance = allegiance,
            };
            spawner.Spawn(sim);
            var ship = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{pick.ID} did not spawn");
            Log.Info($"AUTOTEST spawned {group} {ship.Metadata.PlainShipName} id={ship.UniqueID} tier {pick.Tier} player {allegiance}");
            LastSpawned.Add(ship.UniqueID);
            if (faction is not null) career.Wanted.WarTriggeredByPlayer(ship);
        }
        if (faction is null) return;
        // The war holds only while a ship of the faction has a human ship on its radar (WantedManager drops a faction that sees no
        // human ship at its next update). Ships that just spawned may not see the players yet, so the test waits until one does and
        // triggers the war again. Before, whether the war held depended on the random spawn direction.
        // It runs in the sim tick, so a co-op test ("mp aispawn") stays in step on every machine.
        var spawnedOne = sim.Ships.Where(s => s.Metadata.PlayerIndex == allegiance).OrderBy(s => s.UniqueID).First();
        var hub = run.Game.EventHub;
        double until = sim.LogicalTime.Seconds + 30;
        EventHandler<SimRoot>? retrigger = null;
        retrigger = (_, tickSim) =>
        {
            if (tickSim != sim) return;
            bool seen = sim.Ships.HumanPlayerShips.Any(h => h.IsWithinRadarOfTeam(allegiance));
            if (seen && spawnedOne.ExistsInSim(sim)) career.Wanted.WarTriggeredByPlayer(spawnedOne);
            if (seen || sim.LogicalTime.Seconds > until) hub.Tick -= retrigger;
        };
        hub.Tick += retrigger;
    }

    static void Census(int perBand)
    {
        var sim = Sim();
        var t = T();
        var far = (SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no human ship")).DetWorldCenter + new Vector2(60000f, 60000f);
        foreach (var faction in GameApp.Rules.Factions)
            for (int band = 1; band <= 12; band += 3)
                for (int i = 0; i < perBand; i++)
                {
                    var pick = GameApp.Rules.BuiltinShips.GetRandomMatchingShip(sim.Rand, [faction.ID], new Range<int>(band, band + 2), null, [Combat]);
                    if (pick is null) continue;
                    var spawner = new StasisShipSpawner(sim, pick.ID, far, SpawnedShipFeatures.None, sim.Rand.UInt64()) { ObjectAllegiance = -1 };
                    spawner.Spawn(sim);
                    var ship = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{pick.ID} did not spawn");
                    var p = PersonalityStrategy.Measure(ship, t);
                    var b = PersonalityModel.Scores(p, t.Scale);
                    Log.Info($"AUTOTEST CENSUS {faction.ID} tier {pick.Tier} {pick.File.ShipName}: accel={p.Acceleration:0.00} turn={p.Turn:0.000} " +
                             $"range={p.Range:0} armor={p.Armor:0.00} shields={p.Shields} radius={p.Radius:0} -> agility={b.Agility:0.00} " +
                             $"turn={b.Turn:0.00} range={b.Range:0.00} protection={b.Protection:0.00} size={b.Size:0.00}");
                    sim.Ships.Remove(ship, dispose: true, handleLostShip: false);
                }
    }
}

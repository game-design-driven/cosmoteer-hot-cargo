using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Missions;

/// <summary>
/// Entry point of the Missions area (spec section 11). Mission content is rules (mod/missions); the C# part is
/// the custom objective and reward types (EscortObjective, ShadyTrapObjective, ScaledRewardSpawner) and autotest commands.
/// </summary>
public static class MissionArea
{
    /// <summary>Hot Cargo mission metatypes, by the short name that autotest commands use.</summary>
    static readonly Dictionary<string, string> Metatypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["escort"] = "HotCargoEscort",
        ["assault"] = "HotCargoFleetAssault",
        ["shady"] = "HotCargoShadyBounty",
    };

    static List<Cosmoteer.Game.ObjectID> _markedMarkers = [];

    static Mission LatestOfType(string metatype)
    {
        var id = new ID<MissionMetatype>(metatype);
        return RequireRun().Mode.Missions.LastOrDefault(m => m.MetatypeID.Equals(id)) ?? throw new InvalidOperationException($"no {metatype} mission in the log");
    }

    public static void Register()
    {
        MissionMarkers.Register();
        Ui.CommsHail.Providers.Add(EscortHail.Instance);
        SharedChoice.SharedChoices.Register(EscortHail.Instance);
        foreach (var (name, metatype) in Metatypes)
        {
            var id = new ID<MissionMetatype>(metatype);
            Autotest.Fields[$"hc_offers_{name}"] = () => Offers(RequireSim()).Count(m => m.MetatypeID.Equals(id));
            Autotest.Fields[$"hc_active_{name}"] = () => RequireRun().Mode.Missions.Count(m => m.MetatypeID.Equals(id) && m.Completion == CompletionState.InProgress);
            Autotest.Fields[$"hc_completed_{name}"] = () => RequireRun().Mode.Missions.Count(m => m.MetatypeID.Equals(id) && m.Completion == CompletionState.Completed);
        }
        // Spec 1145. hc_markers_mark <metatype ID>: remembers the markers of the latest mission of that type in the log; the field
        // hc_markers_left counts how many of them are in the current system. hc_abandon <metatype ID>: abandons that mission (MP input).
        Autotest.Commands["hc_markers_mark"] = arg =>
        {
            _markedMarkers = [.. MissionMarkers.Of(LatestOfType(arg.Trim()))];
            if (_markedMarkers.Count == 0) throw new InvalidOperationException($"the latest {arg.Trim()} mission owns no marker");
        };
        Autotest.Fields["hc_markers_left"] = () => _markedMarkers.Count(id => RequireSim().Doodads[id] is not null);
        Autotest.Commands["hc_abandon"] = arg =>
        {
            RequireRun().Game.NetManager.EnqueueInput(new Cosmoteer.Modes.Career.Missions.MissionLogTab.MPAbandonMissionInput(LatestOfType(arg.Trim())));
            Autotest.Wait(2);
        };
        // Spec 1596: a vanilla Visit mission through the real inputs. hc_visit_accept: the galaxy offer for the neighbor that "jump" takes.
        // hc_visit_collect: the receive payment button of the nearest station. Field hc_visit: 0 none, 1 on the way, 2 arrived and the
        // reward waits at the stations of the system, 3 paid and out of the log, 4 other.
        Autotest.Commands["hc_visit_accept"] = _ => VisitAccept();
        Autotest.Commands["hc_visit_collect"] = _ => VisitCollect();
        Autotest.Fields["hc_visit"] = () => VisitState();
        Autotest.Commands["hc_generate"] = TestGenerate;
        Autotest.Commands["hc_accept"] = TestAccept;
        // hc_escort_goto [distance]: the players next to the escort, or this much farther away (spec 1851: it comes to them).
        Autotest.Commands["hc_escort_goto"] = arg => TeleportPlayers((RequireEscort().Escort.DetLocation() ?? throw new InvalidOperationException("escort is not in this system"))
            + new Vector2(arg.Trim().Length == 0 ? 0f : float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), 0f));
        Autotest.Commands["hc_escort_arrive"] = _ => TestEscortArrive();
        // Spec 1850 to 1853. hc_escort_ready: the "Ready" pick of the escort page, as the shared choice input of the acting player.
        // hc_escort_call: opens the real comms window from the acting player's ship to the escort (then comms_click).
        Autotest.Commands["hc_escort_ready"] = _ =>
        {
            RequireRun().Game.NetManager.EnqueueInput(EscortHail.Pick(RequireEscort().Escort.ShipID));
            Autotest.Wait(2);
        };
        Autotest.Commands["hc_escort_call"] = _ =>
        {
            var game = RequireRun().Game;
            var own = Autotest.ActingShip(game.Sim) ?? throw new InvalidOperationException("no own ship");
            var escort = RequireEscort().Escort.TargetShip ?? throw new InvalidOperationException("escort is not live");
            game.Gui.FloatingWindows.AddChild(new Cosmoteer.Modes.Career.Comms.CommWindow(game, own, escort));
            Autotest.Wait(1);
        };
        // 1 while the escort waits for the answer; 1 once it hailed; the hull gap from the escort to the nearest player ship.
        Autotest.Fields["hc_escort_waiting"] = () => RequireEscort().Waiting ? 1 : 0;
        Autotest.Fields["hc_escort_hailed"] = () => RequireEscort().Hailed ? 1 : 0;
        Autotest.Fields["hc_escort_gap"] = () =>
        {
            var escort = RequireEscort().Escort.TargetShip ?? throw new InvalidOperationException("escort is not live");
            var c = escort.DetWorldBoundingCircle;
            return RequireSim().Ships.HumanPlayerSignificantShips.Min(h => c.Center.DistanceTo(h.DetWorldCenter) - c.Radius - h.DetWorldBoundingCircle.Radius);
        };
        // 1 when the comms button of the local player flashes for a hail of the escort (vanilla AI hailers).
        Autotest.Fields["hc_escort_hailing"] = () =>
            RequireRun().Game.Sim.Mode is CareerSimModeManager m && RequireEscort().Escort.TargetShip is { } e && m.IsShipHailingLocalPlayer(e) ? 1 : 0;
        Autotest.Commands["hc_escort_diag"] = arg => LogEscort(arg);
        Autotest.Commands["hc_escort_advance"] = arg => TestEscortAdvance(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
        Autotest.Commands["hc_escort_ambush"] = _ =>
        {
            var o = RequireEscort();
            o.SpringAmbush(RequireSim(), o.Escort.DetLocation() ?? throw new InvalidOperationException("escort is not in this system"));
            Autotest.Wait(2);
        };
        // Test slots start with empty settings, so tutorial codex pages are unseen. The combat tutorial pauses the game
        // the first time an enemy is in sight without a station in sight, which freezes the escort. Tests turn that off.
        Autotest.Commands["hc_assault_goto"] = _ => TeleportPlayers(TaggedShips(RequireSim(), "hc_assault_ally").First().Location);
        Autotest.Commands["hc_shady_spring"] = _ => TestShadySpring();
        Autotest.Commands["hc_kill_ambush"] = _ => TestKillAmbush();
        Autotest.Commands["hc_shady_goto"] = _ => TeleportPlayers(RequireTrap().Site.DetLocation() ?? throw new InvalidOperationException("shady site is not in this system"));
        // Remembers money and the escort distance, for hc_money_gain and hc_escort_progress.
        Autotest.Commands["hc_mark"] = _ =>
        {
            _markedMoney = RequireRun().Game.LocalPlayerState.Money;
            _markedEscortDist = FindObjective<EscortObjective>() is null ? 0f : EscortDistance();
        };
        Autotest.Commands["hc_ambush_file"] = _ =>
        {
            foreach (var file in new[] { AmbushFile.Shady, AmbushFile.Convoy })
                Autotest.Check(file.Generator.Spawners.Length > 0, $"ambush file loads ({file.FilePath})");
        };

        // Convoy cover (spec 36): the heat change on the accepted escort, and a command that gives that reward now.
        Autotest.Fields["hc_escort_cover"] = () => EscortCover().Heat;
        Autotest.Commands["hc_cover_give"] = _ => EscortCover().GiveReward(RequireRun().Game);
        Autotest.Fields["hc_money_gain"] = () => RequireRun().Game.LocalPlayerState.Money - _markedMoney;
        Autotest.Fields["hc_escort_steering"] = () => RequireEscort() is var o && o.Escort.TargetShip?.AI?.Parameters?.PatrolOrigin is { } p && p == o.Destination ? 1 : 0;
        Autotest.Fields["hc_escort_dist"] = () => EscortDistance();
        Autotest.Fields["hc_escort_progress"] = () => _markedEscortDist - EscortDistance();
        // Spec 36: route length, and its share of the nearest gate's distance from the center.
        Autotest.Fields["hc_escort_route"] = () => RequireEscort() is var o ? o.Start.DistanceTo(o.Destination) : 0f;
        Autotest.Fields["hc_escort_route_share"] = () =>
        {
            var o = RequireEscort();
            var gates = ExistingSpawnTags(RequireSim()).GetValueOrDefault(new ID<SimObjectSpawner>("ftl_gate")) ?? throw new InvalidOperationException("no FTL gate in this system");
            return o.Start.DistanceTo(o.Destination) / gates.Min(g => g.Location.Center.Length);
        };
        Autotest.Fields["hc_escort_marker"] = () => RequireEscort() is var o && o.DestinationPoiID is { } id && RequireSim().Doodads[id] is not null ? 1 : 0;
        Autotest.Fields["hc_escort_raiders"] = () => TaggedShips(RequireSim(), "hc_escort_raider").Count;
        // Live raiders whose attack command targets the escort.
        Autotest.Fields["hc_raiders_on_escort"] = () =>
        {
            Ship? escort = RequireEscort().Escort.TargetShip;
            return escort is null ? 0 : TaggedShips(RequireSim(), "hc_escort_raider").Count(t => t.Live?.Commands.CurConfirmedCommand is Cosmoteer.Ships.Commands.AttackCommand a && a.Target == escort);
        };
        Autotest.Fields["hc_escort_health"] = () =>
            100f * Hunters.HunterOrders.HealthFraction(RequireEscort().Escort.TargetShip ?? throw new InvalidOperationException("escort is not live; keep a player near it"));
        Autotest.Fields["hc_escort_done"] = () => RequireEscort().Completion == CompletionState.Completed ? 1 : 0;
        Autotest.Fields["hc_trap_done"] = () => RequireTrap().Completion == CompletionState.Completed ? 1 : 0;
        Autotest.Fields["hc_ambushers"] = () => RequireTrap().Ambushers.Count(a => a.IsAlive);
        Autotest.Fields["hc_trap_state"] = () => (int)RequireTrap().State;
        Autotest.Fields["hc_trap_chance_now"] = () =>
        {
            var r = RequireRun();
            return MissionModel.TrapChance(r.T.Missions.ShadyTrapChanceBase, r.T.Missions.ShadyTrapChancePerHeat, r.State.Heat);
        };
        // Relations (spec 36, 37): counts of tagged ships that are enemies of a human player, or of the given target tag.
        Autotest.Fields["hc_allies"] = () => TaggedShips(RequireSim(), "hc_assault_ally").Count;
        Autotest.Fields["hc_allies_vs_player"] = () => EnemiesOfPlayer("hc_assault_ally");
        Autotest.Fields["hc_allies_vs_target"] = () => EnemiesOf("hc_assault_ally", "hc_assault_station");
        Autotest.Fields["hc_escort_vs_player"] = () => EnemiesOfPlayer("hc_escort_ship");
        Autotest.Fields["hc_raiders_vs_escort"] = () => EnemiesOf("hc_escort_raider", "hc_escort_ship");
        Autotest.Fields["hc_raiders_vs_player"] = () => EnemiesOfPlayer("hc_escort_raider");
        Autotest.Fields["hc_ambush_vs_player"] = () => EnemiesOfPlayer(ShadyTrapObjective.AmbushTag);
    }

    /// <summary>
    /// Mission tunables for code that runs in a game. The first system of a new career generates before the run starts,
    /// so it reads the tunables file like Run.Start does. Multiplayer uses the defaults there, like Run.Start.
    /// </summary>
    internal static MissionTunables TunablesFor(GameRoot game) =>
        Run.Current is { } run && run.Game == game ? run.T.Missions
        : (game.NetManager.IsMultiplayer ? new Tunables() : TunablesFile.Read()?.Values ?? new Tunables()).Missions;

    static int _markedMoney;
    static float _markedEscortDist;

    static HeatReward EscortCover()
    {
        var id = new ID<MissionMetatype>(Metatypes["escort"]);
        var mission = RequireRun().Mode.Missions.LastOrDefault(m => m.MetatypeID.Equals(id)) ?? throw new InvalidOperationException("no accepted escort");
        return mission.Rewards.OfType<HeatReward>().SingleOrDefault() ?? throw new InvalidOperationException("the escort has no convoy cover reward");
    }

    static float EscortDistance() => RequireEscort() is var o && o.Escort.DetLocation() is { } at ? at.DistanceTo(o.Destination) : -1f;

    static Run RequireRun() => Autotest.RequireRun();
    static SimRoot RequireSim() => RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");

    /// <summary>All offered missions in the current system: every station list, the system list and the galaxy list.</summary>
    internal static IEnumerable<Mission> Offers(SimRoot sim)
    {
        foreach (MetaSpaceStation station in SpaceStation.GetStations(sim, onlyOperating: false))
            foreach (Mission m in station.GetProvidedMissions() ?? new MissionList())
                yield return m;
        foreach (Mission m in ((CareerSimModeManager)sim.Mode).SimGlobalAvailableMissions) yield return m;
        foreach (Mission m in ((CareerGameModeManager)sim.RequireGame().Mode).GameGlobalAvailableMissions) yield return m;
    }

    /// <summary>Runs the Hot Cargo spawner groups now. Arg: escort, assault, shady or all.</summary>
    static void TestGenerate(string arg)
    {
        string[] names = arg is "" or "all" ? [.. Metatypes.Keys] : [arg];
        foreach (string name in names)
            if (!Metatypes.ContainsKey(name)) throw new InvalidOperationException($"unknown group '{name}'");
        GenerateGroups(RequireSim(), [.. names.Select(n => $"hc_group_{n}")]);
    }

    /// <summary>The spawner group with the tag in the sector generator. Throws when the mission rules did not load.</summary>
    internal static SimObjectSpawner FindGroup(SimRoot sim, string groupTag)
    {
        var tag = new ID<SimObjectSpawner>(groupTag);
        return AllSpawners(((CareerSimModeManager)sim.Mode).SectorType.SystemGenerator.Spawners)
            .OfType<SimObjectSpawner>().FirstOrDefault(s => s.Tags?.Contains(tag) == true)
            ?? throw new InvalidOperationException($"spawner group {tag} not found in the sector generator; are the mission rules loaded?");
    }

    /// <summary>
    /// Runs spawner groups now, with SpawnChance forced to 1. The groups' parent location is <paramref name="root"/>, by default the
    /// first player ship. Pass Vector2.Zero to place them like real system generation does (relative to the system center).
    /// Single player test helper; it uses sim.Rand from a GUI frame.
    /// </summary>
    internal static void GenerateGroups(SimRoot sim, IReadOnlyList<string> groupTags, Vector2? root = null)
    {
        var mode = (CareerSimModeManager)sim.Mode;
        var groups = groupTags.Select(t => FindGroup(sim, t)).ToList();
        var objects = ExistingSpawnTags(sim);
        var parentTree = new List<SimSpawner.SpawnedObject> { new(root ?? sim.Ships.HumanPlayerSignificantShips.First().DetWorldCenter) };
        uint batch = 0, fleet = 0;
        // Spec 1700: groups inside these that a test world turned off get their rules chance back for this run.
        var unblocked = AllSpawners(groups.SelectMany(g => g.SubSpawners ?? [])).OfType<SimObjectSpawner>()
            .Where(Dev.TestWorld.Blocked.ContainsKey).Distinct().ToList();
        var saved = groups.Concat(unblocked).Select(g => (Group: g, Chance: g.SpawnChance)).ToList();
        try
        {
            foreach (var g in unblocked) g.SpawnChance = Dev.TestWorld.Blocked[g];
            foreach (var g in groups)
            {
                g.SpawnChance = 1f;
                g.Spawn(sim, sim.Rand, parentTree, 0, ref batch, ref fleet, loadInstantly: true, [], objects, mode.GetSpawnFlags(), 0f);
            }
        }
        finally
        {
            foreach (var (g, chance) in saved) g.SpawnChance = chance;
        }
        Log.Info($"AUTOTEST generated groups: {string.Join(", ", groupTags)}");
        Autotest.Wait(2);
    }

    /// <summary>
    /// Spawn tags of the objects that already exist, with their IDs.
    /// SimulationGenerator's addExistingSpawnTags leaves out the IDs of objects in stasis, and missions that spawn at a station need them.
    /// </summary>
    internal static SpawnedObjectCollection ExistingSpawnTags(SimRoot sim)
    {
        var objects = new SpawnedObjectCollection();
        void Add(HashSet<ID<SimObjectSpawner>>? tags, SimSpawner.SpawnedObject so)
        {
            foreach (var tag in tags ?? [])
            {
                if (!objects.TryGetValue(tag, out var list)) objects.Add(tag, list = []);
                list.Add(so);
            }
        }
        for (int i = 0; i < sim.Ships.Count; i++) Add(sim.Ships[i].Tags, new(sim.Ships[i], null, null));
        foreach (StasisSpawner s in sim.Stasis.OrderBy(s => s.ReservedObjectID))
        {
            var tags = s is StasisDoodadSpawner d ? d.Tags : SimQueries.StasisTags(s);
            Add(tags, new(new Circle(s.Location, 0f), s.ReservedObjectID, s, playerIndex: s.ObjectAllegiance));
        }
        foreach (var d in sim.Doodads) Add(d.Tags, new(new Circle(d.Location, d.Rules.SpawnRadius), d.UniqueID));
        return objects;
    }

    internal static IEnumerable<SimSpawner> AllSpawners(IEnumerable<SimSpawner> roots)
    {
        foreach (SimSpawner s in roots)
        {
            yield return s;
            if (s is SimObjectSpawner { SubSpawners: { } subs })
                foreach (SimSpawner sub in AllSpawners(subs)) yield return sub;
        }
    }

    static Mission? _visit;

    static void VisitAccept()
    {
        var run = RequireRun();
        var next = run.Game.CurrentMapNode.Connections.FirstOrDefault(n => !run.State.VisitedNodes.Contains(n.UniqueID))
            ?? throw new InvalidOperationException("no unvisited neighbor system");
        var gotos = new List<Cosmoteer.Modes.Career.Missions.Objectives.GotoNodeObjective>();
        _visit = run.Mode.GameGlobalAvailableMissions.FirstOrDefault(m =>
        {
            gotos.Clear();
            foreach (var o in m.Objectives) o.GetObjectives(gotos);
            return m.MetatypeID.ToString() == "VisitSystem" && gotos.Any(g => g.MapNodeID == next.UniqueID);
        }) ?? throw new InvalidOperationException($"no Visit offer for {next.Name}");
        run.Game.NetManager.EnqueueInput(new Cosmoteer.Modes.Career.Comms.CommMissionsTab.MPAcceptMissionInput(
            Cosmoteer.Modes.Career.Comms.CommMissionsTab.MissionSourceType.GameGlobal, null, _visit));
        Log.Info($"AUTOTEST Visit {next.Name} offered, fame prerequisite {_visit.FamePrereq}");
    }

    /// <summary>Moves the players next to the nearest station that gives missions, then presses its receive payment button.</summary>
    static void VisitCollect()
    {
        var m = _visit ?? throw new InvalidOperationException("no Visit mission; run hc_visit_accept first");
        var sim = RequireSim();
        var me = sim.Ships.HumanPlayerSignificantShips.First();
        var station = SpaceStation.GetStations(sim, onlyOperating: true).Where(st => st.GetProvidedMissions() is not null)
            .OrderBy(st => st.DetWorldCenter.DistanceTo(me.DetWorldCenter)).ThenBy(st => st.StationID).Select(st => (MetaSpaceStation?)st).FirstOrDefault();
        if (station is not { } st0) throw new InvalidOperationException("no station that gives missions in this system");
        var id = st0.StationID;
        var at = Autotest.ClearSpotNear(sim, st0.DetWorldCenter, 900f, me.WorldBoundingRadius);
        foreach (Ship s in sim.Ships.HumanPlayerSignificantShips.ToList()) Autotest.Teleport(s, at);
        Autotest.WaitFor(() => sim.Ships[id] is not null, 20, "the station to leave stasis", then: () =>
        {
            RequireRun().Game.NetManager.EnqueueInput(new Cosmoteer.Modes.Career.Comms.CommMissionsTab.MPMissionPaymentInput(sim.Ships[id]!, m));
            Log.Info($"AUTOTEST Visit payment at {sim.Ships[id]!.Metadata.PlainShipName}");
        });
    }

    static int VisitState()
    {
        var run = RequireRun();
        if (_visit is not { } m) return 0;
        if (!run.Mode.Missions.ContainsMission(m)) return m.Completion == CompletionState.Completed ? 3 : 4;
        var gotos = new List<Cosmoteer.Modes.Career.Missions.Objectives.GotoNodeObjective>();
        foreach (var o in m.Objectives) o.GetObjectives(gotos);
        if (gotos.All(g => g.Completion == CompletionState.InProgress)) return 1;
        return ((CareerSimModeManager)RequireSim().Mode).SimGlobalPendingRewards.ContainsMission(m) ? 2 : 4;
    }

    static void TestAccept(string arg) =>
        AcceptOffer(Metatypes.GetValueOrDefault(arg) ?? throw new InvalidOperationException($"unknown mission kind '{arg}'"));

    /// <summary>Accepts the first offered mission of the metatype, like CommMissionsTab.MPAcceptMissionInput does.</summary>
    internal static Mission AcceptOffer(string metatype)
    {
        var sim = RequireSim();
        var id = new ID<MissionMetatype>(metatype);
        foreach (MetaSpaceStation station in SpaceStation.GetStations(sim, onlyOperating: false))
        {
            if (station.GetProvidedMissions() is not { } list) continue;
            if (list.FirstOrDefault(m => m.MetatypeID.Equals(id)) is not { } mission) continue;
            list.RemoveMission(mission);
            RequireRun().Mode.Missions.AddMission(mission, track: true);
            Log.Info($"AUTOTEST accepted {metatype}");
            return mission;
        }
        throw new InvalidOperationException($"no {metatype} offer in this system");
    }

    /// <summary>The objective of the given type in the latest accepted mission that has one.</summary>
    internal static T? FindObjective<T>() where T : Cosmoteer.Modes.Career.Missions.Objectives.Objective
    {
        var found = new List<T>();
        foreach (Mission m in RequireRun().Mode.Missions)
            foreach (var o in m.Objectives) o.GetObjectives(found);
        return found.LastOrDefault();
    }

    static T RequireObjective<T>() where T : Cosmoteer.Modes.Career.Missions.Objectives.Objective =>
        FindObjective<T>() ?? throw new InvalidOperationException($"no accepted mission has a {typeof(T).Name}");

    static EscortObjective RequireEscort() => RequireObjective<EscortObjective>();
    static ShadyTrapObjective RequireTrap() => RequireObjective<ShadyTrapObjective>();

    /// <summary>Logs the escort state: stasis, position, speed, command, AI origin and target, distance to the destination.</summary>
    static void LogEscort(string label)
    {
        var o = RequireEscort();
        var sim = RequireSim();
        Ship? ship = o.Escort.TargetShip;
        string player = string.Join(" ", sim.Ships.HumanPlayerSignificantShips.Select(s => $"{s.DetWorldCenter}"));
        if (ship is null)
        {
            Log.Info($"AUTOTEST ESCORT {label}: not live; location={o.Escort.DetLocation()} spawner={o.Escort.TargetSpawner?.GetType().Name} dest={o.Destination} players={player} paused={sim.IsPaused.Confirmed} clockPaused={sim.Clock.Paused} scale={sim.Clock.ScaleFactor} t={sim.LogicalTime} runSeconds={RequireRun().State.GameSeconds:0.0}");
            return;
        }
        var ai = ship.AI;
        Log.Info($"AUTOTEST ESCORT {label}: live name={ship.Metadata.PlainShipName} pos={ship.DetWorldCenter} vel={ship.Physics.Body?.LinearVelocity} " +
                 $"cmd={ship.Commands.CurConfirmedCommand?.GetType().Name} canExec={ship.Commands.CanExecuteCommands} " +
                 $"ai={ai?.Rules.ID} origin={ai?.PreferredOrigin} unacq={ai?.Parameters?.TargetUnacquireRangeFromOrigin} " +
                 $"modules={string.Join(",", ai?.StrategyModules.Select(m => m.GetType().Name) ?? [])} crew={ship.Crew.TotalMoneyValue} " +
                 $"dest={o.Destination} dist={ship.DetWorldCenter.DistanceTo(o.Destination):0} player={ship.Metadata.PlayerIndex} players={player} paused={sim.IsPaused.Confirmed} clockPaused={sim.Clock.Paused} scale={sim.Clock.ScaleFactor} t={sim.LogicalTime} runSeconds={RequireRun().State.GameSeconds:0.0}");
    }

    internal static void TeleportPlayers(Vector2 near)
    {
        int i = 0;
        foreach (Ship s in RequireSim().Ships.HumanPlayerSignificantShips.ToList())
            Autotest.Teleport(s, near + new Vector2(400 + 250 * i++, 0));
        Autotest.Wait(3);
    }

    /// <summary>Moves the live escort and the players to the destination. Run hc_escort_goto first, so the escort is out of stasis.</summary>
    static void TestEscortArrive()
    {
        var o = RequireEscort();
        Ship escort = o.Escort.TargetShip ?? throw new InvalidOperationException("escort is not live; run hc_escort_goto and wait first");
        Autotest.Teleport(escort, o.Destination);
        TeleportPlayers(o.Destination);
    }

    /// <summary>Moves the live escort to the given share of its route, and the players next to it. Run hc_escort_goto first.</summary>
    static void TestEscortAdvance(float share)
    {
        var o = RequireEscort();
        Ship escort = o.Escort.TargetShip ?? throw new InvalidOperationException("escort is not live; run hc_escort_goto and wait first");
        Vector2 at = o.Start + (o.Destination - o.Start) * share;
        Autotest.Teleport(escort, at);
        TeleportPlayers(at);
    }

    static void TestShadySpring()
    {
        var o = RequireTrap();
        if (o.State != ShadyTrapState.Waiting) throw new InvalidOperationException($"trap is {o.State}, not Waiting");
        var sim = RequireSim();
        o.Spring(sim, sim.Ships.HumanPlayerSignificantShips.First().DetWorldCenter);
        Autotest.Wait(2);
    }

    static void TestKillAmbush()
    {
        var sim = RequireSim();
        foreach (var a in RequireTrap().Ambushers)
            if (a.TargetShip is { } ship) sim.Ships.Remove(ship, dispose: true, handleLostShip: false);
        Autotest.Wait(2);
    }

    static List<SimQueries.TaggedShip> TaggedShips(SimRoot sim, string tagName) => SimQueries.TaggedShips(sim, new ID<SimObjectSpawner>(tagName));

    static int EnemiesOfPlayer(string tag)
    {
        var sim = RequireSim();
        var humans = sim.RequireGame().HumanPlayersIndexes;
        return TaggedShips(sim, tag).Count(t => humans.Any(h => sim.Mode.AreEnemies(t.PlayerIndex, h)));
    }

    static int EnemiesOf(string tag, string targetTag)
    {
        var sim = RequireSim();
        var targets = TaggedShips(sim, targetTag);
        if (targets.Count == 0) throw new InvalidOperationException($"no ships tagged {targetTag}");
        return TaggedShips(sim, tag).Count(t => targets.All(x => sim.Mode.AreEnemies(t.PlayerIndex, x.PlayerIndex)));
    }
}

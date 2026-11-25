using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
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
        ["lead"] = "HotCargoLead",
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
        Mayday.Register();
        MissionPayCheck.Register();
        Ui.CommsHail.Providers.Add(EscortHail.Instance);
        SharedChoice.SharedChoices.Register(EscortHail.Instance);
        foreach (var (name, metatype) in Metatypes)
        {
            var id = new ID<MissionMetatype>(metatype);
            Autotest.Fields[$"hc_offers_{name}"] = () => Offers(RequireSim()).Count(m => m.MetatypeID.Equals(id));
            Autotest.Fields[$"hc_active_{name}"] = () => RequireRun().Mode.Missions.Count(m => m.MetatypeID.Equals(id) && m.Completion == CompletionState.InProgress);
            Autotest.Fields[$"hc_completed_{name}"] = () => RequireRun().Mode.Missions.Count(m => m.MetatypeID.Equals(id) && m.Completion == CompletionState.Completed);
        }
        // Spec 2250. hc_markers_mark <metatype ID>: remembers the markers of the latest mission of that type in the log; the field
        // hc_markers_left counts how many of them are in the current system. hc_abandon <metatype ID>: abandons that mission (MP input).
        Autotest.Commands["hc_markers_mark"] = arg =>
        {
            _markedMarkers = [.. MissionMarkers.Of(LatestOfType(arg.Trim()))];
            if (_markedMarkers.Count == 0) throw new InvalidOperationException($"the latest {arg.Trim()} mission owns no marker");
        };
        Autotest.Fields["hc_markers_left"] = () => _markedMarkers.Count(id => RequireSim().Doodads[id] is not null);
        // Spec 996: hc_markers_sweep <complete|fail>: every offer in this system that owns a live target area marker (vanilla, Hot Cargo and
        // chain missions: the offers decide, not a list) goes to the log, and each of its objectives that owns a marker completes or fails.
        // The fields then count the markers and those still in the sim. Test only.
        Autotest.Commands["hc_markers_sweep"] = arg => TestMarkerSweep(arg.Trim());
        Autotest.Fields["hc_markers_swept"] = () => _swept.Count;
        Autotest.Fields["hc_markers_swept_left"] = () => _swept.Count(id => RequireSim().Doodads[id] is not null);
        Autotest.Fields["hc_markers_sweep_errors"] = () => _sweepErrors;
        Autotest.Commands["hc_abandon"] = arg =>
        {
            RequireRun().Game.NetManager.EnqueueInput(new Cosmoteer.Modes.Career.Missions.MissionLogTab.MPAbandonMissionInput(LatestOfType(arg.Trim())));
            Autotest.Wait(2);
        };
        // Spec 2251: a vanilla Visit mission through the real inputs. hc_visit_accept: the galaxy offer for the neighbor that "jump" takes.
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
        // hc_escort_call: opens the real comms window from the acting player's ship nearest the escort (then comms_click).
        Autotest.Commands["hc_escort_ready"] = _ =>
        {
            RequireRun().Game.NetManager.EnqueueInput(EscortHail.Pick(RequireEscort().Escort.ShipID));
            Autotest.Wait(2);
        };
        Autotest.Commands["hc_escort_call"] = _ =>
        {
            var game = RequireRun().Game;
            var escort = RequireEscort().Escort.TargetShip ?? throw new InvalidOperationException("escort is not live");
            // The acting player's ship nearest the escort: its comms must reach the escort, or the window closes at once.
            int player = Autotest.ActingPlayerIndex;
            var own = game.Sim.Ships.HumanPlayerSignificantShips.Where(s => s.Commands.CanExecuteCrewCommandsFrom(player))
                .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(escort.DetWorldCenter)).ThenBy(s => s.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no own ship");
            game.Gui.FloatingWindows.AddChild(new Cosmoteer.Modes.Career.Comms.CommWindow(game, own, escort));
            Autotest.Wait(1);
        };
        // hc_escort_bring: moves the acting player's ship nearest the escort next to it (sim; "as 1" in co-op), so that player can call it.
        Autotest.Commands["hc_escort_bring"] = _ =>
        {
            var sim = RequireSim();
            var escort = RequireEscort().Escort.TargetShip ?? throw new InvalidOperationException("escort is not live");
            int player = Autotest.ActingPlayerIndex;
            var own = sim.Ships.HumanPlayerSignificantShips.Where(s => s.Commands.CanExecuteCrewCommandsFrom(player))
                .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(escort.DetWorldCenter)).ThenBy(s => s.UniqueID).FirstOrDefault()
                // A ship takes crew orders only while it has crew (vanilla CanExecuteCrewCommandsFrom), so the message names the crew.
                ?? throw new InvalidOperationException("no own ship that takes crew orders; own ships: " + string.Join(", ",
                    sim.Ships.HumanPlayerSignificantShips.Where(s => s.Commands.IsCommandableBy(player))
                        .Select(s => $"{s.Metadata.PlainShipName} crew {s.Crew.Assigned.Count}")) + ".");
            Autotest.Teleport(own, Autotest.ClearSpotNear(sim, escort.DetWorldCenter, escort.DetWorldBoundingCircle.Radius + own.DetWorldBoundingCircle.Radius + 150f, own.DetWorldBoundingCircle.Radius));
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
        // hc_escort_wreck: a junk copy of the escort freighter ("Test wreck", like auto_wreck) at a clear spot next to it, for the scavenge
        // check (spec 1337). Sim change: use "mp" in co-op.
        Autotest.Commands["hc_escort_wreck"] = _ =>
        {
            var sim = RequireSim();
            var escort = RequireEscort().Escort.TargetShip ?? throw new InvalidOperationException("escort is not live");
            using var stream = new MemoryStream();
            escort.SaveDesign(stream);
            var wreck = Ship.Load(stream.ToArray(), "Test wreck", -3, spawnWithCrew: false, randSeed: 4242UL);
            wreck.WorldCenter = Autotest.ClearSpotNear(sim, escort.DetWorldCenter, escort.DetWorldBoundingCircle.Radius + wreck.WorldBoundingRadius + 200f, wreck.WorldBoundingRadius);
            sim.Ships.Add(wreck);
            Log.Info($"AUTOTEST hc_escort_wreck id={wreck.UniqueID} junk={wreck.IsJunk} at {wreck.WorldCenter}");
        };
        // The escort freighter's scavenge step (Scavenge.ScavengeStep; 0 idle), -1 without the module.
        Autotest.Fields["hc_escort_scav_step"] = () =>
            (int?)(RequireEscort().Escort.TargetShip ?? throw new InvalidOperationException("escort is not live")).AI?.GetStrategyModule<Scavenge.ScavengeStrategy>()?.Step ?? -1;
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
        // Spec 1881 to 1884, for every escort that meets the players (Convoy Duty, the chain escorts, the Mining Escort): the newest one.
        Autotest.Fields["hc_guard_alone"] = () => RequireGuarded().Guard.Guarded ? 0 : 1;
        Autotest.Fields["hc_guard_exposed"] = () => (float)RequireGuarded().Guard.ExposedSeconds;
        Autotest.Fields["hc_guard_live"] = () => RequireGuarded().Escort.TargetShip is null ? 0 : 1;
        Autotest.Fields["hc_guard_failed"] = () => ((Cosmoteer.Modes.Career.Missions.Objectives.Objective)RequireGuarded()).Completion == CompletionState.Failed ? 1 : 0;
        // Live raid ships of the newest escort, and how many of them attack it.
        Autotest.Fields["hc_guard_raiders"] = () => GuardRaiders().Count;
        Autotest.Fields["hc_guard_raiders_on_escort"] = () =>
            RequireGuarded().Escort.TargetShip is { } escort ? GuardRaiders().Count(r => r.Commands.CurConfirmedCommand is Cosmoteer.Ships.Commands.AttackCommand a && a.Target == escort) : 0;
        // Spec 1886: the warning ring, the seconds that the alone line shows, and the escort's time alone before raiders take it.
        Autotest.Fields["hc_guard_ring"] = () => RequireGuarded().Guard.MarkerId is { } id && RequireSim().Doodads[id] is not null ? 1 : 0;
        Autotest.Fields["hc_guard_left"] = () => EscortGuard.ShownSeconds(RequireGuarded().Guard, RequireRun().Game);
        Autotest.Fields["hc_guard_lost_after"] = () => RequireGuarded().Guard.LostAfter(MissionArea.TunablesFor(RequireRun().Game));
        // hc_guard_mark: remembers the newest escort's ship. Field hc_guard_marked_taken: 1 when that ship (live or in stasis) is hostile to the players.
        Autotest.Commands["hc_guard_mark"] = _ => _markedGuardShip = RequireGuarded().Escort.ShipID;
        Autotest.Fields["hc_guard_marked_taken"] = () =>
        {
            var sim = RequireSim();
            int owner = sim.Ships[_markedGuardShip]?.Metadata.PlayerIndex
                ?? (sim.Stasis.FirstOrDefault(s => s.ReservedObjectID == _markedGuardShip) as Cosmoteer.Simulation.Stasis.StasisShipSpawner)?.ObjectAllegiance
                ?? throw new InvalidOperationException("the marked escort is gone");
            return sim.RequireGame().HumanPlayersIndexes.Any(h => sim.Mode.AreEnemies(owner, h)) ? 1 : 0;
        };
        // hc_guard_expose <seconds>: sets the time alone, so a test does not wait for the raid or the loss.
        Autotest.Commands["hc_guard_expose"] = arg => RequireGuarded().Guard.ExposedSeconds = double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
        // hc_guard_goto [distance]: the players next to the newest escort, or this much farther away (any escort, the miner too).
        Autotest.Commands["hc_guard_goto"] = arg => TeleportPlayers((RequireGuarded().Escort.DetLocation() ?? throw new InvalidOperationException("escort is not in this system"))
            + new Vector2(arg.Trim().Length == 0 ? 0f : float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), 0f));
        // hc_guard_diag: one log line for each raid ship of the newest escort (place, order, whether it is an enemy of the escort).
        Autotest.Commands["hc_guard_diag"] = _ =>
        {
            var sim = RequireSim();
            Ship? escort = RequireGuarded().Escort.TargetShip;
            foreach (Ship r in GuardRaiders())
                Log.Info($"AUTOTEST GUARD raider {r.Metadata.PlainShipName} p{r.Metadata.PlayerIndex} at {r.DetWorldCenter} dist={(escort is null ? -1 : r.DetWorldCenter.DistanceTo(escort.DetWorldCenter)):0} " +
                         $"cmd={r.Commands.CurConfirmedCommand?.GetType().Name} target={(r.Commands.CurConfirmedCommand as Cosmoteer.Ships.Commands.AttackCommand)?.Target?.Metadata.PlainShipName} " +
                         $"enemy={(escort is null ? "?" : sim.Mode.AreEnemies(r.Metadata.PlayerIndex, escort.Metadata.PlayerIndex).ToString())} origin={r.AI?.PreferredOrigin} modules={string.Join(",", r.AI?.StrategyModules.Select(m => m.GetType().Name) ?? [])}");
        };
        // hc_guard_camera <size>: centers the camera on the newest escort, this many units wide (screenshots).
        Autotest.Commands["hc_guard_camera"] = arg =>
        {
            Vector2 at = RequireGuarded().Escort.DetLocation() ?? throw new InvalidOperationException("escort is not in this system");
            Autotest.Commands["camera"](string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{at.X} {at.Y} {arg.Trim()}"));
        };
        // Spec 1890 to 1897, the Blind Courier. hc_lead_ready: the "Ready" pick (shared choice input of the acting player).
        Autotest.Commands["hc_lead_ready"] = _ =>
        {
            RequireRun().Game.NetManager.EnqueueInput(EscortHail.Pick(RequireLead().Escort.ShipID));
            Autotest.Wait(2);
        };
        Autotest.Fields["hc_lead_hailed"] = () => RequireLead().Hailed ? 1 : 0;
        Autotest.Fields["hc_lead_waiting"] = () => RequireLead().Waiting ? 1 : 0;
        Autotest.Fields["hc_lead_ambushes"] = () => RequireLead().Ambushed ? 1 : 0;
        // Spec 1890: the main danger of the direct route (0 quiet, 1 raiders, 2 a base, 3 rough space); logs the mood line.
        Autotest.Fields["hc_lead_danger"] = () =>
        {
            var o = RequireLead();
            Log.Info($"AUTOTEST lead route hint: {LeadRoute.Hint(o.RouteDanger)}");
            return (int)o.RouteDanger;
        };
        Autotest.Fields["hc_lead_done"] = () => RequireLead().Completion == CompletionState.Completed ? 1 : 0;
        Autotest.Fields["hc_lead_marker"] = () => RequireLead().DestinationPoiID is { } id && RequireSim().Doodads[id] is not null ? 1 : 0;
        Autotest.Fields["hc_lead_area_on_dest"] = () => RequireLead() is var o && o.GetAreaLocation(RequireRun().Game) is { } c && c.Center == o.Destination ? 1 : 0;
        Autotest.Fields["hc_lead_route"] = () => RequireLead() is var o ? o.Start.DistanceTo(o.Destination) : 0f;
        Autotest.Fields["hc_lead_parts"] = () => RequireLead().Escort.TargetShip?.Parts.Count ?? -1;
        Autotest.Fields["hc_lead_weapons"] = () => RequireLead().Escort.TargetShip?.Weapons.GetTotalFirePower() ?? -1f;
        // Hull gap from the courier to the nearest player ship, and the courier's speed.
        Autotest.Fields["hc_lead_gap"] = () =>
        {
            Ship c = RequireLead().Escort.TargetShip ?? throw new InvalidOperationException("courier is not live");
            return RequireSim().Ships.HumanPlayerSignificantShips.Min(h => h.DetWorldCenter.DistanceTo(c.DetWorldCenter) - h.DetWorldBoundingCircle.Radius - c.DetWorldBoundingCircle.Radius);
        };
        Autotest.Fields["hc_lead_speed"] = () => (RequireLead().Escort.TargetShip ?? throw new InvalidOperationException("courier is not live")).Physics.Body?.LinearVelocity.Length() ?? 0f;
        // hc_lead_lead <distance>: the players this much farther along the route than the courier (toward the jump point), so it follows them.
        Autotest.Commands["hc_lead_lead"] = arg =>
        {
            var o = RequireLead();
            Vector2 at = o.Escort.DetLocation() ?? throw new InvalidOperationException("courier is not in this system");
            TeleportPlayers(at + (o.Destination - at).Normalize() * float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
        };
        // hc_lead_advance <share>: the live courier at this share of its route, the players next to it on the jump point side.
        Autotest.Commands["hc_lead_advance"] = arg =>
        {
            var o = RequireLead();
            Ship c = o.Escort.TargetShip ?? throw new InvalidOperationException("courier is not live; run hc_guard_goto and wait first");
            Vector2 at = o.Start + (o.Destination - o.Start) * float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            Autotest.Teleport(c, at);
            TeleportPlayers(at + (o.Destination - at).Normalize() * 300f);
        };
        // hc_lead_arrive: the live courier and the players at the jump point.
        Autotest.Commands["hc_lead_arrive"] = _ =>
        {
            var o = RequireLead();
            Ship c = o.Escort.TargetShip ?? throw new InvalidOperationException("courier is not live; run hc_guard_goto and wait first");
            Autotest.Teleport(c, o.Destination);
            TeleportPlayers(o.Destination);
        };
        // hc_lead_diag: one log line with the courier's place, order, speed and the players' places.
        Autotest.Commands["hc_lead_diag"] = _ =>
        {
            var o = RequireLead();
            Ship? c = o.Escort.TargetShip;
            string players = string.Join(" ", RequireSim().Ships.HumanPlayerSignificantShips.Select(h => $"{h.DetWorldCenter}"));
            Log.Info($"AUTOTEST LEAD live={c is not null} at={o.Escort.DetLocation()} cmd={c?.Commands.CurConfirmedCommand?.GetType().Name} to={(c?.Commands.CurConfirmedCommand as MoveCommand)?.Location} " +
                     $"speed={c?.Physics.Body?.LinearVelocity.Length():0} canExec={c?.Commands.CanExecuteCommands} cp={c?.Commands.CommandPointsProvided}/{c?.Commands.CommandPointsUsed} crew={c?.Crew.Assigned.Count} doors={c?.Doors.Count} ready={o.Ready} hailed={o.Hailed} start={o.Start} dest={o.Destination} players={players}");
        };
        // Screenshots only. hc_lead_route_dots: small vanilla target area rings every 400 along the straight route from the courier's start to
        // its jump point, so a picture shows the direct route. hc_lead_route_camera: the camera on the whole route.
        Autotest.Commands["hc_lead_route_dots"] = _ =>
        {
            var o = RequireLead();
            var sim = RequireSim();
            var rules = (Cosmoteer.Simulation.Doodads.POIDoodadRules)Cosmoteer.GameApp.Rules.GetDoodadRules(MissionMarkers.TargetArea);
            float length = o.Start.DistanceTo(o.Destination);
            for (float d = 400f; d < length; d += 400f)
                rules.SpawnAt(sim, o.Start + (o.Destination - o.Start).Normalize() * d, Halfling.Geometry.Direction.Zero, sim.Rand, null, null, null, loadInstantly: true, scaleFactor: 60f);
        };
        // hc_lead_route_encounters: a small red ring on each encounter near the direct route (screenshots only).
        Autotest.Commands["hc_lead_route_encounters"] = _ =>
        {
            var o = RequireLead();
            var sim = RequireSim();
            var rules = (Cosmoteer.Simulation.Doodads.POIDoodadRules)Cosmoteer.GameApp.Rules.GetDoodadRules(new ID<Cosmoteer.Simulation.Doodads.DoodadRules>("yarden_zamir.poi_escort_alone"));
            var near = LeadRoute.Near(sim, o.Start, o.Destination);
            foreach (var e in near)
                rules.SpawnAt(sim, new Vector2(e.At.X, e.At.Y), Halfling.Geometry.Direction.Zero, sim.Rand, null, null, null, loadInstantly: true, scaleFactor: 180f);
            Log.Info($"AUTOTEST lead route encounters marked: {near.Count}");
        };
        Autotest.Commands["hc_lead_route_camera"] = _ =>
        {
            var o = RequireLead();
            Vector2 mid = (o.Start + o.Destination) * 0.5f;
            float size = o.Start.DistanceTo(o.Destination) * 1.35f;
            Autotest.Commands["camera"](string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mid.X} {mid.Y} {size}"));
        };
        // hc_lead_collect: the receive payment button of the nearest station for the courier mission.
        Autotest.Commands["hc_lead_collect"] = _ => CollectAt(RequireLead().Mission ?? throw new InvalidOperationException("the courier objective has no mission"));
        // hc_guard_clear_raiders: removes the live raid ships of the newest escort (sim; "mp" in co-op), so a later step is not a fight.
        // Spec 2430 to 2434, the Mayday. hc_mayday_start: starts one now (no chance roll, no pacer).
        Autotest.Commands["hc_mayday_start"] = _ => _mayday = Mayday.Start(RequireRun(), RequireSim()) ?? throw new InvalidOperationException("no mayday started");
        // hc_mayday_roll <chance>: the arrival roll (spec 2430) with this chance; at 1 a mayday is due MaydayDelaySeconds later.
        Autotest.Commands["hc_mayday_roll"] = arg => Mayday.Roll(RequireRun(), RequireSim(), float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
        Autotest.Fields["hc_mayday_due"] = () => RequireRun().State.Mayday.Node is null ? 0 : 1;
        Autotest.Fields["hc_mayday_active"] = () => RequireRun().Mode.Missions.Count(m => m.MetatypeID.Equals(Mayday.Metatype) && m.Completion == CompletionState.InProgress);
        Autotest.Fields["hc_mayday_attackers"] = () => RequireMayday().AttackersLeft(RequireSim());
        Autotest.Fields["hc_mayday_hull"] = () => RequireMayday().HullPercent() ?? -1;
        Autotest.Fields["hc_mayday_done"] = () => RequireMayday().Completion == CompletionState.Completed ? 1 : 0;
        Autotest.Fields["hc_mayday_failed"] = () => RequireMayday().Completion == CompletionState.Failed ? 1 : 0;
        Autotest.Fields["hc_mayday_ring"] = () => RequireMayday().RingId is { } id && RequireSim().Doodads[id] is not null ? 1 : 0;
        // Live attackers whose attack order targets the victim.
        Autotest.Fields["hc_mayday_on_victim"] = () =>
        {
            var o = RequireMayday();
            Ship? victim = o.Victim.TargetShip;
            var sim = RequireSim();
            int n = 0;
            for (int i = 0; i < sim.Ships.Count; i++)
                if (sim.Ships[i].Metadata.PlayerIndex == o.Attackers && sim.Ships[i].Commands.CurConfirmedCommand is Cosmoteer.Ships.Commands.AttackCommand a && a.Target == victim && victim is not null) n++;
            return n;
        };
        // Distance from the victim to the nearest player ship: it runs to the players.
        Autotest.Fields["hc_mayday_victim_dist"] = () =>
        {
            Ship v = RequireMayday().Victim.TargetShip ?? throw new InvalidOperationException("victim is not live");
            return RequireSim().Ships.HumanPlayerSignificantShips.Min(h => h.DetWorldCenter.DistanceTo(v.DetWorldCenter));
        };
        // hc_mayday_camera <size>: the camera on the victim, this many units wide (screenshots).
        Autotest.Commands["hc_mayday_camera"] = arg =>
        {
            Vector2 at = RequireMayday().Victim.DetLocation() ?? throw new InvalidOperationException("the victim is not in this system");
            Autotest.Commands["camera"](string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{at.X} {at.Y} {arg.Trim()}"));
        };
        Autotest.Commands["hc_mayday_kill_attackers"] = _ =>
        {
            var o = RequireMayday();
            var sim = RequireSim();
            foreach (Ship s in Enumerable.Range(0, sim.Ships.Count).Select(i => sim.Ships[i]).Where(s => s.Metadata.PlayerIndex == o.Attackers).ToList())
                sim.Ships.Remove(s, dispose: true, handleLostShip: false);
        };
        Autotest.Commands["hc_mayday_kill_victim"] = _ =>
        {
            Ship v = RequireMayday().Victim.TargetShip ?? throw new InvalidOperationException("victim is not live");
            RequireSim().Ships.Remove(v, dispose: true, handleLostShip: false);
        };
        // hc_mayday_collect: the players at the station that pays (the mission's ReceiveRewards contact), then its receive payment button.
        // #313: the paying station gives missions (it has a mission module), so the pay step can register there.
        Autotest.Fields["hc_mayday_payer_missions"] = () => MaydayPayer().GetProvidedMissions() is null ? 0 : 1;
        // hc_military_goto: the players next to the nearest military station, live or in stasis, so a mayday that starts now finds it
        // nearest. Test only.
        Autotest.Commands["hc_military_goto"] = _ =>
        {
            var sim = RequireSim();
            Vector2 from = sim.Ships.HumanPlayerSignificantShips.First().DetWorldCenter;
            var military = TaggedShips(sim, "military_station").MinBy(t => t.Location.DistanceTo(from));
            if (military == default) throw new InvalidOperationException("no military station in this system");
            Log.Info($"AUTOTEST military station {military.Id} at {military.Location}");
            var at = Autotest.ClearSpotNear(sim, military.Location, 900f, sim.Ships.HumanPlayerSignificantShips.Max(s => s.WorldBoundingRadius));
            foreach (Ship s in sim.Ships.HumanPlayerSignificantShips.ToList()) Autotest.Teleport(s, at);
            Autotest.Wait(3);
        };
        Autotest.Commands["hc_mayday_collect"] = _ =>
        {
            var m = _mayday ?? throw new InvalidOperationException("no mayday; run hc_mayday_start first");
            var sim = RequireSim();
            var station = MaydayPayer();
            var me = sim.Ships.HumanPlayerSignificantShips.First();
            var at = Autotest.ClearSpotNear(sim, station.DetWorldCenter, 900f, me.WorldBoundingRadius);
            foreach (Ship s in sim.Ships.HumanPlayerSignificantShips.ToList()) Autotest.Teleport(s, at);
            var id = station.StationID;
            Autotest.WaitFor(() => sim.Ships[id] is not null, 20, "the station to leave stasis", then: () =>
                RequireRun().Game.NetManager.EnqueueInput(new Cosmoteer.Modes.Career.Comms.CommMissionsTab.MPMissionPaymentInput(sim.Ships[id]!, m)));
        };
        Autotest.Commands["hc_guard_clear_raiders"] = _ =>
        {
            foreach (Ship r in GuardRaiders()) RequireSim().Ships.Remove(r, dispose: true, handleLostShip: false);
        };
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
        : TunablesFile.ForNewGame(game).Missions;

    static int _markedMoney;
    static Cosmoteer.Game.ObjectID _markedGuardShip;
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
    static readonly HashSet<Cosmoteer.Game.ObjectID> _swept = [];
    static int _sweepErrors;

    static void TestMarkerSweep(string mode)
    {
        bool fail = mode switch { "complete" => false, "fail" => true, _ => throw new InvalidOperationException($"hc_markers_sweep: complete or fail, not '{mode}'") };
        var run = RequireRun();
        var sim = RequireSim();
        _swept.Clear();
        _sweepErrors = 0;
        // One offer can sit in several lists as copies with the same MissionID.
        foreach (var copies in Contracts.ContractBook.Offers(sim).GroupBy(o => o.Mission.MissionID).OrderBy(g => g.Key).ToList())
        {
            var m = copies.First().Mission;
            var ids = MissionMarkers.Of(m).Distinct()
                .Where(id => sim.Doodads[id] is Cosmoteer.Simulation.Doodads.POIDoodad poi && poi.Rules.ID == MissionMarkers.TargetArea).ToList();
            if (ids.Count == 0) continue;
            try
            {
                foreach (var (_, list) in copies) list.RemoveMission(m);
                run.Mode.Missions.AddMission(m, track: false);
                var objectives = new List<Cosmoteer.Modes.Career.Missions.Objectives.Objective>();
                m.GetObjectives(objectives);
                foreach (var o in objectives.Where(o => o.Completion == CompletionState.InProgress && MissionMarkers.OwnedBy(o).Any()))
                    if (fail) o.Fail(); else o.Complete();
                _swept.UnionWith(ids);
                Log.Info($"AUTOTEST MARKERS {mode} {m.MetatypeID} {m.MissionID}: {ids.Count} marker(s) [{string.Join(" ", ids)}]");
            }
            catch (Exception ex)
            {
                _sweepErrors++;
                Log.Error($"AUTOTEST MARKERS {mode} {m.MetatypeID} {m.MissionID}: {ex}");
            }
        }
        Autotest.Wait(2);
    }

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

    static void VisitCollect() => CollectAt(_visit ?? throw new InvalidOperationException("no Visit mission; run hc_visit_accept first"));

    /// <summary>
    /// Moves the players next to the station that pays the mission, then presses its receive payment button. Vanilla ReceiveRewards pays
    /// only at the station that offered the mission (Mission.SpawnedAt); a mission without one (a galaxy offer) pays at the nearest station
    /// that gives missions.
    /// </summary>
    internal static void CollectAt(Mission m)
    {
        var sim = RequireSim();
        var me = sim.Ships.HumanPlayerSignificantShips.First();
        // The offering station can be in stasis (far from the players): it wakes when they arrive.
        var station = m.SpawnedAt is { } at0
            ? SpaceStation.GetStations(sim, onlyOperating: false).Where(st => st.StationID == at0.ShipID).Select(st => (MetaSpaceStation?)st).FirstOrDefault()
              ?? throw new InvalidOperationException($"the station that offered {m.MetatypeID} is not in this system")
            : SpaceStation.GetStations(sim, onlyOperating: true).Where(st => st.GetProvidedMissions() is not null)
                .OrderBy(st => st.DetWorldCenter.DistanceTo(me.DetWorldCenter)).ThenBy(st => st.StationID).Select(st => (MetaSpaceStation?)st).FirstOrDefault();
        if (station is not { } st0) throw new InvalidOperationException("no station that gives missions in this system");
        var id = st0.StationID;
        var at = Autotest.ClearSpotNear(sim, st0.DetWorldCenter, 900f, me.WorldBoundingRadius);
        foreach (Ship s in sim.Ships.HumanPlayerSignificantShips.ToList()) Autotest.Teleport(s, at);
        Autotest.WaitFor(() => sim.Ships[id] is not null, 20, "the station to leave stasis", then: () =>
        {
            RequireRun().Game.NetManager.EnqueueInput(new Cosmoteer.Modes.Career.Comms.CommMissionsTab.MPMissionPaymentInput(sim.Ships[id]!, m));
            Log.Info($"AUTOTEST payment of {m.MetatypeID} at {sim.Ships[id]!.Metadata.PlainShipName}");
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
    static Mission? _mayday;

    /// <summary>The station where the newest mayday pays: the contact of its ReceiveRewards step.</summary>
    static MetaSpaceStation MaydayPayer()
    {
        var m = _mayday ?? throw new InvalidOperationException("no mayday; run hc_mayday_start first");
        var objectives = new List<Cosmoteer.Modes.Career.Missions.Objectives.Objective>();
        m.GetObjectives(objectives);
        var contact = objectives.OfType<Cosmoteer.Modes.Career.Missions.Objectives.ReceiveRewardsObjective>().Single().ContactShip
            ?? throw new InvalidOperationException("the mayday pays at no station");
        return SpaceStation.GetStations(RequireSim(), onlyOperating: false).Single(st => st.StationID == contact.ShipID);
    }
    static MaydayObjective RequireMayday()
    {
        var m = _mayday = _mayday ?? RequireRun().Mode.Missions.LastOrDefault(x => x.MetatypeID.Equals(Mayday.Metatype))
            ?? throw new InvalidOperationException("no mayday in the log");
        var found = new List<MaydayObjective>();
        m.GetObjectives(found);
        return found.Single();
    }
    static LeadObjective RequireLead() => RequireObjective<LeadObjective>();

    /// <summary>The newest objective that meets the players and then runs the guard rule.</summary>
    static IMeetingEscort RequireGuarded()
    {
        var found = new List<Cosmoteer.Modes.Career.Missions.Objectives.Objective>();
        foreach (Mission m in RequireRun().Mode.Missions)
            foreach (var o in m.Objectives) o.GetObjectives(found);
        return found.OfType<IMeetingEscort>().LastOrDefault() ?? throw new InvalidOperationException("no accepted mission has an escort");
    }

    static List<Ship> GuardRaiders()
    {
        int raiders = RequireGuarded().Guard.RaidAllegiance;
        return raiders == 0 ? [] : [.. RequireSim().Ships.Where(s => s.Metadata.PlayerIndex == raiders)];
    }
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
        // A clear spot (#308): the route point can lie on a rock or ship that loads out of stasis around the freighter and holds it there.
        Vector2 at = Autotest.ClearSpotNear(RequireSim(), o.Start + (o.Destination - o.Start) * share, 0f, escort.WorldBoundingRadius);
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

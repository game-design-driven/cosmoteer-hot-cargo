using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Autonomy;
using HotCargo.Core;

namespace HotCargo.Missions.Mechanics;

public enum MiningEscortPhase
{
    /// <summary>Spec 1850: the miner meets the players and waits for "Ready".</summary>
    Meet,
    ToField,
    Mining,
    Return,
}

/// <summary>
/// Rules type "HotCargoMiningEscort" (spec 1860 to 1867): escort a miner to an asteroid field, guard it while it fills its hold with the
/// field's ore and two raider waves come, and bring it back to the station. The miner works with vanilla systems: move orders, salvage
/// orders for its mining laser (<see cref="AutonomyWorld.OrderSalvage"/>) and collect orders for loose ore of its type, also ore that
/// players drop (<see cref="AutonomyWorld.OrderCollect"/>). Its AI (HotCargoMiner) has no strategy modules, so these orders stand.
/// It spawns the miner when the offer is made, and fails when the miner dies.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoMiningEscort")]
internal sealed class MiningEscortObjective : Objective, IMeetingEscort
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoMiningEscort")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        /// <summary>The spawn tag of asteroid field POIs. The miner takes the nearest field to the station that has ore and no hostile ship.</summary>
        [Serialize, PortableHash] public ID<SimObjectSpawner> FieldTag;
        [Serialize, PortableHash] public SpawnedObjectSearch StationTag = null!;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(StationTag, loc, rand, out var station);
            if (invalid) return null;
            var t = MechanicsArea.TunablesFor(sim.RequireGame()).MiningEscort;
            Vector2 at = station!.Location.Center;
            var fields = objects.TryGetValue(FieldTag, out var list) ? list.Select(f => f.Location.Center).OrderBy(c => c.DistanceTo(at)).ToList() : [];
            // Spec 1860: vanilla puts pirates or defense platforms in most fields (sysgen_asteroids.rules); a civilian miner avoids them.
            var picked = fields.Select(c => (Center: c, Ore: FieldOre(sim, c, t.FieldRadius))).FirstOrDefault(f => f.Ore is not null && !HostileNear(sim, f.Center, t.HostileRadius));
            if (picked.Ore is not { } ore)
            {
                Log.Info($"mining escort offer dropped: none of {fields.Count} asteroid fields has ore and no hostile ship");
                invalid = true;
                return null;
            }
            Vector2 center = picked.Center;
            ObjectID stationId = station!.UniqueID ?? throw new InvalidOperationException($"station tag {StationTag} found an object without an ID");
            int stationPlayer = sim.Ships[stationId]?.Metadata.PlayerIndex
                ?? (sim.Stasis.FirstOrDefault(s => s.ReservedObjectID == stationId) as StasisShipSpawner)?.ObjectAllegiance
                ?? throw new InvalidOperationException($"station {stationId} is neither live nor in stasis");
            if (Cosmoteer.Factions.FactionRules.GetFaction(stationPlayer) is not { } faction)
            {
                Log.Info("mining escort offer dropped: the station has no faction");
                invalid = true;
                return null;
            }
            Vector2 start = station.Location.Center + Vector2.FromAngle(rand.Direction()) * rand.Single(new Range<float>(t.SpawnDistanceMin, t.SpawnDistanceMax));
            var miner = MinerShip.Spawn(sim, rand, faction, start, rand.Direction());
            var game = sim.RequireGame();
            return new MiningEscortObjective(new ObjectiveTargetShip(game, miner), new ObjectiveTargetShip(game, sim.NodeId(), stationId), center, ore)
            {
                Start = start,
                Tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(center) + TierOffset),
            };
        }
    }

    /// <summary>
    /// Spec 1860: the most common ore of the asteroids within the radius, live or in stasis; ties go to the lower resource ID.
    /// Null when there is none. A live asteroid counts once for each ore that its parts hold.
    /// </summary>
    internal static ID<ResourceRules>? FieldOre(SimRoot sim, Vector2 center, float radius)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Count(string ore) => counts[ore] = counts.GetValueOrDefault(ore) + 1;
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var s = sim.Ships[i];
            if (!s.Rules.IsAsteroid || s.DetWorldCenter.DistanceTo(center) > radius) continue;
            foreach (var ore in s.Parts.SelectMany(p => p.Rules.Resources).Select(r => r.Type.ToString()).Distinct()) Count(ore);
        }
        foreach (var sp in sim.Stasis.OfType<StasisDoodadSpawner>())
            if (sp.Location.DistanceTo(center) <= radius && AutonomyModel.OreOfDoodad(sp.DoodadID.ToString()) is { } ore
                && GameApp.Rules.HasResourceRules(new ID<ResourceRules>(ore)))
                Count(ore);
        return counts.Count == 0 ? null : new ID<ResourceRules>(counts.MaxBy(kv => kv.Value).Key);
    }

    /// <summary>A ship within the radius, live or in stasis, that is an enemy of the human players.</summary>
    static bool HostileNear(SimRoot sim, Vector2 center, float radius)
    {
        var humans = sim.RequireGame().HumanPlayersIndexes;
        bool Hostile(int playerIndex) => humans.Any(h => sim.Mode.AreEnemies(playerIndex, h));
        for (int i = 0; i < sim.Ships.Count; i++)
            if (sim.Ships[i] is { IsJunk: false } s && s.DetWorldCenter.DistanceTo(center) <= radius && Hostile(s.Metadata.PlayerIndex)) return true;
        return sim.Stasis.OfType<StasisShipSpawner>().Any(s => s.Location.DistanceTo(center) <= radius && Hostile(s.ObjectAllegiance));
    }

    [Serialize] public ObjectiveTargetShip Escort { get; private set; }
    [Serialize] public ObjectiveTargetShip Station { get; private set; }
    [Serialize] public Vector2 Field { get; private set; }
    [Serialize] public ID<ResourceRules> Ore { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; }
    [Serialize(Optional = true)] public MiningEscortPhase Phase { get; private set; }
    [Serialize(Optional = true)] public Vector2 Start { get; private set; }
    [Serialize(Optional = true)] public bool Hailed;
    [Serialize(Optional = true)] public double HailIn;
    /// <summary>Spec 1861: the ore to bring back. 0 until the miner is first live.</summary>
    [Serialize(Optional = true)] public int Goal { get; private set; }
    /// <summary>Ore that the miner held when the goal was set. Only ore above it counts.</summary>
    [Serialize(Optional = true)] public int BaseOre { get; private set; }
    /// <summary>Ore that counts now (the last sim count).</summary>
    [Serialize(Optional = true)] public int Mined { get; private set; }
    /// <summary>Player index of all wave ships of this objective. 0 until the first wave.</summary>
    [Serialize(Optional = true)] public int WaveAllegiance { get; private set; }
    [Serialize(Optional = true)] public int WavesSent { get; private set; }
    /// <summary>Game seconds until the next work step.</summary>
    [Serialize(Optional = true)] public double StepIn { get; private set; }

    public bool Waiting => Completion == CompletionState.InProgress && Phase == MiningEscortPhase.Meet;

    public static readonly ID<SimObjectSpawner> WaveTag = new("hc_mining_raider");

    public override string ScriptTypeName => "HotCargoMiningEscort";

    public MiningEscortObjective(ObjectiveTargetShip miner, ObjectiveTargetShip station, Vector2 field, ID<ResourceRules> ore)
    {
        Escort = miner ?? throw new ArgumentNullException(nameof(miner));
        Station = station ?? throw new ArgumentNullException(nameof(station));
        Field = field;
        Ore = ore;
    }

    [GenericConstructor]
    MiningEscortObjective(GenericSerialReader reader)
    {
        Escort = null!;
        Station = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Escort.GetIntegrityHash(iHash);
        ((int)Phase).GetIntegrityHash(iHash);
        Goal.GetIntegrityHash(iHash);
        Mined.GetIntegrityHash(iHash);
        WavesSent.GetIntegrityHash(iHash);
        Hailed.GetIntegrityHash(iHash);
    }

    static MiningEscortTunables Settings(GameRoot game) => MechanicsArea.TunablesFor(game).MiningEscort;

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Escort.MapNode;

    /// <summary>Spec 1866: the map marks the miner while it waits, the field while it goes there and mines, then the station.</summary>
    public override Circle? GetAreaLocation(GameRoot game)
    {
        if (Completion != CompletionState.InProgress || game.Sim?.MapNode?.UniqueID != Escort.MapNodeID) return null;
        return Phase switch
        {
            MiningEscortPhase.Meet => Escort.DetLocation() is { } at ? new Circle(at, MissionArea.TunablesFor(game).EscortMarkerRadius) : null,
            MiningEscortPhase.Return => Station.DetLocation() is { } s ? new Circle(s, Settings(game).DockedGap) : null,
            _ => new Circle(Field, Settings(game).FieldRadius),
        };
    }

    public override bool IsShipAssociated(MetaShip ship) =>
        Completion == CompletionState.InProgress && (Escort.TargetMetaShip == ship || (Phase == MiningEscortPhase.Return && Station.TargetMetaShip == ship));

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Escort.IsAlive;

    ResourceRules OreRules => Ore.GetResourceRules();

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["target_ship"] = Escort.GetXmlTargetName(game.Sim);
        args["resource_type_plural"] = OreRules.PluralNameKey ?? OreRules.NameKey;
        args["resource_icon"] = OreRules.TextIconXml;
    }

    /// <summary>Spec 1873: the vanilla target focus, else the miner's place (also in stasis, not yet discovered).</summary>
    public override bool CanFocusDisplayLine(GameRoot game) => MissionFocus.CanFocus(game, Escort);

    public override void OnDisplayLineFocused(GameRoot game) => MissionFocus.Focus(game, Escort);

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        string name = Escort.GetXmlTargetName(game.Sim);
        string ore = OreRules.TextIconXml + " " + OreRules.GetPluralName();
        string goal = Goal > 0 ? Goal.ToString() : Strings.GetText("HotCargoMechanics/MiningGoalPending");
        var text = Phase switch
        {
            MiningEscortPhase.Meet => Strings.KeyStringFmt(Hailed ? "HotCargoObjectives/EscortAnswerFmt" : "HotCargoObjectives/EscortMeetFmt", name),
            MiningEscortPhase.ToField => Strings.KeyStringFmt("HotCargoMechanics/MiningToFieldFmt", name),
            MiningEscortPhase.Mining => Strings.KeyStringFmt("HotCargoMechanics/MiningFmt", name, ore, Mined, goal),
            MiningEscortPhase.Return => Strings.KeyStringFmt("HotCargoMechanics/MiningReturnFmt", name, Station.GetXmlTargetName(game.Sim)),
            _ => throw new InvalidOperationException($"unknown phase {Phase}"),
        };
        lines.Add(new DisplayLine { Text = text, State = Completion, CanFocus = CanFocusDisplayLine, OnFocused = OnDisplayLineFocused });
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        Escort.Killed += OnMinerKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && !Escort.IsAlive) OnMinerKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        Escort.Killed -= OnMinerKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnMinerKilled(object? sender, EventArgs e)
    {
        if (Completion != CompletionState.InProgress) return;
        Log.Info("mining escort failed: the miner died");
        Fail();
    }

    /// <summary>Spec 1850. Runs in the shared choice input.</summary>
    public void Depart(SimRoot sim, int playerIndex)
    {
        if (!Waiting) throw new InvalidOperationException("the miner is not waiting");
        SetPhase(MiningEscortPhase.ToField, $"answer by player {playerIndex}");
        Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/EscortDepartsFmt", Escort.GetXmlTargetName(sim)));
    }

    void SetPhase(MiningEscortPhase phase, string why)
    {
        Log.Info($"mining escort: {Phase} -> {phase} ({why}), ore {Mined}/{Goal}");
        Phase = phase;
        OnDisplayLinesChanged();
    }

    /// <summary>A moving player ship changes the meeting spot a little each tick. The move order follows only past this distance.</summary>
    const float MeetSlack = 150f;

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim || sim.NodeId() != Escort.MapNodeID) return;
        if (!Escort.IsAlive)
        {
            OnMinerKilled(null, EventArgs.Empty);
            return;
        }
        // In stasis the miner does not move or mine, like every ship far from the players.
        if (Escort.TargetShip is not { } ship) return;
        var t = Settings(sim.RequireGame());
        if (Goal == 0)
        {
            BaseOre = OreHeld(ship);
            Goal = MechanicsModel.OreGoal(AutonomyWorld.CargoRoom(ship, Ore), t.OreGoal);
            Log.Info($"mining escort: {ship.Metadata.PlainShipName} mines {Ore}, goal {Goal} (room {AutonomyWorld.CargoRoom(ship, Ore)}, held {BaseOre})");
            OnDisplayLinesChanged();
        }
        if (Phase == MiningEscortPhase.Meet)
        {
            if (EscortMeeting.Step(sim, ship, Start, ref HailIn, ref Hailed, out bool firstHail) is { } spot) MoveTo(ship, spot, null, MeetSlack);
            if (firstHail) OnDisplayLinesChanged();
            return;
        }
        StepIn -= sim.FixedUpdater.Interval.Seconds;
        if (StepIn > 0) return;
        StepIn = t.StepSeconds;
        Count(ship);
        switch (Phase)
        {
            case MiningEscortPhase.ToField:
                // Spec 1864: on the way it stops for matching loose ore within reach, so ore that players drop counts here too.
                if (CollectLoose(sim, ship, t) > 0)
                {
                    if (ship.Commands.CurConfirmedCommand is not null) ship.Commands.Clear();
                    break;
                }
                MoveTo(ship, Field, null, t.FieldRadius * 0.25f);
                if (ship.DetWorldCenter.DistanceTo(Field) <= t.FieldRadius * 0.5f) SetPhase(MiningEscortPhase.Mining, "at the field");
                break;
            case MiningEscortPhase.Mining:
                Mine(sim, ship, t);
                break;
            case MiningEscortPhase.Return:
                Dock(ship, t);
                break;
        }
    }

    int OreHeld(Ship ship) => ship.Resources.GetResourceTotal(Ore, includeStored: true, includeCarried: true);

    /// <summary>The counted ore: what the miner holds above BaseOre. The HUD line follows it.</summary>
    void Count(Ship ship)
    {
        int before = Mined;
        Mined = Math.Max(0, OreHeld(ship) - BaseOre);
        if (Mined != before) OnDisplayLinesChanged();
    }

    /// <summary>Spec 1864: collect orders for loose ore of the miner's type within CollectRadius of its hull. Returns how many nuggets got one.</summary>
    int CollectLoose(SimRoot sim, Ship ship, MiningEscortTunables t)
    {
        var loose = new List<Nugget>();
        for (int i = 0; i < sim.Nuggets.Count; i++)
        {
            var n = sim.Nuggets[i];
            if (n.ResourceType == Ore && AutonomyWorld.EdgeDistance(ship, n.DetWorldLocation) <= t.CollectRadius) loose.Add(n);
        }
        AutonomyWorld.OrderCollect(ship, loose);
        return loose.Count;
    }

    /// <summary>
    /// Spec 1862 to 1864: sends the waves that are due, orders loose ore of its type collected, and mines the nearest asteroid with that ore.
    /// A full goal, or a system without such an asteroid, sends the miner home.
    /// </summary>
    void Mine(SimRoot sim, Ship ship, MiningEscortTunables t)
    {
        int due = MechanicsModel.WavesReached(Mined, Goal, t.WaveShares) - WavesSent;
        for (int i = 0; i < due; i++) SpawnWave(sim, ship, t);
        if (Mined >= Goal)
        {
            SetPhase(MiningEscortPhase.Return, "hold full");
            return;
        }
        int loose = CollectLoose(sim, ship, t);
        var mine = new CargoSettings(0, [], [], [Ore.ToString()], []);
        var a = Run.Current?.T.Autonomy ?? throw new InvalidOperationException("no run for the autonomy tunables");
        if (AutonomyWorld.NearestAsteroid(ship, mine) is not { } rock)
        {
            if (loose == 0) SetPhase(MiningEscortPhase.Return, "no asteroid with the ore is left");
            return;
        }
        if (rock.Live is not { } live)
        {
            MoveTo(ship, rock.Center, null, a.MineDistance);
            return;
        }
        if (AutonomyWorld.WorkSpot(ship, live, mine, a.MineDistance, a.MineReach) is { } work) MoveTo(ship, work.Spot, work.Rotation, a.MineDistance * 0.25f);
        if (AutonomyWorld.EdgeDistance(ship, live.DetWorldCenter) - live.WorldBoundingRadius <= a.MineDistance * 3f) AutonomyWorld.OrderSalvage(ship, live, mine, a.SalvageJobs);
    }

    /// <summary>Spec 1865: flies DockGap from the station's hull; docked within DockedGap completes the objective, and the pay waits at the station.</summary>
    void Dock(Ship ship, MiningEscortTunables t)
    {
        if (Station.TargetShip is not { } station)
        {
            if (Station.DetLocation() is { } far) MoveTo(ship, far, null, t.DockedGap);
            return;
        }
        Circle s = station.DetWorldBoundingCircle, m = ship.DetWorldBoundingCircle;
        Vector2 dir = Vector2.FromAngle(s.Center.DirectionTo(m.Center));
        MoveTo(ship, s.Center + dir * (s.Radius + m.Radius + t.DockGap), null, t.DockGap);
        if (MechanicsModel.Gap(s.Center.DistanceTo(m.Center), s.Radius, m.Radius) > t.DockedGap) return;
        Log.Info($"mining escort: {ship.Metadata.PlainShipName} docked with {Mined} {Ore}");
        Complete();
    }

    /// <summary>A move order to the point, unless the current order already goes within slack of it (and turns within 10 degrees).</summary>
    static void MoveTo(Ship ship, Vector2 to, Direction? rotation, float slack)
    {
        if (ship.Commands.CurConfirmedCommand is MoveCommand { Location: { } at } m && at.DistanceTo(to) <= slack
            && (rotation is not { } r || m.Rotation is { } mr && (float)mr.AngularDistanceTo(r) <= 10f * MathF.PI / 180f)) return;
        ship.Commands.EnqueueCommand(new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), to, rotation, CollisionAvoidanceFlags.All, isTentative: false),
            append: false, confirmed: true);
    }

    /// <summary>Spec 1863: one raider wave around the miner, from a random side. Sim tick only (sim.Rand). Internal for the autotest.</summary>
    internal void SpawnWave(SimRoot sim, Ship miner, MiningEscortTunables t)
    {
        var career = (CareerSimModeManager)sim.Mode;
        if (WaveAllegiance == 0) WaveAllegiance = career.AllocUniquePlayerIndex();
        int live = SimQueries.TaggedShips(sim, WaveTag).Count(s => s.PlayerIndex == WaveAllegiance);
        // Spec 334: co-op scales the wave and the live cap.
        int wanted = Scaling.EnemyScale.FightCount(MechanicsModel.WaveShipCount(t.ShipsBase, t.ShipsPerTier, Tier, 0, int.MaxValue), sim);
        int count = Math.Clamp(wanted, 0, Math.Max(0, Scaling.EnemyScale.FightCap(t.MaxLiveShips, sim) - live));
        int shipTier = MechanicsModel.WaveShipTier(t.ShipTierBase, t.ShipTierPerTier, Tier, t.ShipTierMax);
        WaveSpawns.Spawn(sim, miner.DetWorldCenter, sim.Rand.Direction(), count, shipTier, t.ShipTags, WaveAllegiance, WaveTag);
        WavesSent++;
        Log.Info($"mining escort: wave {WavesSent} of {count} tier-{shipTier} ships at ore {Mined}/{Goal} ({live} still live)");
    }

    public override int GetTypeIntegrityHash() => 0x4843_4D45; // "HCME"
}

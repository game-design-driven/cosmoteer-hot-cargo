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
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// Rules type "HotCargoEscort" (spec requirement 36): completes when the escorted AI ship arrives at the destination (spec 1877, ShipDelivery).
/// While the mission is active, the escort is live and a player ship is near (spec 1881, <see cref="EscortGuard"/>), the objective points
/// the escort AI at the destination. Alone, the escort holds where it is, draws raiders, and in stasis it is lost (spec 1882, 1883).
/// Put it in an AnyOrder next to a ProtectShips on the same ship: ProtectShips fails the mission if the escort dies.
/// At creation it moves the destination POI to MissionTunables.EscortRouteShare of the gate ring. When the escort covers
/// EscortAmbushAt of the route, it spawns the raiders from missions/convoy_ambush.rules ahead of the escort.
/// It removes the POI when it completes or fails.
/// Spec 1850 to 1855: after the accept the escort first meets the players. It flies to the nearest player ship, hails it and waits
/// for "Ready" (<see cref="EscortHail"/>). Only then does it fly the route.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoEscort")]
internal sealed class EscortObjective : Objective, IMissionMarkers, IMeetingEscort
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoEscort")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch EscortTag = null!;
        /// <summary>A POI doodad that marks the destination. The objective reveals it on acceptance.</summary>
        [Serialize, PortableHash] public SpawnedObjectSearch DestinationTag = null!;
        [Serialize(Optional = true), PortableHash] public int TierOffset;
        /// <summary>The straight route keeps this distance from every sun.</summary>
        [Serialize(Optional = true), PortableHash] public float SunClearance = 6000f;
        /// <summary>Spec 1886: game seconds alone before raiders take the escort. 0: Missions.EscortLostSeconds.</summary>
        [Serialize(Optional = true), PortableHash] public float LostSeconds;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            // A missing tag means the escort group could not place its destination. Drop the offer instead of crashing generation.
            invalid = !objects.TryFindObject(EscortTag, loc, rand, out var escort) | !objects.TryFindObject(DestinationTag, loc, rand, out var dest);
            if (invalid) return null;
            ObjectID escortId = escort!.UniqueID ?? throw new InvalidOperationException($"escort tag {EscortTag} found an object without an ID");
            ObjectID poiId = dest!.UniqueID ?? throw new InvalidOperationException($"destination tag {DestinationTag} found an object without an ID");
            // POI doodads never go into stasis (POIDoodadRules.CanSpawnIntoStasis), so the spawned POI is live.
            var poi = sim.Doodads[poiId] as POIDoodad ?? throw new InvalidOperationException($"destination {poiId} is not a live POI doodad");
            Vector2 start = escort.Location.Center;
            if (Route(sim, objects, start, poi.Location, SunClearance) is not { } destination)
            {
                invalid = true;
                return null;
            }
            poi.Location = destination;
            var mode = (CareerSimModeManager)sim.Mode;
            return new EscortObjective(new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), escortId), destination)
            {
                DestinationPoiID = poiId,
                Start = start,
                AmbushSeed = rand.UInt64(),
                Guard = new EscortGuardState { LostSeconds = EscortGuard.CheckLostSeconds(sim, LostSeconds) },
                RouteSunClearance = SunClearance,
                Tier = Math.Max(1, mode.TierFields.GetTier(destination) + TierOffset),
            };
        }

    }

    /// <summary>
    /// The destination at EscortRouteShare of the nearest gate's distance from the center (spec 36, spec 105: inside the gate ring), with the
    /// straight route <paramref name="sunClearance"/> from every sun. The first direction tried is toward the rules POI. Null drops the
    /// offer: no gate, or no direction fits.
    /// </summary>
    internal static Vector2? Route(SimRoot sim, SpawnedObjectCollection objects, Vector2 start, Vector2 rulesDestination, float sunClearance) =>
        RouteCandidates(sim, objects, start, rulesDestination, sunClearance) is [var first, ..] ? first : null;

    /// <summary>
    /// Every destination that fits the convoy route rules of <see cref="Route"/>, in the order that it tries them. Empty when none fits.
    /// The Blind Courier picks among them (spec 1890).
    /// </summary>
    internal static List<Vector2> RouteCandidates(SimRoot sim, SpawnedObjectCollection objects, Vector2 start, Vector2 rulesDestination, float sunClearance)
    {
        List<System.Numerics.Vector2> Tagged(string tag) =>
            objects.TryGetValue(new ID<SimObjectSpawner>(tag), out var list) ? [.. list.Select(o => N(o.Location.Center))] : [];
        var gates = Tagged("ftl_gate");
        if (gates.Count == 0)
        {
            Log.Info("escort offer dropped: no FTL gate in this system");
            return [];
        }
        float reach = gates.Min(g => g.Length());
        float length = MissionArea.TunablesFor(sim.RequireGame()).EscortRouteShare * reach;
        Vector2 toward = rulesDestination - start;
        if (toward == Vector2.Zero) toward = new Vector2(1f, 0f);
        var fits = MissionModel.RouteCandidates(N(start), N(toward), length, reach, sunClearance, Tagged("sun"));
        if (fits.Count == 0) Log.Info($"escort offer dropped: no route of {length:0} from {start} inside {reach:0}");
        return [.. fits.Select(d => new Vector2(d.X, d.Y))];
    }

    [Serialize] public ObjectiveTargetShip Escort { get; private set; }
    [Serialize] public Vector2 Destination { get; private set; }
    [Serialize(Optional = true)] public ObjectID? DestinationPoiID { get; private set; }

    IEnumerable<ObjectID> IMissionMarkers.MarkerIds => new[] { DestinationPoiID, Guard.MarkerId }.OfType<ObjectID>();
    [Serialize(Optional = true)] public int Tier { get; private set; }
    /// <summary>Where the escort started. The ambush springs at a share of the route from here.</summary>
    [Serialize(Optional = true)] public Vector2 Start { get; private set; }
    [Serialize(Optional = true)] public ulong AmbushSeed { get; private set; }
    [Serialize(Optional = true)] public bool AmbushSprung { get; private set; }
    /// <summary>Spec 1850: a player answered the hail with "Ready". Until then the escort meets the players and the route waits.</summary>
    [Serialize(Optional = true)] public bool Ready { get; private set; }
    /// <summary>The escort hailed the players at least once.</summary>
    [Serialize(Optional = true)] public bool Hailed;
    /// <summary>Game seconds until the waiting escort may hail again.</summary>
    [Serialize(Optional = true)] public double HailIn;
    /// <summary>The straight route keeps this distance from every sun (the spawner's SunClearance).</summary>
    [Serialize(Optional = true)] public float RouteSunClearance { get; private set; }
    /// <summary>Spec 1855: a stop before the destination, when the straight route from the meeting place passes a sun.</summary>
    [Serialize(Optional = true)] public Vector2? Via { get; private set; }
    /// <summary>Spec 1881 to 1884: the guard rule after "Ready".</summary>
    [Serialize(Optional = true)] public EscortGuardState Guard { get; private set; } = new();

    /// <summary>Spec 1882: guard raids carry the convoy raider tag, so they want the freighter like the ambush (ConvoyTargetPatch).</summary>
    static readonly ID<SimObjectSpawner> RaiderTag = new("hc_escort_raider");

    /// <summary>The escort waits for the players' answer (spec 1850).</summary>
    public bool Waiting => Completion == CompletionState.InProgress && !Ready;

    public override string ScriptTypeName => "HotCargoEscort";

    public EscortObjective(ObjectiveTargetShip escort, Vector2 destination)
    {
        Escort = escort ?? throw new ArgumentNullException(nameof(escort));
        Destination = destination;
    }

    [GenericConstructor]
    EscortObjective(GenericSerialReader reader)
    {
        Escort = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Escort.GetIntegrityHash(iHash);
        Tier.GetIntegrityHash(iHash);
        AmbushSprung.GetIntegrityHash(iHash);
        Ready.GetIntegrityHash(iHash);
        Hailed.GetIntegrityHash(iHash);
        Guard.Raided.GetIntegrityHash(iHash);
        Guard.Guarded.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Escort.MapNode;

    /// <summary>Spec 1854: the map marks the waiting escort, then the destination.</summary>
    public override Circle? GetAreaLocation(GameRoot game)
    {
        if (Completion != CompletionState.InProgress || game.Sim?.MapNode?.UniqueID != Escort.MapNodeID) return null;
        if (Ready) return ShipDelivery.Area(game, new Circle(Destination, 0f));
        return Escort.DetLocation() is { } at ? new Circle(at, MissionArea.TunablesFor(game).EscortMarkerRadius) : null;
    }

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Escort.TargetMetaShip == ship;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && poi.UniqueID == DestinationPoiID;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Escort.IsAlive;

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["escort_ship"] = Escort.GetXmlTargetName(game.Sim);
    }

    /// <summary>Spec 2292: the vanilla target focus, else the escort's place (also in stasis, not yet discovered).</summary>
    public override bool CanFocusDisplayLine(GameRoot game) => MissionFocus.CanFocus(game, Escort);

    public override void OnDisplayLineFocused(GameRoot game) => MissionFocus.Focus(game, Escort);

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        string name = Escort.GetXmlTargetName(game.Sim);
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt(Ready ? "HotCargoObjectives/EscortFmt" : Hailed ? "HotCargoObjectives/EscortAnswerFmt" : "HotCargoObjectives/EscortMeetFmt", name),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });
        if (Ready && Completion == CompletionState.InProgress && EscortGuard.AloneLine(Guard, name, game) is { } alone) lines.Add(alone);
    }

    int _shownSeconds = -1;

    /// <summary>Spec 1886: the alone line counts down once a second.</summary>
    public override void RefreshDisplayLines(GameRoot game)
    {
        int shown = EscortGuard.ShownSeconds(Guard, game);
        if (shown == _shownSeconds) return;
        _shownSeconds = shown;
        OnDisplayLinesChanged();
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (DestinationPoiID is { } poiId && game.Sim?.Doodads[poiId] is POIDoodad poi) poi.IsUndiscovered = false;
        Escort.Killed += OnEscortKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && !Escort.IsAlive) OnEscortKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        Escort.Killed -= OnEscortKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnEscortKilled(object? sender, EventArgs e)
    {
        if (Completion != CompletionState.InProgress) return;
        Fail();
        if (Mission?.Game.Sim is { } sim) RemoveMarker(sim);
    }

    /// <summary>
    /// Removes the destination POI, like vanilla AutoRemoveWhenTagDestroyed does. Only in the escort's system: object IDs belong to one system.
    /// Limit: a failure while the players are in another system leaves the POI there. Revisit if a freighter can die in stasis.
    /// </summary>
    void RemoveMarker(SimRoot sim)
    {
        if (sim.NodeId() != Escort.MapNodeID || DestinationPoiID is not { } id || sim.Doodads[id] is not POIDoodad poi) return;
        sim.Doodads.Remove(poi, dispose: true);
        Log.Info("escort marker removed");
    }

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        if (sim.NodeId() != Escort.MapNodeID || Escort.DetLocation() is not { } at) return;
        if (!Ready)
        {
            Meet(sim);
            return;
        }
        // Spec 1877: the shared arrival rule, for the stop and for the destination (points, so no hull).
        bool AtPoint(Vector2 point) => Escort.TargetShip is { } ship && ShipDelivery.Arrived(sim.RequireGame(), ship.DetWorldBoundingCircle, new Circle(point, 0f));
        if (Via is { } via && AtPoint(via)) Via = null;
        // Spec 1881: alone, the escort holds where it was when the last player ship left.
        bool guarded = EscortGuard.Tick(sim, Escort, Guard, Tier, RaiderTag, OnDisplayLinesChanged, () => OnEscortKilled(null, EventArgs.Empty));
        if (Completion != CompletionState.InProgress) return;
        if (Escort.TargetShip?.AI is { } ai) SteerTo(ai, guarded ? Via ?? Destination : Guard.HoldAt ?? at, 0f);
        float left = at.DistanceTo(Destination);
        if (!AmbushSprung && Escort.TargetShip is not null
            && MissionModel.RouteProgress(Start.DistanceTo(Destination), left) >= MissionArea.TunablesFor(sim.RequireGame()).EscortAmbushAt)
            SpringAmbush(sim, at);
        if (AtPoint(Destination))
        {
            Log.Info($"escort {Escort.GetPlainTargetName(sim)} arrived");
            Complete();
            RemoveMarker(sim);
        }
    }

    /// <summary>Spawns the raiders EscortAmbushLead ahead of the escort, on its route. Internal for the autotest.</summary>
    internal void SpringAmbush(SimRoot sim, Vector2 at)
    {
        // Set first: a broken ambush file must not retry on every tick.
        AmbushSprung = true;
        float lead = Math.Min(MissionArea.TunablesFor(sim.RequireGame()).EscortAmbushLead, at.DistanceTo(Destination));
        Vector2 root = at + (Destination - at).Normalize() * lead;
        try
        {
            AmbushFile.Convoy.Generator.Generate(sim, new FastRand(AmbushSeed, 0x4843_4553_414DUL), loadInstantly: true, rootLoc: root);
        }
        catch (Exception ex)
        {
            // The escort goes on without a fight.
            Errors.Report("convoy ambush", ex.Message, ex);
            return;
        }
        Log.Info($"convoy ambush sprung at {root}");
    }

    /// <summary>
    /// Moves the AI origin to a point, unless it is already within <paramref name="slack"/> of it. The escort rules give a small
    /// TargetUnacquireRangeFromOrigin, so AggroEnemies keeps flying it to the origin and fights only while it moves.
    /// </summary>
    static void SteerTo(ShipAI ai, Vector2 to, float slack)
    {
        if (ai.Parameters?.PatrolOrigin is { } origin && origin.DistanceTo(to) <= slack && (slack > 0f || origin == to)) return;
        AIParameters p = ai.Parameters?.Clone() ?? new AIParameters();
        p.PatrolOrigin = to;
        ai.Parameters = p;
    }

    /// <summary>A moving player ship changes the meeting spot a little each tick. The AI origin follows only past this distance.</summary>
    const float MeetSlack = 150f;

    /// <summary>Spec 1851 to 1853, sim tick: the live escort flies to the meeting spot and hails (<see cref="EscortMeeting.Step"/>).</summary>
    void Meet(SimRoot sim)
    {
        if (Escort.TargetShip is not { } ship) return;
        var spot = EscortMeeting.Step(sim, ship, Start, ref HailIn, ref Hailed, out bool firstHail);
        if (spot is { } to && ship.AI is { } ai) SteerTo(ai, to, MeetSlack);
        if (firstHail) OnDisplayLinesChanged();
    }

    /// <summary>
    /// Spec 1850, 1855: a player answered "Ready". The route starts where the escort is. When the straight route from here passes a sun
    /// closer than RouteSunClearance, the escort first flies back to its start, where the checked route began. Runs in the shared choice input.
    /// </summary>
    public void Depart(SimRoot sim, int playerIndex)
    {
        if (!Waiting) throw new InvalidOperationException("the escort is not waiting");
        Ready = true;
        if (Escort.DetLocation() is { } at)
        {
            var suns = sim.Doodads.Where(d => d.Tags?.Contains(SunTag) == true).Select(d => N(d.Location));
            if (!MissionModel.RouteClear(N(at), N(Destination), RouteSunClearance, suns)) Via = Start;
            else Start = at;
        }
        Log.Info($"escort {Escort.GetPlainTargetName(sim)} departs, answer by player {playerIndex}, via start {Via is not null}");
        Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/EscortDepartsFmt", Escort.GetXmlTargetName(sim)));
        OnDisplayLinesChanged();
    }

    static readonly ID<SimObjectSpawner> SunTag = new("sun");

    static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);

    public override int GetTypeIntegrityHash() => 0x4843_4553; // "HCES"
}

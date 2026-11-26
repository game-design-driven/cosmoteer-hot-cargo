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
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// Rules type "HotCargoLead" (spec 1890 to 1897), the Blind Courier: a weak courier with a dead navigation core follows the players to a
/// jump point. It meets the players and waits for "Ready" like every escort (spec 1850 to 1854), then follows the nearest player ship.
/// Raiders come at shares of the route, the guard rule holds (spec 1881 to 1885), and arrival (spec 1877) completes the step.
/// It spawns the courier when the offer is made, and fails when the courier dies.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoLead")]
internal sealed class LeadObjective : Objective, IMissionMarkers, IMeetingEscort
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoLead")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        /// <summary>A POI doodad that marks the destination. The objective moves it onto the route and reveals it on acceptance.</summary>
        [Serialize, PortableHash] public SpawnedObjectSearch DestinationTag = null!;
        [Serialize, PortableHash] public SpawnedObjectSearch StationTag = null!;
        [Serialize(Optional = true), PortableHash] public int TierOffset;
        /// <summary>The straight route keeps this distance from every sun.</summary>
        [Serialize(Optional = true), PortableHash] public float SunClearance = 6000f;
        /// <summary>Spec 1886: game seconds alone before raiders take the courier. 0: Missions.EscortLostSeconds.</summary>
        [Serialize(Optional = true), PortableHash] public float LostSeconds;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(StationTag, loc, rand, out var station) | !objects.TryFindObject(DestinationTag, loc, rand, out var dest);
            if (invalid) return null;
            ObjectID poiId = dest!.UniqueID ?? throw new InvalidOperationException($"destination tag {DestinationTag} found an object without an ID");
            // POI doodads never go into stasis (POIDoodadRules.CanSpawnIntoStasis), so the spawned POI is live.
            var poi = sim.Doodads[poiId] as POIDoodad ?? throw new InvalidOperationException($"destination {poiId} is not a live POI doodad");
            ObjectID stationId = station!.UniqueID ?? throw new InvalidOperationException($"station tag {StationTag} found an object without an ID");
            int stationPlayer = sim.Ships[stationId]?.Metadata.PlayerIndex
                ?? (sim.Stasis.FirstOrDefault(s => s.ReservedObjectID == stationId) as StasisShipSpawner)?.ObjectAllegiance
                ?? throw new InvalidOperationException($"station {stationId} is neither live nor in stasis");
            if (Cosmoteer.Factions.FactionRules.GetFaction(stationPlayer) is not { } faction)
            {
                Log.Info("blind courier offer dropped: the station has no faction");
                invalid = true;
                return null;
            }
            var t = MissionArea.TunablesFor(sim.RequireGame());
            Vector2 start = station.Location.Center + Vector2.FromAngle(rand.Direction()) * rand.Single(new Range<float>(t.LeadSpawnDistanceMin, t.LeadSpawnDistanceMax));
            // Spec 1890: the jump point whose straight route passes the most encounters.
            if (LeadRoute.Pick(sim, objects, start, poi.Location, SunClearance) is not { } route)
            {
                invalid = true;
                return null;
            }
            Vector2 destination = route.Destination;
            poi.Location = destination;
            var courier = CourierShip.Spawn(sim, rand, faction, start, rand.Direction());
            return new LeadObjective(new ObjectiveTargetShip(sim.RequireGame(), courier), destination)
            {
                DestinationPoiID = poiId,
                Start = start,
                Tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(destination) + TierOffset),
                RouteDanger = route.Danger,
                Guard = new EscortGuardState { LostSeconds = EscortGuard.CheckLostSeconds(sim, LostSeconds) },
            };
        }
    }

    [Serialize] public ObjectiveTargetShip Escort { get; private set; }
    [Serialize] public Vector2 Destination { get; private set; }
    [Serialize(Optional = true)] public ObjectID? DestinationPoiID { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; }
    /// <summary>Where the route starts: the courier's spawn spot, then where it was when a player answered "Ready".</summary>
    [Serialize(Optional = true)] public Vector2 Start { get; private set; }
    [Serialize(Optional = true)] public bool Ready { get; private set; }
    [Serialize(Optional = true)] public bool Hailed;
    [Serialize(Optional = true)] public double HailIn;
    /// <summary>Spec 1893: the one ambush came.</summary>
    [Serialize(Optional = true)] public bool Ambushed { get; private set; }
    /// <summary>Spec 1890: the main kind of danger on the direct route. It picks the mood line of the offer.</summary>
    [Serialize(Optional = true)] public RouteDanger RouteDanger { get; private set; }
    [Serialize(Optional = true)] public EscortGuardState Guard { get; private set; } = new();

    IEnumerable<ObjectID> IMissionMarkers.MarkerIds => new[] { DestinationPoiID, Guard.MarkerId }.OfType<ObjectID>();

    public bool Waiting => Completion == CompletionState.InProgress && !Ready;

    /// <summary>Spec 1893: ambushes and guard raids carry the convoy raider tag, so they want the courier (ConvoyTargetPatch).</summary>
    static readonly ID<SimObjectSpawner> RaiderTag = new("hc_escort_raider");

    public override string ScriptTypeName => "HotCargoLead";

    public LeadObjective(ObjectiveTargetShip courier, Vector2 destination)
    {
        Escort = courier ?? throw new ArgumentNullException(nameof(courier));
        Destination = destination;
    }

    [GenericConstructor]
    LeadObjective(GenericSerialReader reader)
    {
        Escort = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Escort.GetIntegrityHash(iHash);
        Ready.GetIntegrityHash(iHash);
        Hailed.GetIntegrityHash(iHash);
        Ambushed.GetIntegrityHash(iHash);
        Guard.Raided.GetIntegrityHash(iHash);
        Guard.Guarded.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Escort.MapNode;

    /// <summary>Spec 1895: the map marks the waiting courier, then the destination ring that its hull must reach.</summary>
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
        args["route_hint"] = LeadRoute.Hint(RouteDanger);
    }

    /// <summary>Spec 2292, 1895: the vanilla target focus, else the courier's place (also in stasis, not yet discovered).</summary>
    public override bool CanFocusDisplayLine(GameRoot game) => MissionFocus.CanFocus(game, Escort);

    public override void OnDisplayLineFocused(GameRoot game) => MissionFocus.Focus(game, Escort);

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        string name = Escort.GetXmlTargetName(game.Sim);
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt(Ready ? "HotCargoObjectives/LeadFmt" : Hailed ? "HotCargoObjectives/EscortAnswerFmt" : "HotCargoObjectives/EscortMeetFmt", name),
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
        Escort.Killed += OnCourierKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && !Escort.IsAlive) OnCourierKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        Escort.Killed -= OnCourierKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnCourierKilled(object? sender, EventArgs e)
    {
        if (Completion != CompletionState.InProgress) return;
        Log.Info("blind courier failed: the courier died");
        Fail();
    }

    /// <summary>Spec 1850, 1892. Runs in the shared choice input: the route starts where the courier is.</summary>
    public void Depart(SimRoot sim, int playerIndex)
    {
        if (!Waiting) throw new InvalidOperationException("the courier is not waiting");
        Ready = true;
        if (Escort.DetLocation() is { } at) Start = at;
        Log.Info($"blind courier {Escort.GetPlainTargetName(sim)} follows, answer by player {playerIndex}");
        Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/LeadDepartsFmt", Escort.GetXmlTargetName(sim), LeadRoute.Hint(RouteDanger)));
        OnDisplayLinesChanged();
    }

    /// <summary>A moving player ship changes the meeting spot a little each tick. The move order follows only past this distance.</summary>
    const float MeetSlack = 150f;

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim || sim.NodeId() != Escort.MapNodeID) return;
        if (!Escort.IsAlive)
        {
            OnCourierKilled(null, EventArgs.Empty);
            return;
        }
        // Spec 1881 to 1883: after the answer the guard rule runs, also in stasis, where only its clock can lose the courier.
        bool guarded = !Ready || EscortGuard.Tick(sim, Escort, Guard, Tier, RaiderTag, OnDisplayLinesChanged, () => OnCourierKilled(null, EventArgs.Empty));
        if (Completion != CompletionState.InProgress || Escort.TargetShip is not { } ship) return;
        var t = MissionArea.TunablesFor(sim.RequireGame());
        if (!Ready)
        {
            if (EscortMeeting.Step(sim, ship, Start, ref HailIn, ref Hailed, out bool firstHail) is { } spot) MoveOrder.To(ship, spot, MeetSlack);
            if (firstHail) OnDisplayLinesChanged();
            return;
        }
        if (!guarded)
        {
            if (Guard.HoldAt is { } hold) MoveOrder.To(ship, hold, t.LeadFollowSlack);
            return;
        }
        // Spec 1877: the shared arrival rule. The jump point is a point, so it has no hull. Arrival comes first: no ambush on the last tick.
        if (ShipDelivery.Arrived(sim.RequireGame(), ship.DetWorldBoundingCircle, new Circle(Destination, 0f)))
        {
            Log.Info($"blind courier {ship.Metadata.PlainShipName} arrived");
            Complete();
            return;
        }
        Follow(sim, ship, t);
        Ambush(sim, ship, t);
    }

    /// <summary>Spec 1892: a move order to LeadFollowGap behind the nearest player ship's hull (lowest ship ID on a tie). It stops where that ship stops.</summary>
    static void Follow(SimRoot sim, Ship ship, MissionTunables t)
    {
        Vector2 at = ship.DetWorldCenter;
        var lead = sim.Ships.HumanPlayerSignificantShips.OrderBy(h => h.DetWorldCenter.DistanceSquaredTo(at)).ThenBy(h => h.UniqueID).FirstOrDefault();
        if (lead is null) return;
        var spot = MissionModel.FollowSpot(N(lead.DetWorldCenter), lead.DetWorldBoundingCircle.Radius, N(at), ship.DetWorldBoundingCircle.Radius, t.LeadFollowGap);
        MoveOrder.To(ship, new Vector2(spot.X, spot.Y), t.LeadFollowSlack);
    }

    /// <summary>Spec 1893: one raid ahead of the courier, toward the jump point, when its route progress reaches LeadAmbushAt.</summary>
    void Ambush(SimRoot sim, Ship ship, MissionTunables t)
    {
        if (Ambushed) return;
        float progress = MissionModel.RouteProgress(Math.Max(1f, Start.DistanceTo(Destination)), ship.DetWorldCenter.DistanceTo(Destination));
        if (progress < t.LeadAmbushAt) return;
        Ambushed = true;
        Errors.Guard("blind courier ambush", () =>
        {
            Vector2 at = ship.DetWorldCenter;
            int count = EscortGuard.SpawnRaid(sim, at, at.DirectionTo(Destination), t.LeadAmbushDistanceMin, t.LeadAmbushDistanceMax, Guard, Tier, RaiderTag);
            Log.Info($"blind courier ambush: {count} ships at progress {progress:0.00}");
            Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/LeadAmbushFmt", ship.Metadata.XmlShipName));
        });
    }

    static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);

    public override int GetTypeIntegrityHash() => 0x4843_4C44; // "HCLD"
}

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
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;
using HotCargo.Missions;
using HotCargo.Missions.Mechanics;

namespace HotCargo.Worlds;

/// <summary>
/// Rules type "HotCargoClosePass" (spec 2440 to 2445): Hold the Pass in reverse. After the accept a clock runs; the raider convoy comes when
/// a player ship is in the pass ring or the clock runs out. Its freighters fly through the pass center to the exit point while escorts
/// guard them. Every freighter dead completes the step; a freighter at the exit, or a convoy that runs unseen, fails it.
/// Away from the pass system the clock waits, like Hold the Pass.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoClosePass")]
internal sealed class ClosePassObjective : Objective, IMissionMarkers
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoClosePass")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        /// <summary>The ring POI that marks the pass.</summary>
        [Serialize, PortableHash] public SpawnedObjectSearch PassTag = null!;
        [Serialize, PortableHash] public float Radius;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(PassTag, loc, rand, out var pass);
            if (invalid) return null;
            Vector2 center = pass!.Location.Center;
            return new ClosePassObjective(sim.NodeId(), center, Radius)
            {
                PassPoiID = pass.UniqueID,
                Tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(center) + TierOffset),
            };
        }
    }

    [Serialize] public ObjectID NodeID { get; private set; }
    [Serialize] public Vector2 Center { get; private set; }
    [Serialize] public float Radius { get; private set; }
    [Serialize(Optional = true)] public ObjectID? PassPoiID { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; }
    /// <summary>Game seconds of the clock in the pass system before the convoy came.</summary>
    [Serialize(Optional = true)] public double Elapsed { get; private set; }
    /// <summary>The convoy came.</summary>
    [Serialize(Optional = true)] public bool Running { get; private set; }
    [Serialize(Optional = true)] public List<ObjectiveTargetShip> Freighters { get; private set; } = [];
    /// <summary>The convoy's allegiance (freighters and escorts). 0 until it comes.</summary>
    [Serialize(Optional = true)] public int Convoy { get; private set; }
    [Serialize(Optional = true)] public Vector2 Exit { get; private set; }
    /// <summary>The red ring on the exit point.</summary>
    [Serialize(Optional = true)] public ObjectID? ExitRingId { get; private set; }

    IEnumerable<ObjectID> IMissionMarkers.MarkerIds => ExitRingId is { } id ? [id] : [];

    static readonly ID<SimObjectSpawner> FreighterTag = new("hc_closepass_freighter"), EscortTag = new("hc_closepass_escort");

    public override string ScriptTypeName => "HotCargoClosePass";

    public ClosePassObjective(ObjectID nodeId, Vector2 center, float radius)
    {
        if (radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius), radius, "must be above 0");
        NodeID = nodeId;
        Center = center;
        Radius = radius;
    }

    [GenericConstructor]
    ClosePassObjective(GenericSerialReader reader) => reader.ReflectiveRead(this);

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Running.GetIntegrityHash(iHash);
        Convoy.GetIntegrityHash(iHash);
        ((int)Elapsed).GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[NodeID];

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && game.Sim?.MapNode?.UniqueID == NodeID ? new Circle(Center, Radius) : null;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && poi.UniqueID == PassPoiID;

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Freighters.Any(f => f.TargetMetaShip == ship);

    /// <summary>Spec 2444: the lead freighter while the convoy runs, else the pass.</summary>
    ObjectiveTargetShip? Lead => Freighters.FirstOrDefault(f => f.IsAlive);

    public override bool CanFocusDisplayLine(GameRoot game) => Lead is { } lead ? MissionFocus.CanFocus(game, lead) : PassPoiID is not null;

    public override void OnDisplayLineFocused(GameRoot game)
    {
        if (Lead is { } lead) MissionFocus.Focus(game, lead);
        else if (game.Sim is { } sim && sim.MapNode?.UniqueID == NodeID)
        {
            var view = sim.Camera.ViewController;
            view.ClearFocus(clearFramesOfReferenceAndRotation: true);
            view.FRLocation = Center;
        }
    }

    static WorldsTunables T(SimRoot? sim) => Current.Tunables(sim);

    int _shown = int.MinValue;

    /// <summary>Spec 2444: the clock counts down once a second before the convoy; then the freighter count.</summary>
    int Shown(SimRoot? sim) => Running ? -Freighters.Count(f => f.IsAlive) : (int)Math.Ceiling(T(sim).ClosePassDeadlineSeconds - Elapsed);

    public override void RefreshDisplayLines(GameRoot game)
    {
        int now = Shown(game.Sim);
        if (now == _shown) return;
        _shown = now;
        OnDisplayLinesChanged();
    }

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        var text = Running
            ? Strings.KeyStringFmt("HotCargoWorlds/ClosePassRunFmt", Freighters.Count(f => f.IsAlive))
            : Strings.KeyStringFmt("HotCargoWorlds/ClosePassWaitFmt", Clock(T(game.Sim).ClosePassDeadlineSeconds - Elapsed));
        lines.Add(new DisplayLine { Text = text, State = Completion, CanFocus = CanFocusDisplayLine, OnFocused = OnDisplayLineFocused });
    }

    static string Clock(double seconds)
    {
        int s = (int)Math.Max(0, Math.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (PassPoiID is { } poiId && game.Sim?.Doodads[poiId] is POIDoodad poi) poi.IsUndiscovered = false;
        game.EventHub.Tick += OnTick;
    }

    public override void UnregisterEvents(GameRoot game) => game.EventHub.Tick -= OnTick;

    void OnTick(object? sender, SimRoot sim) => Errors.Guard("close pass tick", () => Tick(sim));

    void Tick(SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim || sim.MapNode?.UniqueID != NodeID) return;
        var t = T(sim);
        if (!Running)
        {
            Elapsed += sim.FixedUpdater.Interval.Seconds;
            var players = sim.Ships.HumanPlayerSignificantShips.ToList();
            bool inRing = players.Any(s => s.DetWorldCenter.DistanceTo(Center) - s.DetWorldBoundingCircle.Radius <= Radius);
            if (!inRing && Elapsed < t.ClosePassDeadlineSeconds) return;
            // Spec 2443: the clock ran out with no player ship near enough to see it: the convoy runs through unseen.
            if (!inRing && !players.Any(s => s.DetWorldCenter.DistanceTo(Center) <= sim.Stasis.Rules.StasisLiveRange))
            {
                End(sim, fail: true, "the convoy ran the pass unseen");
                return;
            }
            Errors.Guard("close pass convoy", () => Come(sim, t));
            return;
        }
        var live = Freighters.Where(f => f.TargetShip is not null).Select(f => f.TargetShip!).ToList();
        if (!Freighters.Any(f => f.IsAlive))
        {
            End(sim, fail: false, "every freighter is dead");
            return;
        }
        foreach (var ship in live)
        {
            // Spec 2443: the shared arrival rule (spec 1877) at the exit point.
            if (ShipDelivery.Arrived(sim.RequireGame(), ship.DetWorldBoundingCircle, new Circle(Exit, 0f)))
            {
                Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoWorlds/ClosePassThroughFmt", ship.Metadata.XmlShipName));
                End(sim, fail: true, $"{ship.Metadata.PlainShipName} reached the exit");
                return;
            }
            MoveTo(ship, WorldsModel.PastCenter(N(ship.DetWorldCenter), N(Center), N(Exit)) ? Exit : Center, 100f);
        }
        // The escorts guard the lead freighter.
        if (live.FirstOrDefault() is { } lead)
            for (int i = 0; i < sim.Ships.Count; i++)
                if (sim.Ships[i] is { } e && e.Metadata.PlayerIndex == Convoy && e.HasTag(EscortTag)) Steer(e, lead.DetWorldCenter, 300f);
    }

    /// <summary>Spec 2441, 2442: the convoy jumps in outside the pass. Sim tick only (sim.Rand).</summary>
    void Come(SimRoot sim, WorldsTunables t)
    {
        var mode = (CareerSimModeManager)sim.Mode;
        var (entry, exit) = WorldsModel.PassRun(N(Center), t.ClosePassRunLength);
        Vector2 at = new(entry.X, entry.Y);
        Exit = new Vector2(exit.X, exit.Y);
        Convoy = mode.AllocUniquePlayerIndex();
        Running = true;
        int tier = Math.Max(1, Tier + t.ClosePassTierOffset);
        int freighters = Scaling.EnemyScale.FightCount(t.ClosePassFreighters, sim);
        var tags = t.ClosePassFreighterTags.Select(s => new ID<BuiltinShipTag>(s)).ToArray();
        var game = sim.RequireGame();
        for (int i = 0; i < freighters; i++)
        {
            var hull = WaveSpawns.PickShipNearTier(sim, tags, tier);
            Vector2 spot = WaveSpawns.Location(sim, at, sim.Rand.Direction(), 0f, 300f);
            var spawner = new StasisShipSpawner(sim, hull.ID, spot, SpawnedShipFeatures.None, sim.Rand.UInt64(), [FreighterTag])
            {
                // Spec 2442: the module-less AI of the Ore Tender, so only the objective's move orders fly it: no flee, no surrender, no fight.
                AI = MinerShip.AiId,
                AIParameters = new AIParameters { ChanceToConsiderSurrenderingToPlayer = 0f },
                Direction = spot.DirectionTo(Center),
                ObjectAllegiance = Convoy,
            };
            spawner.Spawn(sim);
            var ship = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"convoy freighter {hull.ID} did not spawn");
            _ = ship.Renderer.AnimateFtlJumpIn(ship.WorldFlightDirection);
            Freighters.Add(new ObjectiveTargetShip(game, ship));
        }
        int escorts = Scaling.EnemyScale.FightCount(t.ClosePassEscorts, sim);
        WaveSpawns.Spawn(sim, at, sim.Rand.Direction(), escorts, tier, t.ClosePassEscortTags, Convoy, EscortTag, 200f, 500f);
        ExitRingId = EscortGuard.WarningRing(sim, null, Exit);
        Log.Info($"close pass: convoy of {freighters} freighters and {escorts} escorts at {at}, exit {Exit}, after {Elapsed:0} s");
        Ux.UxKit.ChatLine(game, Strings.GetText("HotCargoWorlds/ClosePassInbound"));
        Ux.ShipAlerts.Banner(game, Strings.GetText("HotCargoWorlds/ClosePassInbound"), Cosmoteer.Gui.WidgetRules.Instance.BadTextColor);
        OnDisplayLinesChanged();
    }

    void End(SimRoot sim, bool fail, string why)
    {
        Log.Info($"close pass {(fail ? "failed" : "won")}: {why}");
        ExitRingId = EscortGuard.WarningRing(sim, ExitRingId, null);
        if (fail) Fail();
        else Complete();
    }

    /// <summary>A move order to the point, unless the current order already goes within slack of it.</summary>
    static void MoveTo(Ship ship, Vector2 to, float slack)
    {
        if (ship.Commands.CurConfirmedCommand is Cosmoteer.Ships.Commands.MoveCommand { Location: { } at } && at.DistanceTo(to) <= slack) return;
        ship.Commands.EnqueueCommand(new Cosmoteer.Ships.Commands.MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), to, null,
            Cosmoteer.Ships.Commands.CollisionAvoidanceFlags.All, isTentative: false), append: false, confirmed: true);
    }

    /// <summary>Moves the AI origin to a point unless it is already within <paramref name="slack"/> of it, like the escort freighter.</summary>
    static void Steer(Ship ship, Vector2 to, float slack)
    {
        if (ship.AI is not { } ai || ai.Parameters?.PatrolOrigin is { } origin && origin.DistanceTo(to) <= slack) return;
        AIParameters p = ai.Parameters?.Clone() ?? new AIParameters();
        p.PatrolOrigin = to;
        ai.Parameters = p;
    }

    static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);

    public override int GetTypeIntegrityHash() => 0x4843_4350; // "HCCP"
}

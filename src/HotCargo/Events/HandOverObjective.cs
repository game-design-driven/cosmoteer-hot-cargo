using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// An objective whose item leaves the players' ships with the mission (spec 1101, 1144). When the mission leaves the log,
/// <see cref="MissionCargo"/> takes one unit back: the first of these forms that a player ship holds.
/// </summary>
internal interface IMissionCargo
{
    /// <summary>The forms of the unit to take back, or empty when none is due.</summary>
    IReadOnlyList<ID<ResourceRules>> CargoToTakeBack { get; }
}

/// <summary>
/// Rules type "HotCargoHandOver" (spec 1100 to 1103, 1107): an exchange with the Contact ship in its comms. Page is the Hail event
/// (HailTarget = Contact) that the contact shows while this objective is active; its outcome with HandOver = true calls <see cref="Deliver"/>.
/// With a ResourceType, the players carry one unit and hand it over. GiveOnAccept: the accepting player's ship gets it on accept
/// (<see cref="MissionCargo"/>). Otherwise the players first recover it (salvage, a pickup); the step reads "Recover" until the fleet holds it.
/// Once held, the objective fails when no unit is left in the players' ships or loose in the system. Without a ResourceType, LineKey names the exchange.
/// Fails when the contact dies.
/// Limit: the lost check counts units of the resource, not units of this mission. Two open jobs with the same item fail only when every
/// unit is gone. Sundive also gives a Flight Recorder (spec 1144), so a Black Box hand-over can take a Sundive recorder while both
/// run. Revisit if players report it.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoHandOver")]
internal sealed class HandOverObjective : Objective, Missions.IMissionMarkers, IMissionCargo
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoHandOver")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize(Optional = true), PortableHash] public ID<ResourceRules>? ResourceType;
        /// <summary>The accept loads the unit. False: the players must recover it first (spec 1107).</summary>
        [Serialize(Optional = true), PortableHash] public bool GiveOnAccept = true;
        /// <summary>
        /// The ship that holds the unit to recover. The first time it is live, it gets exactly one unit (a rules ResourceLoadout
        /// fills every storage tile, so it cannot give exactly one). Salvage or capture it to get the unit out.
        /// </summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? SourceShipTag;
        /// <summary>A ship that the players deliver to the contact, which takes it (spec 1110). A choice with Need { HandOverReady } waits for it.</summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? ShipTag;
        /// <summary>Hull-to-hull gap between the delivered ship and the contact.</summary>
        [Serialize, PortableHash] public SpawnedObjectSearch ContactShipTag = null!;
        /// <summary>The Hail event (events.rules) that the contact's comms show for this exchange. None for a ship delivery (spec 1877).</summary>
        [Serialize(Optional = true), PortableHash] public string Page = "";
        /// <summary>Objective line, a string key with {0} = the contact. Needed without a ResourceType.</summary>
        [Serialize(Optional = true), PortableHash] public string? LineKey;
        /// <summary>A POI area around the contact. The map marks it until a player ship has the contact on radar, like vanilla GotoShip.</summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? ContactAreaTag;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            if (Page.Length == 0 && ShipTag is null) throw new InvalidDataException("a HotCargoHandOver needs a Page (a Hail event with HailTarget = Contact), or a ShipTag");
            if (ResourceType is null && LineKey is null) throw new InvalidDataException("a HotCargoHandOver without a ResourceType needs a LineKey");
            invalid = !objects.TryFindObject(ContactShipTag, loc, rand, out var contact);
            if (invalid) return null;
            ObjectID id = contact!.UniqueID ?? throw new InvalidOperationException($"contact tag {ContactShipTag} found an object without an ID");
            ObjectiveTargetShip? source = null;
            string? sourceName = null;
            if (SourceShipTag is not null)
            {
                if (ResourceType is null || GiveOnAccept) throw new InvalidDataException("a SourceShipTag needs a ResourceType and GiveOnAccept = false");
                var found = objects.FindObject(SourceShipTag, loc, rand);
                var sourceId = found.UniqueID ?? throw new InvalidOperationException($"source tag {SourceShipTag} found an object without an ID");
                source = new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), sourceId);
                sourceName = sim.Ships[sourceId]?.Metadata.PlainShipName ?? sim.Stasis[sourceId]?.ObjectName
                    ?? throw new InvalidOperationException($"source tag {SourceShipTag} found no ship {sourceId} in this system");
            }
            return new HandOverObjective(ResourceType, new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), id), Page)
            {
                LineKey = LineKey,
                GiveOnAccept = GiveOnAccept,
                Source = source,
                SourceName = sourceName,
                DeliveredShipId = ShipTag is null ? null : objects.FindObject(ShipTag, loc, rand).UniqueID
                    ?? throw new InvalidOperationException($"ship tag {ShipTag} found an object without an ID"),
                AreaDoodadID = ContactAreaTag is null ? null : objects.FindObject(ContactAreaTag, loc, rand).UniqueID,
                Tier = Math.Max(1, ((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).TierFields.GetTier(contact.Location.Center)),
            };
        }
    }

    [Serialize(Optional = true)] public ID<ResourceRules>? ResourceType { get; private set; }
    [Serialize] public ObjectiveTargetShip Contact { get; private set; }
    [Serialize] public string Page { get; private set; }
    [Serialize(Optional = true)] public string? LineKey { get; private set; }
    [Serialize(Optional = true)] public bool GiveOnAccept { get; private set; } = true;
    /// <summary>The fleet held the unit at least once (the accept loaded it, or the players recovered it). The lost check starts here.</summary>
    [Serialize(Optional = true)] public bool Held { get; private set; }
    [Serialize(Optional = true)] public ObjectiveTargetShip? Source { get; private set; }
    /// <summary>The source ship got its one unit.</summary>
    [Serialize(Optional = true)] public bool Seeded { get; private set; }
    /// <summary>Spec 1874: the source ship's name at creation, for the objective line. Null in saves from before.</summary>
    [Serialize(Optional = true)] public string? SourceName { get; private set; }

    /// <summary>Spec 1874: the step reads "Recover": the players must still get the unit out of the source ship.</summary>
    internal bool Recovering => Completion == CompletionState.InProgress && !Held && Source is not null && ResourceType is not null;

    /// <summary>The source ship when it is live in this sim, else null.</summary>
    internal Ship? LiveSource(SimRoot sim) => Source is { } src && sim.NodeId() == src.MapNodeID ? sim.Ships[src.ShipID] : null;

    /// <summary>Spec 1874: loose units of the item in this sim (the source broke and spilled it), lowest ID first.</summary>
    internal IEnumerable<Nugget> LooseUnits(SimRoot sim)
    {
        if (ResourceType is not { } item) return [];
        var forms = Forms(item);
        return sim.Nuggets.Where(n => forms.Contains(n.ResourceType)).OrderBy(n => n.UniqueID);
    }

    /// <summary>
    /// Spec 1874: where the unit to recover is. A loose unit first (the players must pick it up), else the source ship, live or in stasis.
    /// Only while <see cref="Recovering"/>.
    /// </summary>
    internal Missions.FocusTarget RecoverWhere(GameRoot game)
    {
        var src = Source ?? throw new InvalidOperationException("a hand-over without a source has nothing to recover");
        if (game.Sim is not { } sim || sim.NodeId() != src.MapNodeID) return new(src.MapNodeID, null, null, null);
        if (LooseUnits(sim).FirstOrDefault() is { } loose) return new(src.MapNodeID, null, null, loose.DetWorldLocation);
        var live = LiveSource(sim);
        return new(src.MapNodeID, live, null, live?.DetWorldCenter ?? sim.Stasis[src.ShipID]?.Location);
    }
    [Serialize(Optional = true)] public ObjectID? DeliveredShipId { get; private set; }

    /// <summary>
    /// Spec 1880: a goods delivery. The players gather a common resource (ore, iron, processors) and hand it over on the shared page
    /// <see cref="GoodsPage"/>, in one or more trips (a small hold carries less than MinAmount). The job completes once the units handed over
    /// reach MinAmount, at most MaxAmount in all. No lost check and no take-back: the resource is the players' own.
    /// </summary>
    [Serialize(Optional = true)] public bool Goods { get; private set; }
    [Serialize(Optional = true)] public int MinAmount { get; private set; } = 1;
    [Serialize(Optional = true)] public int MaxAmount { get; private set; } = 1;
    /// <summary>Units handed over in earlier trips.</summary>
    [Serialize(Optional = true)] public int Delivered { get; private set; }
    /// <summary>Units that one trip can still hand over.</summary>
    internal int Remaining => MaxAmount - Delivered;
    /// <summary>Spec 1880: a POI where the resource is found (vanilla SuggestedCollectionAreaTag). The map marks it while the fleet holds none.</summary>
    [Serialize(Optional = true)] public ObjectID? CollectionDoodadID { get; private set; }

    /// <summary>The Hail event (events.rules) of every goods delivery.</summary>
    internal const string GoodsPage = "deliver_goods";

    /// <summary>Spec 1880: a goods delivery of MinAmount to MaxAmount units of the resource to the contact ship of this system.</summary>
    /// <param name="contactAt">Where the contact is: at generation it is not in the sim yet, so the caller gives its place for the tier.</param>
    internal static HandOverObjective NewGoods(SimRoot sim, ID<ResourceRules> resource, int minAmount, int maxAmount, ObjectiveTargetShip contact, Vector2 contactAt, ObjectID? collectionPoi)
    {
        if (minAmount < 1 || maxAmount < minAmount) throw new ArgumentOutOfRangeException(nameof(maxAmount), $"goods amount {minAmount} to {maxAmount}: needs 1 <= min <= max");
        return new HandOverObjective(resource, contact, GoodsPage)
        {
            GiveOnAccept = false,
            Goods = true,
            MinAmount = minAmount,
            MaxAmount = maxAmount,
            CollectionDoodadID = collectionPoi,
            Tier = Math.Max(1, ((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).TierFields.GetTier(contactAt)),
        };
    }

    /// <summary>A trip of this many units completes the job: with the earlier trips it reaches MinAmount.</summary>
    internal bool Completes(float units)
    {
        if (units < 1 || units > Remaining) throw new ArgumentOutOfRangeException(nameof(units), units, $"a goods trip takes 1 to {Remaining}");
        return Delivered + units >= MinAmount;
    }

    /// <summary>
    /// Spec 1880: the pay of a goods delivery is linear in the total amount: the job's money reward × total / MaxAmount, so MaxAmount pays the
    /// full reward. Asked for a trip that completes the job; 0 for any other hand-over (no change).
    /// </summary>
    internal float GoodsPayFactor(float units)
    {
        if (!Goods) return 0f;
        if (!Completes(units)) throw new InvalidOperationException($"a trip of {units} does not complete the goods delivery ({Delivered} of {MinAmount})");
        return (Delivered + units) / MaxAmount;
    }

    /// <summary>Spec 1880: what a completing trip of this many units pays: each money reward of the job × <see cref="GoodsPayFactor"/>, rounded like the delivery.</summary>
    internal int GoodsPay(float units) =>
        Mission?.Rewards.OfType<Cosmoteer.Modes.Career.Missions.Rewards.MoneyReward>()
            .Sum(r => Missions.MissionModel.ScaleReward(r.Money, GoodsPayFactor(units), EventModel.HandOverPayRoundTo)) ?? 0;

    /// <summary>Spec 1110, 1877: the delivered ship has arrived at the contact (ShipDelivery), live in this sim.</summary>
    bool ShipArrived(SimRoot sim, Ship ship) =>
        Contact.TargetShip is { } contact && Missions.ShipDelivery.Arrived(sim.RequireGame(), ship.DetWorldBoundingCircle, contact.DetWorldBoundingCircle);

    /// <summary>
    /// Spec 1110: why the delivered ship that arrived still waits (player crew aboard: the contact takes the ship), or null. Sim state only.
    /// </summary>
    internal string? ShipWait(SimRoot sim) =>
        DeliveredShipId is { } id && sim.Ships[id] is { } ship && ShipArrived(sim, ship) && PlayerCrewAboard(sim, ship)
            ? Strings.FormatText("HotCargoEventMissions/DeliverCrewFmt", ship.Metadata.PlainShipName)
            : null;

    /// <summary>Human crew whose home is the ship, or who stand on it now.</summary>
    static bool PlayerCrewAboard(SimRoot sim, Ship ship) =>
        (sim.Mode.IsHumanPlayerShip(ship) && ship.Crew.Assigned.Count > 0)
        || Tech.Vaults.HumanShips(sim).SelectMany(s => s.Crew.Assigned).Any(c => c.CurrentShip == ship);

    /// <summary>A surrendered delivered ship stays in the system and flies to the contact (spec 1110). Sim tick.</summary>
    internal void SteerSurrendered(SimRoot sim)
    {
        if (DeliveredShipId is not { } id || sim.Ships[id] is not { } ship || Contact.DetLocation() is not { } at) return;
        if (!((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).SurrenderedAIs.Contains(id)) return;
        if (ship.AI is not { Parameters: { } parameters } ai || parameters.PatrolOrigin is { } origin && origin.DistanceTo(at) < 100f) return;
        var p = parameters.Clone();
        p.PatrolOrigin = at;
        ai.Parameters = p;
        Log.Info($"hand-over: surrendered {ship.Metadata.PlainShipName} flies to its contact");
    }

    /// <summary>True for the delivered ship of any open hand-over: vanilla must not fly it out of the system (spec 1110).</summary>
    internal static bool IsHeld(Run run, ObjectID shipId)
    {
        var found = new List<HandOverObjective>();
        foreach (Mission m in run.Mode.Missions) m.GetObjectives(found);
        return found.Any(o => o.Completion == CompletionState.InProgress && o.DeliveredShipId == shipId);
    }

    /// <summary>Puts the one unit into the source ship once it is live. Sim tick, for every open hand-over (its turn or not).</summary>
    internal void SeedSource(SimRoot sim)
    {
        // By ID in this sim: the source is junk, and an objective that is not running yet does not track its target.
        if (Seeded || Source is null || sim.NodeId() != Source.MapNodeID || sim.Ships[Source.ShipID] is not { } ship || ResourceType is not { } item) return;
        if (ship.Resources.AddResources(item, 1, excludeConstructionSinks: true) != 1) throw new InvalidOperationException($"{ship.Metadata.PlainShipName} has no storage for {item}");
        Seeded = true;
        Log.Info($"hand-over: {item} placed in {ship.Metadata.PlainShipName}");
    }
    [Serialize(Optional = true)] public int Tier { get; private set; } = 1;
    [Serialize(Optional = true)] public ObjectID? AreaDoodadID { get; private set; }

    public override string ScriptTypeName => "HotCargoHandOver";

    public HandOverObjective(ID<ResourceRules>? resourceType, ObjectiveTargetShip contact, string page)
    {
        ResourceType = resourceType;
        Contact = contact ?? throw new ArgumentNullException(nameof(contact));
        // Empty for a ship delivery: it completes on arrival, with no comms step (spec 1877).
        Page = page ?? throw new ArgumentNullException(nameof(page));
    }

    [GenericConstructor]
    HandOverObjective(GenericSerialReader reader)
    {
        Contact = null!;
        Page = "";
        reader.ReflectiveRead(this);
    }

    /// <summary>The active hand-over whose contact is this ship, or null.</summary>
    public static HandOverObjective? ForContact(Run run, ObjectID shipId) => Open(run).FirstOrDefault(o => o.Contact.ShipID == shipId && o.Page.Length > 0);

    public static IEnumerable<HandOverObjective> Open(Run run)
    {
        var found = new List<HandOverObjective>();
        foreach (Mission m in run.Mode.Missions) m.GetObjectives(found);
        // Its turn: an exchange after other steps (kill the escorts first) shows its page only when the sequence reaches it.
        return found.Where(o => o.Completion == CompletionState.InProgress && o._registered);
    }

    /// <summary>The page outcome of a hand-over (HandOver = true) on this contact. The page cost already took the unit. Sim logic.</summary>
    /// <paramref name="payFactor"/> above 0 scales the job's money reward (spec 1118: the contact notices and pays less).
    public static void Deliver(Run run, ObjectID? contact, float payFactor = 0f, float units = 0f)
    {
        var o = Require(run, contact);
        // Spec 1880: a goods trip that does not reach MinAmount counts and keeps the job open; the completing trip pays for the total.
        if (o.Goods)
        {
            if (!o.Completes(units))
            {
                o.Delivered += (int)units;
                Log.Info($"hand-over: goods trip of {units}, {o.Delivered} of {o.MinAmount} delivered");
                o.OnDisplayLinesChanged();
                return;
            }
            payFactor = o.GoodsPayFactor(units);
            o.Delivered += (int)units;
        }
        Log.Info($"hand-over: {o.Page} done with {o.Contact.GetPlainTargetName(run.Game.Sim)}");
        if (payFactor > 0f && o.Mission is { } mission)
            foreach (var reward in mission.Rewards.OfType<Cosmoteer.Modes.Career.Missions.Rewards.MoneyReward>())
            {
                int before = reward.Money;
                reward.Money = Missions.MissionModel.ScaleReward(reward.Money, payFactor, EventModel.HandOverPayRoundTo);
                Log.Info($"hand-over: pay {before} x {payFactor} = {reward.Money}");
            }
        o.Complete();
    }

    static HandOverObjective Require(Run run, ObjectID? contact) =>
        contact is { } id && ForContact(run, id) is { } o ? o
            : throw new InvalidOperationException($"a hand-over outcome ran on ship {contact?.ToString() ?? "none"}, which is no open hand-over contact");

    /// <summary>
    /// Spec 1118: the contact took the item and ends the deal. The job fails. Hostile: the contact gets its own hostile allegiance and
    /// hunts the players. Leaves: it jumps out of the system. Sim logic.
    /// </summary>
    public static void React(Run run, SimRoot sim, ObjectID? contact, ContactReaction reaction)
    {
        var o = Require(run, contact);
        var ship = sim.Ships[o.Contact.ShipID] ?? throw new InvalidOperationException("the contact is not live");
        Log.Info($"hand-over: {ship.Metadata.PlainShipName} reacts with {reaction}; the job fails");
        o.FailAfterHandOver();
        switch (reaction)
        {
            case ContactReaction.Hostile:
                var career = (Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode;
                ship.Metadata.PlayerIndex = career.AllocUniquePlayerIndex();
                if (ship.AI is { Parameters: { } parameters } ai)
                {
                    // The courier contact keeps to its spot (acquire range 200); a hostile contact hunts across the area.
                    var p = parameters.Clone();
                    p.PatrolOrigin = ship.DetWorldCenter;
                    p.TargetAcquireRangeFromOrigin = 4000f;
                    p.TargetUnacquireRangeFromOrigin = 6000f;
                    ai.Parameters = p;
                }
                break;
            case ContactReaction.Leaves:
                // The diplomacy area's leaving list plays the FTL jump-out and removes the ship (spec 195).
                run.State.Diplomacy.Leaving.Add(new Diplomacy.Leaving { Ship = ship.UniqueID, Due = run.State.GameSeconds + 2 });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reaction), reaction, "no reaction");
        }
    }

    /// <summary>
    /// The item and the items that its actions turn it into (spec 1117). An Opened Case is still the job's case: it counts as held, and
    /// the hand-over page takes it.
    /// </summary>
    public static ID<ResourceRules>[] Forms(ID<ResourceRules> item) =>
        [item, .. ItemActions.For(item).Where(a => a.Becomes is not null).Select(a => a.Becomes!.Value).Distinct()];

    public static int FleetUnits(SimRoot sim, ID<ResourceRules>[] forms) => forms.Sum(f => FleetUnits(sim, f));
    public static int UnitsLeft(SimRoot sim, ID<ResourceRules>[] forms) => forms.Sum(f => UnitsLeft(sim, f));

    /// <summary>False while no human ship is in this system, for example during a jump. Item counts then read 0 without a loss.</summary>
    public static bool FleetHere(SimRoot sim) => Tech.Vaults.HumanShips(sim).Any();

    /// <summary>Units that the players hold: stored in their ships or carried by their crew (<see cref="FleetHold"/>, spec 1875).</summary>
    public static int FleetUnits(SimRoot sim, ID<ResourceRules> resource) => FleetHold.Units(sim, resource);

    /// <summary>
    /// Units that the players hold, loose in this system, or in the storage of a derelict (junk) ship: a player ship without control or a
    /// piece of one keeps its storage, and the crew can salvage it back (spec 1103).
    /// </summary>
    public static int UnitsLeft(SimRoot sim, ID<ResourceRules> resource) =>
        FleetUnits(sim, resource) + sim.Nuggets.Where(n => n.ResourceType == resource).Sum(n => n.Quantity)
        + sim.Ships.AllShips.Where(s => s.IsJunk).Sum(s => s.Resources.GetResourceTotal(resource, includeStored: true, includeCarried: true));

    internal void MarkHeld() => Held = true;

    /// <summary>A job that leaves the log without its hand-over takes its held unit back.</summary>
    public IReadOnlyList<ID<ResourceRules>> CargoToTakeBack =>
        !Goods && Completion != CompletionState.Completed && Held && ResourceType is { } item ? Forms(item) : [];

    /// <summary>The contact took the item and still ended the deal (spec 1118). The players hold nothing for this job any more.</summary>
    internal void FailAfterHandOver()
    {
        Held = false;
        Fail();
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        ResourceType?.GetIntegrityHash(iHash);
        Contact.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Recovering ? Source!.MapNode : Contact.MapNode;

    /// <summary>The contact when it is on radar, else its area POI once explored. Same rule as vanilla GotoShipObjective.GetAreaLocation.</summary>
    public override Circle? GetAreaLocation(GameRoot game)
    {
        if (Completion != CompletionState.InProgress) return null;
        // Spec 1874: the unit to recover. Radius 0 for a point, as vanilla MetaShip gives a ship in stasis.
        if (Recovering)
            return RecoverWhere(game) switch
            {
                { Live: { } source } => source.DetWorldBoundingCircle,
                { Location: { } at } => new Circle(at, 0f),
                _ => null,
            };
        if (Contact.TargetShip is { IsWithinRadarOfLocalPlayer: true } ship) return ship.WorldBoundingCircle;
        return Area(game) is { IsExplored: true } poi ? new Circle(poi.WorldLocation, poi.WorldUniformScale) : null;
    }

    /// <summary>Spec 996: the contact's area marker goes with the mission.</summary>
    public IEnumerable<ObjectID> MarkerIds => new[] { AreaDoodadID, CollectionDoodadID }.OfType<ObjectID>();

    POIDoodad? Area(GameRoot game) => AreaDoodadID is { } id ? game.Sim?.Doodads[id] as POIDoodad : null;

    public override bool IsPOIAssociated(POIDoodad poi) =>
        Completion == CompletionState.InProgress
        && (poi.UniqueID == AreaDoodadID && Contact.TargetShip is not { IsWithinRadarOfLocalPlayer: true } || poi.UniqueID == CollectionDoodadID && !Held);

    public override bool IsShipAssociated(MetaShip ship) =>
        Recovering ? ship.UniqueID == Source!.ShipID : Completion == CompletionState.InProgress && Contact.TargetMetaShip == ship;

    /// <summary>
    /// Only a running step expires; an offer stays even when its contact died, and fails at once when accepted (RegisterEvents).
    /// Limit: an offer that expired would keep its map marker, because marker cleanup (MissionMarkers) runs when a mission leaves the
    /// log, not when an offer expires. Revisit when MissionMarkers also cleans up expired offers.
    /// </summary>
    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || _registered && game.Sim is { } sim && ContactGone(sim);

    /// <summary>Gone: not in this system's ships or stasis. By ID, because ObjectiveTargetShip tracks nothing before the step runs.</summary>
    bool ContactGone(SimRoot sim) =>
        sim.NodeId() == Contact.MapNodeID && sim.Ships[Contact.ShipID] is null && !sim.Stasis.Any(st => st.ReservedObjectID == Contact.ShipID);

    /// <summary>Spec 1873, 1874: the click shows the unit to recover while the step reads "Recover", else the contact.</summary>
    public override bool CanFocusDisplayLine(GameRoot game) =>
        Recovering ? Missions.MissionFocus.CanFocus(game, RecoverWhere(game)) : Missions.MissionFocus.CanFocus(game, Contact);

    public override void OnDisplayLineFocused(GameRoot game)
    {
        if (Recovering) Missions.MissionFocus.Focus(game, RecoverWhere(game));
        else Missions.MissionFocus.Focus(game, Contact);
    }

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        string contact = Contact.GetXmlTargetName(game.Sim);
        var text = ResourceType?.GetResourceRules() is { } r
            ? Goods ? Strings.KeyStringFmt(Delivered > 0 ? "HotCargoEventMissions/GoodsPartFmt" : "HotCargoEventMissions/GoodsFmt",
                MinAmount == MaxAmount ? $"{MinAmount}" : $"{MinAmount} to {MaxAmount}", r.TextIconXml, r.NameKey, contact, Delivered)
            : Held ? Strings.KeyStringFmt("HotCargoEventMissions/HandOverFmt", r.TextIconXml, r.NameKey, contact)
                : SourceName is { } from
                    ? Strings.KeyStringFmt("HotCargoEventMissions/RecoverFromFmt", r.TextIconXml, r.NameKey, Halfling.StringTools.EscapeForXml(from))
                    : Strings.KeyStringFmt("HotCargoEventMissions/RecoverFmt", r.TextIconXml, r.NameKey)
            : Strings.KeyStringFmt(LineKey ?? throw new InvalidOperationException("a hand-over without an item has no LineKey"), contact);
        lines.Add(new DisplayLine
        {
            Text = text,
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });
        if (Completion == CompletionState.InProgress && _lineBlock is { } why)
            lines.Add(new DisplayLine { Text = new Halfling.Gui.Components.Text.SimpleTextProvider(Halfling.StringTools.EscapeForXml(why), null), State = CompletionState.InProgress });
    }

    /// <summary>Why the contact's comms do not show this exchange's line, as of the last tick, or null (spec 1875).</summary>
    string? _lineBlock;

    /// <summary>
    /// Spec 1875, 1876: why the contact's comms do not show this exchange's line now, or null when they show it (or the step still reads
    /// "Recover"). The line shows over a waiting page (it parks that page), but only one page parks at a time: while another hand-over
    /// page is open with a page parked behind it, the line waits. A page in the contact's own comms shows there, so it is no block.
    /// Sim state only.
    /// </summary>
    internal string? LineBlock(Run run, SimRoot sim)
    {
        if (ResourceType is not null && !Held) return null;
        // Spec 1876: the line shows over a waiting page; it hides only while another hand-over page is open with a page parked behind it.
        if (run.State.Events.Parked is null || run.State.Events.Pending is not { } p || p.Carrier == Contact.ShipID) return null;
        string title = EventLibrary.Get(p.EventId).Title;
        string? carrier = p.Carrier is { } c ? sim.Ships[c]?.Metadata.PlainShipName ?? sim.Stasis[c]?.ObjectName : null;
        return carrier is null
            ? Strings.FormatText("HotCargoEventMissions/HandOverWaitFmt", title)
            : Strings.FormatText("HotCargoEventMissions/HandOverWaitAtFmt", title, carrier);
    }

    /// <summary>
    /// True while the mission runs this step. A Sequential parent registers a step only when it reaches it, on every machine,
    /// so this is the same everywhere. Vanilla IsActive does not follow the sequence.
    /// </summary>
    bool _registered;

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        _registered = true;
        Log.Info($"hand-over: step {Page} starts (loading: {isLoading})");
        Contact.Killed += OnContactKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && game.Sim is { } sim && ContactGone(sim)) OnContactKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        _registered = false;
        Contact.Killed -= OnContactKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnContactKilled(object? sender, EventArgs e)
    {
        if (Completion == CompletionState.InProgress) Fail();
    }

    void OnTick(object? sender, SimRoot sim) => Errors.Guard("hand-over tick", () =>
    {
        if (Mission?.Game is not { } game || Completion != CompletionState.InProgress || sim != game.Sim) return;
        if (DeliveredShipId is { } shipId && sim.NodeId() == Contact.MapNodeID && sim.Ships[shipId] is null && !sim.Stasis.Any(st => st.ReservedObjectID == shipId))
        {
            Log.Info("hand-over: the ship to deliver is gone; the job fails");
            Fail();
            return;
        }
        // Spec 1110, 1877: a ship delivery completes on arrival once no player crew is aboard; the contact takes the ship.
        if (DeliveredShipId is { } deliveredId && sim.Ships[deliveredId] is { } delivered && ShipArrived(sim, delivered) && !PlayerCrewAboard(sim, delivered))
        {
            Log.Info($"hand-over: {Contact.GetPlainTargetName(sim)} takes {delivered.Metadata.PlainShipName}");
            sim.Ships.Remove(delivered, dispose: true, handleLostShip: false);
            Complete();
            return;
        }
        if (Run.Current is { } run && run.Game == game && (ShipWait(sim) ?? LineBlock(run, sim)) is var block && block != _lineBlock)
        {
            _lineBlock = block;
            Log.Info($"hand-over: {Page} line {(block is null ? "shows" : $"blocked: {block}")}");
            OnDisplayLinesChanged();
        }
        if (ResourceType is not { } item) return;
        // During a jump the ships are in limbo: no human ship is in any system, so the fleet holds 0 units. Not a loss (spec 1103).
        if (!FleetHere(sim)) return;
        // Spec 1880: goods have no lost check. Held follows the fleet: it holds some of the resource or not.
        if (Goods)
        {
            bool enough = FleetUnits(sim, Forms(item)) >= 1;
            if (enough == Held) return;
            Held = enough;
            OnDisplayLinesChanged();
            return;
        }
        if (!Held)
        {
            if (FleetUnits(sim, Forms(item)) == 0)
            {
                // The source is gone (destroyed, or salvaged with the unit destroyed) and no unit drifts loose: it is lost.
                bool sourceGone = Source is { } src && sim.NodeId() == src.MapNodeID && sim.Ships[src.ShipID] is null && !sim.Stasis.Any(st => st.ReservedObjectID == src.ShipID);
                if (!sourceGone || UnitsLeft(sim, Forms(item)) > 0) return;
                Log.Info($"hand-over: the source of {item} is gone and no unit is loose; the job fails");
                string lost = item.GetResourceRules().NameKey.Text;
                Fail();
                Ux.UxKit.CenterText(game, Strings.FormatText("HotCargoEventMissions/HandOverLostFmt", lost));
                return;
            }
            Held = true;
            Log.Info($"hand-over: the fleet recovered {item}");
            OnDisplayLinesChanged();
            return;
        }
        if (UnitsLeft(sim, Forms(item)) > 0) return;
        Log.Info($"hand-over: no {item} left in the fleet or the system; the job fails");
        // Fail() can take the mission out of the log at once, so read everything first.
        string name = item.GetResourceRules().NameKey.Text;
        Fail();
        // UI only: tell the players why.
        Ux.UxKit.CenterText(game, Strings.FormatText("HotCargoEventMissions/HandOverLostFmt", name));
    });

    public override int GetTypeIntegrityHash() => 0x4843_484F; // "HCHO"
}

/// <summary>
/// Items that a mission gives on accept (spec 1101): one unit of each hand-over's resource goes to the accepting player's ship with room.
/// The contracts accept gate refuses the accept when no ship has room. A job that leaves the log without the hand-over takes its unit back.
/// </summary>
public static class MissionCargo
{
    static List<HandOverObjective> Given(Mission m)
    {
        var found = new List<HandOverObjective>();
        m.GetObjectives(found);
        return found.Where(o => o.ResourceType is not null && o.GiveOnAccept).ToList();
    }

    static IEnumerable<ID<ResourceRules>> Items(Mission m) => Given(m).Select(o => o.ResourceType!.Value);

    public static bool HasItems(Mission m) => Items(m).Any();

    /// <summary>The items of every hand-over of the mission, given on accept or not. Scavengers leave them alone (spec 1330).</summary>
    public static IEnumerable<ID<ResourceRules>> ItemTypes(Mission m)
    {
        var found = new List<HandOverObjective>();
        m.GetObjectives(found);
        return found.Where(o => o.ResourceType is not null).Select(o => o.ResourceType!.Value);
    }

    /// <summary>The first ship of this player, by ID, that can take one unit. Deterministic order.</summary>
    static Ship? ShipWithRoom(SimRoot sim, int playerIndex, ID<ResourceRules> resource) =>
        Tech.Vaults.HumanShips(sim).Where(s => s.Metadata.PlayerIndex == playerIndex)
            .OrderBy(s => s.UniqueID)
            .FirstOrDefault(s => s.Resources.GetBestReceivingSink(resource, 1, MPValueType.Confirmed, out _, includeAnticipatedDeliveries: false, externalDelivery: false,
                excludeDeconstructionQueued: false, ignoreAllowManualReceiveToggles: true, excludeConstructionSinks: true) is not null);

    /// <summary>The first item that no ship of this player has room for, or null when all fit.</summary>
    public static ID<ResourceRules>? NoRoomFor(SimRoot sim, Mission m, int playerIndex) =>
        Items(m).Select(r => (ID<ResourceRules>?)r).FirstOrDefault(r => ShipWithRoom(sim, playerIndex, r!.Value) is null);

    /// <summary>Runs inside the accept input after the mission reached the log. Sim logic.</summary>
    public static void Load(SimRoot sim, Mission m, int playerIndex)
    {
        foreach (var r in Items(m))
        {
            var ship = ShipWithRoom(sim, playerIndex, r) ?? throw new InvalidOperationException($"no room for {r} after the accept check");
            if (ship.Resources.AddResources(r, 1, excludeConstructionSinks: true) != 1) throw new InvalidOperationException($"{ship.Metadata.PlainShipName} did not take {r}");
            Log.Info($"mission cargo: {r} loaded into {ship.Metadata.PlainShipName} for {m.MetatypeID} {m.MissionID}");
        }
        foreach (var o in Given(m)) o.MarkHeld();
        Watch(m.Game);
    }

    /// <summary>Sim tick (Run.Hooks): watches the mission log and fills source ships of open hand-overs.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("mission cargo tick", () =>
    {
        if (sim != run.Game.Sim) return;
        Watch(run.Game);
        var found = new List<HandOverObjective>();
        foreach (Mission m in run.Mode.Missions) m.GetObjectives(found);
        foreach (var o in found.Where(o => o.Completion == CompletionState.InProgress))
        {
            if (o.Source is not null && !o.Seeded) o.SeedSource(sim);
            if (o.Recovering) RecoverMarkers.Mark(sim, o);
            o.SteerSurrendered(sim);
        }
    });

    static MissionList? _watched;

    /// <summary>Subscribes to the mission log of the current game. A new or loaded game has a new log.</summary>
    internal static void Watch(GameRoot game)
    {
        if (Run.Current is not { } run || run.Game != game || _watched == run.Mode.Missions) return;
        if (_watched is not null) _watched.MissionRemoved -= OnMissionRemoved;
        _watched = run.Mode.Missions;
        _watched.MissionRemoved += OnMissionRemoved;
    }

    /// <summary>A job without its hand-over (failed or abandoned) takes its unit back, so dead cargo does not block a storage slot.</summary>
    static void OnMissionRemoved(object? sender, Mission m) => Errors.Guard("mission cargo", () =>
    {
        if (Run.Current is not { } run || !ReferenceEquals(sender, run.Mode.Missions) || run.Game.Sim is not { } sim) return;
        var found = new List<Objective>();
        m.GetObjectives(found, o => o is IMissionCargo);
        foreach (var forms in found.Cast<IMissionCargo>().Select(o => o.CargoToTakeBack).Where(f => f.Count > 0))
        {
            // The first form that a ship holds: sealed, or opened (spec 1117).
            int removed = forms.Select(f => FleetHold.Take(sim, f, 1)).FirstOrDefault(n => n > 0);
            Log.Info($"mission cargo: {m.MetatypeID} {m.MissionID} left the log; took back {removed} {forms[0]}");
        }
    });
}

/// <summary>Spec 1110: a surrendered ship that a running hand-over must deliver does not leave the system until the mission ends.</summary>
[HarmonyLib.HarmonyPatch(typeof(Cosmoteer.Ships.AI.StrategyModules.AIExitSectorModule), nameof(Cosmoteer.Ships.AI.StrategyModules.AIExitSectorModule.OnAITick))]
static class HeldShipExitPatch
{
    static bool Prefix(Cosmoteer.Ships.AI.StrategyModules.AIExitSectorModule __instance, Ship ship, ref bool __result)
    {
        // Cheap check first: this runs on every AI tick of every ship with the module.
        if (!__instance.IsActivated) return true;
        bool held = false;
        Errors.Guard("held ship exit", () => held = Run.Current is { } run && ship.Sim == run.Game.Sim && HandOverObjective.IsHeld(run, ship.UniqueID));
        if (!held) return true;
        __result = false;
        return false;
    }
}

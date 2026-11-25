using System.Collections.Concurrent;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Modes;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HotCargo.Hunters;
using HotCargo.ShipAi;

namespace HotCargo.Autonomy;

/// <summary>
/// Build ship and refit to design (spec 1084 to 1092): the design data of the slots, its cost, and the vanilla blueprint steps. Every
/// result comes from the design data and sim state, so all co-op machines agree.
/// </summary>
internal static class Build
{
    /// <summary>
    /// What one design is: its ship rules, blueprint parts and doors, the resources that construction takes for it (per resource ID,
    /// in ID order), the tile rectangle center and radius, and a stable hash of the data.
    /// </summary>
    internal sealed record Info(ID<ShipRules> Rules, HashSet<PartInfo> Parts, HashSet<DoorInfo> Doors, SortedDictionary<string, int> Cost,
        Vector2 Center, float Radius, int Hash);

    /// <summary>Designs by data. The info is a pure function of the data, so the cache holds no sim state.</summary>
    static readonly ConcurrentDictionary<string, Info> Infos = new();

    /// <summary>Loads the design data as a blueprint-only ship. Throws FormatException when the data is not a design of the game.</summary>
    static Ship Load(BuildDesign d, int playerIndex = 0)
    {
        try { return Ship.Load(Convert.FromBase64String(d.Data), d.Name, playerIndex, blueprintsOnly: true); }
        catch (Exception ex) when (ex is not FormatException) { throw new FormatException($"design '{d.Name}' does not load: {ex.Message}", ex); }
    }

    /// <summary>Spec 1085, 1086. Throws FormatException for data that does not load or a design without parts.</summary>
    internal static Info InfoOf(BuildDesign d) => Infos.GetOrAdd(d.Data, _ =>
    {
        using var proto = Load(d);
        if (proto.BlueprintParts.Count == 0) throw new FormatException($"design '{d.Name}' has no parts");
        var parts = proto.BlueprintParts.Select(p => new PartInfo(p)).ToHashSet();
        var doors = proto.BlueprintDoors.Select(door => new DoorInfo(door)).ToHashSet();
        var rects = proto.BlueprintParts.Select(p => p.Rect).ToList();
        int left = rects.Min(r => r.Left), top = rects.Min(r => r.Top), right = rects.Max(r => r.Right), bottom = rects.Max(r => r.Bottom);
        var center = new Vector2((left + right) / 2f, (top + bottom) / 2f);
        float radius = new Vector2(right - left, bottom - top).Length / 2f * proto.Rules.ShipScale;
        return new Info(proto.Rules.ID, parts, doors, CostOf(parts, doors), center, radius, ShipAiModel.StableHash(d.Data));
    });

    /// <summary>Spec 1086: per resource, the parts' ResourcesMinusUnderlying and the doors' Resources: what construction takes.</summary>
    static SortedDictionary<string, int> CostOf(IEnumerable<PartInfo> parts, IEnumerable<DoorInfo> doors)
    {
        var cost = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(ID<ResourceRules> type, int n) => cost[type.ToString()] = cost.GetValueOrDefault(type.ToString()) + n;
        foreach (var p in parts)
            foreach (var (type, n) in p.Rules.ResourcesMinusUnderlying) Add(type, n);
        foreach (var door in doors)
            foreach (var (type, n) in door.Rules.Resources) Add(type, n);
        return cost;
    }

    /// <summary>Spec 1090: the cost of the design parts and doors that the ship does not have built now.</summary>
    internal static SortedDictionary<string, int> RefitCost(Ship ship, Info info)
    {
        var built = ship.Parts.Select(p => new PartInfo(p)).ToHashSet();
        var doors = ship.Doors.Select(door => new DoorInfo(door)).ToHashSet();
        return CostOf(info.Parts.Where(p => !built.Contains(p)), info.Doors.Where(door => !doors.Contains(door)));
    }

    /// <summary>Spec 1086: the resources that vanilla construction near the ship can use cover the cost.</summary>
    internal static bool Affords(Ship ship, IEnumerable<KeyValuePair<string, int>> cost) =>
        BuildModel.Affords(cost, type => ship.Resources.GetUsableConstructionResourceTotal(new ID<ResourceRules>(type), includeAnticipated: true));

    /// <summary>Spec 1086: the owner's live ships in the ship's system that a build started from the design.</summary>
    internal static int Built(Ship ship, Info info) =>
        HunterOrders.Sim(ship).Ships.Count(s => !s.IsJunk && s.Metadata.PlayerIndex == ship.Metadata.PlayerIndex && ShipTactics.Of(s)?.BuiltFrom == info.Hash);

    /// <summary>Spec 1087, 1090: the AI builds only in crew construction mode, the mode in which construction takes the resources.</summary>
    internal static bool CrewMode(Ship ship) => HunterOrders.Sim(ship).Mode.GetConstructionMode(ship) == ConstructionMode.CrewResources;

    /// <summary>Spec 1088: the ship that the last build of this ship started still lives and its construction is not done.</summary>
    internal static bool Busy(Ship ship, ShipTactics t) =>
        t.Building is { } id && HunterOrders.Sim(ship).Ships[id] is { IsJunk: false } started && !started.PhysicalMatchesBlueprints;

    /// <summary>Spec 1090: the ship's blueprint parts and doors are the design's.</summary>
    internal static bool BlueprintIs(Ship ship, Info info) =>
        info.Parts.SetEquals(ship.BlueprintParts.Select(p => new PartInfo(p))) && info.Doors.SetEquals(ship.BlueprintDoors.Select(door => new DoorInfo(door)));

    /// <summary>
    /// Spec 1087: the first of 8 directions around the ship, starting toward the owner's nearest crewed ship (whose crew build it), else
    /// from +X, where the new ship's circle is clear of every ship, vanilla allows a new ship (AllowStartNewShip) and crew can reach it
    /// (CrewReach). Null when none works, so the row does not run and leaves no blueprint that nobody builds.
    /// </summary>
    internal static Vector2? Spot(Ship ship, Info info, float gap)
    {
        var sim = HunterOrders.Sim(ship);
        float distance = ship.WorldBoundingRadius + info.Radius + gap;
        var crewed = sim.Ships.HumanPlayerShips.Where(s => s != ship && s.Metadata.PlayerIndex == ship.Metadata.PlayerIndex && Autonomous.IsCrewed(s))
            .OrderBy(s => HunterOrders.EdgeDistance(ship, s)).ThenBy(s => s.UniqueID).FirstOrDefault();
        float start = crewed is null ? 0f : (float)ship.DetWorldCenter.DirectionTo(crewed.DetWorldCenter);
        for (int i = 0; i < 8; i++)
        {
            var at = ship.DetWorldCenter + Vector2.FromAngle(start + i * MathF.PI / 4f) * distance;
            if (sim.Ships.DetHasIntersectingWith(new Circle(at, info.Radius + gap / 2f))) continue;
            if (sim.Mode.AllowStartNewShip(ship.Metadata.PlayerIndex, at, out _) && CrewReach(ship, at, info.Radius)) return at;
        }
        return null;
    }

    /// <summary>
    /// Spec 1087: an own crewed ship whose crew can build a new ship at the spot: vanilla's construction helper rule
    /// (Job.GetNearbyConstructionHelperShips): within the crew's lose-home warning distance of the new ship, construction assist allows
    /// it (CanShipHelpWithConstruction, asked with the builder, a ship of the same owner) and the ship is slow enough for crew to leave.
    /// </summary>
    static bool CrewReach(Ship builder, Vector2 at, float radius)
    {
        var sim = HunterOrders.Sim(builder);
        var reach = new Circle(at, radius + GameApp.Rules.Crew.LoseHomeShipWarningDistance);
        return sim.Ships.HumanPlayerShips.Any(s => s.Metadata.PlayerIndex == builder.Metadata.PlayerIndex && Autonomous.IsCrewed(s)
            && s.DetWorldBoundingCircle.IntersectsWith(reach) && sim.Mode.CanShipHelpWithConstruction(s, builder) && s.AllowAutoCrewExit);
    }

    /// <summary>
    /// Spec 1087: a new empty ship of the owner at the spot, as MPNewShipInput makes it, then the design's blueprints queued for crew
    /// construction, as build mode "Load ship (replace)" does. The new ship gets the design's tactics. Returns the new ship.
    /// </summary>
    internal static Ship Start(Ship builder, BuildDesign d, Vector2 spot)
    {
        var info = InfoOf(d);
        var rules = GameApp.Rules.GetShipRules(info.Rules);
        var ship = new Ship(rules, d.Name, builder.Metadata.PlayerIndex);
        ship.Location = spot - info.Center * rules.ShipScale;
        HunterOrders.Sim(builder).Ships.Add(ship, giveDefaultCommand: true);
        using var proto = Load(d, builder.Metadata.PlayerIndex);
        SetBlueprints(ship, proto);
        var t = ShipTactics.For(ship);
        if (ShipTactics.Of(proto) is { } design)
        {
            t.SetRows(design.Text);
            t.SetCargo(design.CargoText);
            t.Auto = design.Auto;
        }
        t.BuiltFrom = info.Hash;
        Log.Info($"build: {builder.Metadata.PlainShipName} started '{d.Name}' id={ship.UniqueID} parts={info.Parts.Count} cost={string.Join(",", info.Cost.Select(c => $"{c.Key}={c.Value}"))}");
        return ship;
    }

    /// <summary>Spec 1090: the ship's blueprints become the design's, queued for crew construction. The ship keeps its tactics.</summary>
    internal static void Refit(Ship ship, BuildDesign d)
    {
        using var proto = Load(d, ship.Metadata.PlayerIndex);
        SetBlueprints(ship, proto);
        Log.Info($"build: {ship.Metadata.PlainShipName} refits to '{d.Name}'");
    }

    /// <summary>
    /// The two player steps in build mode. "Load ship (replace)" (BuildToolbox.OnLoadShipReplaceClicked): clear the blueprints, add the
    /// design's parts and doors as ShipUpdateInfo.CopyBlueprintsFromShip does, apply them. Not CopyBlueprintsFromShip itself: its
    /// tactics hook (requirement 407) sends an MP input, and this runs in the sim on every machine. Then "Make it so"
    /// (BuildToolbox.OnConstructClicked, MPUpdateShipInput in crew construction): construct with its flags and no crew to hire, queue
    /// the construction, clear the destroyed parts, apply the blueprint part settings. Unlike a player, the AI buys no resources.
    /// </summary>
    static void SetBlueprints(Ship ship, Ship proto)
    {
        using (var load = ShipUpdateInfo.Alloc(ship))
        {
            load.ClearBlueprints();
            foreach (var p in proto.BlueprintParts) load.BlueprintPartsToAdd.Add(new PartInfo(p.Rules, p.Location, p.Rotation, p.FlipX));
            foreach (var door in proto.BlueprintDoors) load.BlueprintDoorsToAdd.Add(new DoorInfo(door.Rules, door.Location));
            load.ApplyBlueprints();
        }
        using var construct = ShipUpdateInfo.Alloc(ship);
        if (!construct.TryConstruct(ConstructionFlags.RepairExisting | ConstructionFlags.MatchBlueprints | ConstructionFlags.FireCrew, maxCrewToHire: 0))
        {
            // Vanilla shows "construction blocked" to a player here. The blueprints stay, so a player can see and fix it.
            Log.Info($"build: construction of {ship.Metadata.PlainShipName} is blocked; the blueprints stay");
            return;
        }
        construct.QueuePhysical(updateDestroyed: true);
        ship.ClearDestroyed();
        ship.Metadata.ApplyAllBlueprintStates();
    }

    /// <summary>
    /// Spec 1084: a saved design file as a slot: the vanilla design data without the design's own slots (they never nest), named after
    /// the file. Throws FormatException when the file is not a design.
    /// </summary>
    internal static BuildDesign FromFile(string path)
    {
        using var proto = Ship.TryLoad(path, showErrorDialog: false, blueprintsOnly: true) ?? throw new FormatException($"'{path}' is not a ship design");
        if (ShipTactics.Of(proto) is { } t && t.Cargo.Designs.Any(x => x is not null))
            t.SetCargo(AutonomyModel.Encode(t.Cargo with { Designs = new BuildDesign?[BuildModel.MaxDesigns] }));
        using var stream = new MemoryStream();
        proto.SaveDesign(stream);
        string name = BuildModel.CleanName(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)));
        var d = new BuildDesign(name.Length > 0 ? name : "Design", Convert.ToBase64String(stream.ToArray()));
        InfoOf(d);
        return d;
    }
}

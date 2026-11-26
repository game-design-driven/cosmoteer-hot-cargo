using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Generators.Galaxies;
using Cosmoteer.Generators.Names;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Map;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Journey;

/// <summary>
/// Galaxy map rules type "HotCargoRunMap" (spec 2550, 2551): makes the systems and links of the run map (RunMapModel) in place of the
/// vanilla MapNodes spawner. Steps is Journey.RunSectorCount (MapRules sets it before each new galaxy).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoRunMap")]
[TrackMultipleReferences]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class RunMapSpawner : GalaxySpawner
{
    [Serialize, PortableHash] public int Steps = 6;
    [Serialize(Optional = true)] public NameGenerator? NameGenerator;

    public override void DoSpawn(MapRoot map, Rand rand, Action<MapNode>? onNodeCreated)
    {
        var layout = RunMapModel.Generate(Steps, rand.Single);
        var nodes = layout.Nodes.Select(p =>
        {
            var node = new MapNode(map.Game, new Vector2(p.X, p.Y), NameGenerator?.GenerateName(rand, 30), rand.UInt64());
            map.Nodes.AddNode(node);
            return node;
        }).ToList();
        foreach (var link in layout.Links) map.Nodes.AddConnection(nodes[link.A], nodes[link.B]);
        foreach (var node in nodes) onNodeCreated?.Invoke(node);
        Log.Info($"run map: {Steps} steps, {nodes.Count} systems, {layout.Links.Count} links");
    }
}

/// <summary>
/// Galaxy map rules type "HotCargoRunMapTiers" (spec 2552, 2553). Runs after the vanilla Factions spawner: the start system (band 0)
/// becomes the starting system (basic, fringe space), and every system gets the tier of its band.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoRunMapTiers")]
[TrackMultipleReferences]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class RunMapTiersSpawner : GalaxySpawner
{
    [Serialize, PortableHash] public int FinalTier = 12;
    [Serialize, PortableHash] public int TierSpread = 2;
    [Serialize, PortableHash] public ID<FactionRules> StartFaction;
    [Serialize, PortableHash] public ID<SectorTypeRules> StartSectorType;

    public override void DoSpawn(MapRoot map, Rand rand, Action<MapNode>? onNodeCreated)
    {
        if (map.Game.Mode is not CareerGameModeManager mode) return;
        int steps = RunMap.LastBand(map) + 1;
        foreach (var node in map.Nodes)
        {
            int tier = RunMapModel.Tier(RunMap.Band(node), steps, FinalTier);
            var data = mode.GetMapNodeData(node);
            data.TierRange = new Range<int>(tier, tier + TierSpread);
            if (RunMap.Band(node) != 0) continue;
            data.IsInitNode = true;
            data.SectorTypeID = StartSectorType;
            node.SetFactionStrength(StartFaction, 1f, clearOthers: true);
        }
    }
}

/// <summary>Spec 2550 to 2556: what the game reads from the run map. The band of a system follows from its location.</summary>
public static class RunMap
{
    internal static int Band(MapNode node) => RunMapModel.Band(node.Location.X);

    internal static float Position(MapNode node) => RunMapModel.Position(node.Location.X);

    /// <summary>The last band and the Buyer's system of a map. The map never changes after the galaxy is made, so it is read once.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MapRoot, Tuple<int, MapNode>> Ends = new();

    static Tuple<int, MapNode> EndsOf(MapRoot map) => Ends.GetValue(map, m =>
    {
        int last = m.Nodes.Max(Band);
        return Tuple.Create(last, m.Nodes.Single(n => Band(n) == last));
    });

    /// <summary>The highest band of the map. While the galaxy is made (RunMapTiersSpawner), it reads the nodes without the cache.</summary>
    internal static int LastBand(MapRoot map) => map.Nodes.Max(Band);

    /// <summary>True when the career uses the Hot Cargo run map size.</summary>
    public static bool IsRunMap(CareerGameModeManager mode) => mode.Init.MapSizeIndex == JourneyArea.RunMapSizeIndex();

    /// <summary>Spec 2554: the Buyer's system, the one system of the last band. Null on another map size.</summary>
    internal static MapNode? BuyerNode(Run run) =>
        IsRunMap(run.Mode) ? EndsOf(run.Game.Map).Item2 : null;

    /// <summary>Spec 2555: the steps of the run: the map's bands on the run map, else Journey.RunSectorCount.</summary>
    public static int Steps(Run run) => IsRunMap(run.Mode) ? EndsOf(run.Game.Map).Item1 + 1 : run.T.Journey.RunSectorCount;

    /// <summary>
    /// Spec 2555: the step of the run (1 = the start, Steps = the Buyer): on the run map, 1 + the furthest band that the players
    /// entered; else the sector index.
    /// </summary>
    public static int Step(Run run)
    {
        if (!IsRunMap(run.Mode)) return run.State.SectorIndex;
        var visited = run.State.VisitedNodes;
        return 1 + run.Game.Map.Nodes.Where(n => visited.Contains(n.UniqueID) || n == run.Game.CurrentMapNode).Max(Band);
    }

    /// <summary>Spec 2555: run progress from 0 at the start to 1 at the Buyer (hunter waves and the Pursuit ramp read it).</summary>
    public static float Progress(Run run) => Hunters.WaveModel.Progress(Step(run), Steps(run));
}

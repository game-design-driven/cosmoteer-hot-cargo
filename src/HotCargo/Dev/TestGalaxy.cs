using System.Globalization;
using System.Text;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Localization;
using Cosmoteer.Generators.Galaxies;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Map;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Geometry;
using Halfling.ObjectText;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Dev;

/// <summary>One system of the test galaxy, as the game made it once (spec 1702).</summary>
[ReflectiveSerialization(Explicit = true)]
[ReflectivePortableHashing(Explicit = true)]
public sealed class TestSystem
{
    [Serialize, PortableHash] public string Name = "";
    [Serialize, PortableHash] public Vector2 Location;
    [Serialize, PortableHash] public ulong SimSeed;
    [Serialize, PortableHash] public ulong RandomSeed;
    /// <summary>Empty when the game gave the system no sector type.</summary>
    [Serialize(Optional = true), PortableHash] public string SectorType = "";
    [Serialize, PortableHash] public int TierMin;
    [Serialize, PortableHash] public int TierMax;
    [Serialize(Optional = true), PortableHash] public bool Start;
    /// <summary>In the order of the game's faction table: the order decides ties.</summary>
    [Serialize(Optional = true), PortableHash] public TestFaction[] Factions = [];
    /// <summary>Indexes of the linked systems that come later in the list.</summary>
    [Serialize(Optional = true), PortableHash] public int[] Links = [];
}

[ReflectiveSerialization(Explicit = true)]
[ReflectivePortableHashing(Explicit = true)]
public sealed class TestFaction
{
    [Serialize, PortableHash] public string ID = "";
    [Serialize, PortableHash] public float Strength;
}

/// <summary>
/// Spec 1702: galaxy map rules type "HotCargoTestGalaxy", the fixed galaxy of the autotests (mod/dev/test_world.rules). It makes the
/// systems and links of the data and sets every input of system generation that the game keeps per system: the seeds, the sector
/// type, the tier range, the start flag and the faction strengths. At the end the galaxy random stream takes its saved state, so the
/// visit missions that the game makes next (CareerGameModeManager.CreateVisitNodeMissions) come out the same too. So a change of
/// the map generator does not change the world that the tests start in.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoTestGalaxy")]
[TrackMultipleReferences]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class TestGalaxySpawner : GalaxySpawner
{
    [Serialize, PortableHash] public TestSystem[] Systems = [];
    [Serialize, PortableHash] public ulong RandA;
    [Serialize, PortableHash] public ulong RandB;

    public override void DoSpawn(MapRoot map, Rand rand, Action<MapNode>? onNodeCreated)
    {
        if (map.Game.Mode is not CareerGameModeManager mode) throw new InvalidOperationException("the test galaxy needs a career");
        var nodes = Systems.Select(s =>
        {
            var node = new MapNode(map.Game, s.Location, s.Name, s.SimSeed);
            map.Nodes.AddNode(node);
            onNodeCreated?.Invoke(node);
            return node;
        }).ToList();
        for (int i = 0; i < Systems.Length; i++)
        {
            var s = Systems[i];
            foreach (int j in s.Links) map.Nodes.AddConnection(nodes[i], nodes[j]);
            var data = mode.GetMapNodeData(nodes[i]);
            data.RandomSeed = s.RandomSeed;
            data.SectorTypeID = s.SectorType == "" ? null : new ID<SectorTypeRules>(s.SectorType);
            data.TierRange = new Range<int>(s.TierMin, s.TierMax);
            data.IsInitNode = s.Start;
            nodes[i].ClearAllFactionStrengths();
            foreach (var f in s.Factions) nodes[i].SetFactionStrength(new ID<FactionRules>(f.ID), f.Strength);
        }
        if (rand is not FastRand fast) throw new InvalidOperationException($"the galaxy random source is a {rand.GetType().Name}, not a FastRand");
        (fast._a, fast._b) = (RandA, RandB);
    }
}

/// <summary>Spec 1702: the test galaxy as a map size that only autotests add, and the command that writes it from a real galaxy.</summary>
static class TestGalaxy
{
    const string NameKey = "HotCargoDev/TestGalaxy";
    static string FilePath => Path.Combine(Mod.ModRoot, "dev", "test_world.rules");

    /// <summary>
    /// Adds the test galaxy to the career map sizes. Autotest.Init calls it on every machine of a test, so co-op machines list it at
    /// the same index. It is not the run map size, so the run map rules (spec 2550 to 2567) are off in it.
    /// </summary>
    internal static void Add()
    {
        var rules = GameApp.Rules.CareerMode;
        if (Array.Exists(rules.MapSizes, m => m.NameKey.Key == NameKey)) return;
        var spawner = App.ObjectTextSerializer.ReadFromPath<TestGalaxySpawner>(new OTFile(FilePath), "TestGalaxy")
            ?? throw new InvalidDataException($"TestGalaxy missing in {FilePath}");
        rules.MapSizes = [.. rules.MapSizes, new MapSize { NameKey = new KeyString(NameKey), TipKey = new KeyString(NameKey), MapGenerator = new GalaxyGenerator { Spawners = [spawner] } }];
        Log.Info($"test galaxy: {spawner.Systems.Length} systems from {FilePath}");
    }

    /// <summary>The map size index of the test galaxy. Throws when Add did not run.</summary>
    internal static int Index()
    {
        int i = Array.FindIndex(GameApp.Rules.CareerMode.MapSizes, m => m.NameKey.Key == NameKey);
        return i >= 0 ? i : throw new InvalidOperationException("the test galaxy is not in the map sizes; only autotests add it");
    }

    /// <summary>The galaxy random stream just before the visit missions: what the test galaxy restores. Kept only in autotests.</summary>
    static (ulong A, ulong B)? _randBeforeVisits;

    [HarmonyPatch(typeof(CareerGameModeManager), "CreateVisitNodeMissions")]
    static class RandCapture
    {
        static void Prefix(Rand rand)
        {
            if (Autotest.IsActive && rand is FastRand fast) _randBeforeVisits = (fast._a, fast._b);
        }
    }

    /// <summary>
    /// systemprint: logs what the current system holds (live ships, and the ships, stations and doodads in stasis) in a stable order,
    /// with one line for the whole list. Two runs that print the same line start in the same system.
    /// </summary>
    internal static void PrintSystem()
    {
        var game = Autotest.RequireRun().Game;
        var sim = game.Sim;
        static string At(Vector2 v) => FormattableString.Invariant($"{MathF.Round(v.X / 10) * 10},{MathF.Round(v.Y / 10) * 10}");
        var lines = new List<string>();
        foreach (Ship ship in sim.Ships) lines.Add($"ship {ship.Metadata.PlainShipName} {(ship.IsHumanPlayerShip ? "human" : "ai")} at {At(ship.DetWorldCenter)}");
        foreach (var stasis in sim.Stasis)
        {
            lines.Add(stasis switch
            {
                Cosmoteer.Simulation.Stasis.StasisShipSpawner ship =>
                    $"stasis ship [{string.Join(" ", SimQueries.StasisTags(ship) ?? [])}] at {At(ship.Location)}",
                Cosmoteer.Simulation.Stasis.StasisDoodadSpawner d => $"stasis {d.DoodadID} at {At(d.Location)}",
                _ => $"stasis {stasis.GetType().Name} at {At(stasis.Location)}",
            });
        }
        lines.Sort(StringComparer.Ordinal);
        string all = string.Join("\n", lines);
        Log.Info($"AUTOTEST SYSTEM {game.CurrentMapNode.Name}: {lines.Count} objects, hash {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(all)))[..16]}\n{all}");
    }

    /// <summary>testworld_dump: writes mod/dev/test_world.rules (in the deployed mod folder) from the galaxy of the current game.</summary>
    internal static void Dump()
    {
        var run = Autotest.RequireRun();
        if (_randBeforeVisits is not { } r) throw new InvalidOperationException("no galaxy made in this autotest (start one with newgame generated)");
        var nodes = run.Game.Map.Nodes.ToList();
        var index = nodes.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("// Spec 1702: the fixed galaxy of the autotests (Dev/TestGalaxy.cs). Written by the autotest command testworld_dump from a real");
        sb.AppendLine("// galaxy (galaxy seed 12345); every system keeps the inputs of its generation. Do not edit by hand: write it again.");
        sb.AppendLine("TestGalaxy");
        sb.AppendLine("{");
        sb.AppendLine(string.Create(inv, $"\tRandA = {r.A}"));
        sb.AppendLine(string.Create(inv, $"\tRandB = {r.B}"));
        sb.AppendLine("\tSystems");
        sb.AppendLine("\t[");
        foreach (var n in nodes)
        {
            var d = run.Mode.GetMapNodeData(n);
            var tier = d.TierRange ?? throw new InvalidOperationException($"{n.Name} has no tier");
            var links = n.Connections.Select(c => index[c]).Where(j => j > index[n]).Order();
            var factions = n.FactionStrengths.Select(f => string.Create(inv, $"{{ ID = {f.Key}; Strength = {f.Value:R} }}"));
            sb.AppendLine(string.Create(inv, $"\t\t{{ Name = \"{n.Name}\"; Location = [{n.Location.X:R}, {n.Location.Y:R}]; SimSeed = {n.SimSeed}; " +
                $"RandomSeed = {d.RandomSeed}; SectorType = \"{d.SectorTypeID?.ToString() ?? ""}\"; " +
                $"TierMin = {tier.Min}; TierMax = {tier.Max}; Start = {(d.IsInitNode ? "true" : "false")}; " +
                $"Factions = [{string.Join(", ", factions)}]; Links = [{string.Join(", ", links)}] }}"));
        }
        sb.AppendLine("\t]");
        sb.AppendLine("}");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, sb.ToString());
        Log.Info($"AUTOTEST testworld_dump: {nodes.Count} systems to {FilePath}");
        Log.Info("AUTOTEST TESTWORLD BEGIN\n" + sb + "AUTOTEST TESTWORLD END");
    }
}

using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using PartAt = HotCargo.Core.ShipBuilder.PartAt;

namespace HotCargo.Thieves;

/// <summary>
/// Spec 1772, 1773: the thief ships, one design per tier of the vanilla tier table (TierValueMaximums), built here from vanilla parts like the
/// scavenger (spec 1342). A cargo freighter around a corridor spine (x = 0) that every room opens onto: a command core with the Manipulator
/// Beam Emitter (the "funnel") at the nose, then segments down the spine (in a cycle of three: weapons, power, armor), and thrusters at the tail. Segments are added until the design's value reaches
/// ValueShare of the tier's value maximum; higher tiers use kits of bigger parts (Kits).
/// Rotation 0 faces the nose (up, -y). The doors come from the game's automatic door rule.
/// </summary>
internal static class ThiefShip
{
    /// <summary>The share of the tier's value maximum that a thief is worth: a strong ship of its tier (vanilla tier ships lie between the maximum of the tier below and of their tier).</summary>
    internal const float ValueShare = 0.85f;
    /// <summary>A kit that needs more segments than this to reach the target gives way to the next, bigger kit.</summary>
    const int MaxSegments = 6;
    /// <summary>
    /// The fewest segments: one weapon and one power segment, so every design has a reactor (the core has none).
    /// </summary>
    const int MinSegments = 2;
    /// <summary>The biggest kit stops here, so the top tiers stay a sane length (their value stays below the target).</summary>
    const int MaxTopSegments = 12;

    /// <summary>
    /// One kit: a segment Rows tall. Storage and Reactor are Rows × Rows; the energy weapon lies sideways (WeaponLength wide, WeaponHeight
    /// tall), stacked Rows / WeaponHeight per side; the crew quarters are QuartersWidth × QuartersHeight, at most Rows tall, at rotation
    /// QuartersRot, so that a door faces the spine on their right (crew_quarters_med has doors on one side only).
    /// </summary>
    sealed record Kit(int Rows, string Storage, string Weapon, int WeaponLength, int WeaponHeight, string Reactor, string Quarters, int QuartersWidth, int QuartersHeight, int QuartersRot);
    static readonly Kit[] Kits =
    [
        new(2, "cosmoteer.storage_2x2", "cosmoteer.laser_blaster_large", 5, 2, "cosmoteer.reactor_small", "cosmoteer.crew_quarters_med", 2, 2, 1),
        new(3, "cosmoteer.storage_3x3", "cosmoteer.laser_blaster_large", 5, 2, "cosmoteer.reactor_med", "cosmoteer.crew_quarters_med", 2, 2, 1),
        new(4, "cosmoteer.storage_4x4", "cosmoteer.ion_beam_emitter", 5, 2, "cosmoteer.reactor_large", "cosmoteer.crew_quarters_large", 3, 4, 0),
    ];

    /// <summary>The number of tiers: the length of the vanilla tier value table.</summary>
    internal static int TierCount => GameData.TierCount;

    /// <summary>
    /// Spec 1772: the parts of a design with this kit and segment count, in an order where each part touches one before it. Rotation 0 faces
    /// up; rotation 1 faces right (+x), 3 faces left. A part's cell is the top left of its rotated footprint.
    /// </summary>
    static List<PartAt> Layout(Kit k, int segments)
    {
        int r = k.Rows;
        var parts = new List<PartAt>();
        // Command core: rows y = 0 .. 2r-1, the funnel above it at the nose.
        int coreRows = 2 * r;
        for (int y = -2; y < coreRows; y++) parts.Add(new("cosmoteer.corridor", 0, y, 0));
        parts.Add(new("cosmoteer.manipulator_beam_emitter", 1, -2, 0));
        parts.Add(new("cosmoteer.control_room_small", 1, 0, 0));
        parts.Add(new(k.Quarters, -k.QuartersWidth, 0, k.QuartersRot));
        parts.Add(new(k.Storage, 1, r, 0));
        parts.Add(new(k.Quarters, -k.QuartersWidth, r, k.QuartersRot));
        // A third crew quarters beside the funnel at the nose: the funnel alone takes 4 crew.
        parts.Add(new(k.Quarters, -k.QuartersWidth, -k.QuartersHeight, k.QuartersRot));
        // The ship editor's vital parts include a fire extinguisher: on the nose of the spine, its door down into it.
        parts.Add(new("cosmoteer.fire_extinguisher", 0, -3, 0));
        // Segments in a cycle of three: weapons (storage on both sides, energy weapons outside), power (a reactor and crew quarters), armor
        // (storage on both sides, a 2-deep armor shell outside). Every design has at least one of the first two.
        for (int i = 0; i < segments; i++)
        {
            int y0 = coreRows + i * r;
            for (int y = y0; y < y0 + r; y++) parts.Add(new("cosmoteer.corridor", 0, y, 0));
            bool power = i % 3 == 1;
            parts.Add(new(power ? k.Reactor : k.Storage, 1, y0, 0));
            parts.Add(power ? new(k.Quarters, -k.QuartersWidth, y0, k.QuartersRot) : new(k.Storage, -r, y0, 0));
            if (i % 3 == 0)
            {
                for (int y = y0; y + k.WeaponHeight <= y0 + r; y += k.WeaponHeight)
                {
                    parts.Add(new(k.Weapon, r + 1, y, 1));
                    parts.Add(new(k.Weapon, -r - k.WeaponLength, y, 3));
                }
            }
            else
            {
                int left = power ? k.QuartersWidth : r;
                for (int y = y0; y < y0 + r; y++)
                    for (int d = 1; d <= 2; d++)
                    {
                        parts.Add(new("cosmoteer.armor", r + d, y, 0));
                        parts.Add(new("cosmoteer.armor", -left - d, y, 0));
                    }
            }
        }
        // Tail: a corridor across the width, so crew reach every thruster with batteries (the rooms of the last segment may let no crew
        // through), then an airlock on the spine (the crew fetch loot through it) and thrusters across the width.
        int tail = coreRows + segments * r;
        parts.Add(new("cosmoteer.corridor", 0, tail, 0));
        for (int x = 1; x <= r; x++)
        {
            parts.Add(new("cosmoteer.corridor", x, tail, 0));
            parts.Add(new("cosmoteer.corridor", -x, tail, 0));
        }
        tail++;
        parts.Add(new("cosmoteer.airlock", 0, tail, 2));
        for (int x = 1; x <= r; x++)
        {
            parts.Add(new("cosmoteer.thruster_small", x, tail, 0));
            parts.Add(new("cosmoteer.thruster_small", -x, tail, 0));
        }
        // Armor on the sides of the core, from the bottom up, so each piece touches a part placed before it.
        for (int y = coreRows - 1; y >= 0; y--)
        {
            parts.Add(new("cosmoteer.armor", r + 1, y, 0));
            parts.Add(new("cosmoteer.armor", -k.QuartersWidth - 1, y, 0));
        }
        return parts;
    }

    /// <summary>For price studies (econ_census): the parts of every kit layout that Design can pick. Test only.</summary>
    internal static IEnumerable<(int Kit, int Segments, IEnumerable<string> Parts)> CensusLayouts()
    {
        for (int k = 0; k < Kits.Length; k++)
            for (int n = MinSegments; n <= (k == Kits.Length - 1 ? MaxTopSegments : MaxSegments); n++)
                yield return (k, n, Layout(Kits[k], n).Select(p => p.Part));
    }

    /// <summary>The ship rules that hold every part of all kits (the terran rules in 0.30.5a).</summary>
    static ShipRules Rules() => ShipBuilder.RulesFor(Kits.SelectMany(k => Layout(k, 3)), "thief");

    /// <summary>The value of a layout: the vanilla cost of its parts.</summary>
    static int Value(List<PartAt> layout)
    {
        using var ship = ShipBuilder.Build(Rules(), layout, "value", "thief");
        return ship.Parts.TotalMoneyValue;
    }

    /// <summary>
    /// Spec 1772: the design of a tier: the first kit (smallest first) that reaches ValueShare × the tier's value maximum within MaxSegments
    /// segments, with the fewest segments (at least MinSegments) that do. When none does, the biggest kit with as many segments as reach it, at most MaxTopSegments.
    /// Limit: designs of the top tiers stop at MaxTopSegments and stay below the target (tier 20 reaches about a third); revisit if thieves
    /// of those tiers show up in play.
    /// </summary>
    internal static List<PartAt> Design(int tier)
    {
        float target = ValueShare * GameData.TierValueMaximum(tier);
        foreach (var kit in Kits)
            for (int n = MinSegments; n <= (kit == Kits[^1] ? MaxTopSegments : MaxSegments); n++)
            {
                var layout = Layout(kit, n);
                if (Value(layout) >= target || kit == Kits[^1] && n == MaxTopSegments) return layout;
            }
        throw new InvalidOperationException($"thief design: no design for tier {tier}");
    }

    static readonly Dictionary<int, byte[]> DesignBytes = new();

    /// <summary>The saved design of a tier, built once per process.</summary>
    internal static byte[] Bytes(int tier)
    {
        tier = Math.Clamp(tier, 1, TierCount);
        if (DesignBytes.TryGetValue(tier, out var cached)) return cached;
        return DesignBytes[tier] = ShipBuilder.DesignBytes(Design(tier), $"Thief {tier}", "thief");
    }

    /// <summary>
    /// Spec 1773: a full thief of the tier with crew, power and empty storage bays, the given AI, AI parameters and player index, at the spot, named
    /// "Thief N" (a number no ship in the system has). The object ID and the random seed come from the sim random source, so every co-op
    /// machine builds the same ship.
    /// </summary>
    internal static Ship Spawn(SimRoot sim, int tier, int playerIndex, ID<ShipAIRules> ai, AIParameters parameters, Vector2 at, Direction facing)
    {
        var ship = ShipBuilder.Load(sim, sim.Rand, Bytes(tier), Name(sim), playerIndex, at, facing);
        ship.AI = new ShipAI(GameApp.Rules.ShipAIs[ai], parameters);
        sim.Ships.Add(ship);
        Log.Info($"thieves: {ship.Metadata.PlainShipName} id={ship.UniqueID} tier {tier} player {playerIndex} ai {ai} arrives at " +
                 $"{at.X.ToString("0", CultureInfo.InvariantCulture)},{at.Y.ToString("0", CultureInfo.InvariantCulture)} parts={ship.Parts.Count} " +
                 $"value={ship.Parts.TotalMoneyValue} crew={ship.Crew.Assigned.Count}/{ship.Crew.TotalCrewCapacity}");
        return ship;
    }

    /// <summary>"Thief" and a number that no ship in the system has (live or in stasis).</summary>
    static string Name(SimRoot sim)
    {
        var taken = sim.Ships.AllShips.Select(s => s.Metadata.PlainShipName).Concat(sim.Stasis.Select(sp => sp.ObjectName).OfType<string>()).ToHashSet();
        for (int n = 1; ; n++)
            if (!taken.Contains($"Thief {n}")) return $"Thief {n}";
    }
}

using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Random;

namespace HotCargo.Core;

/// <summary>
/// Ships that Hot Cargo designs from vanilla parts (the scavenger, the thieves, the Mining Escort miner): build a design from a part
/// list, save it, and spawn full ships of it. Rotation 0 faces up (-y); a part's cell is the top left of its rotated footprint.
/// </summary>
internal static class ShipBuilder
{
    internal readonly record struct PartAt(string Part, int X, int Y, int Rot);

    /// <summary>The ship rules that hold every part of the layout (the terran rules in 0.30.5a), found from the rules, not named.</summary>
    internal static ShipRules RulesFor(IEnumerable<PartAt> layout, string what)
    {
        var ids = layout.Select(p => new ID<PartRules>(p.Part)).Distinct().ToList();
        return GameApp.Rules.Ships.FirstOrDefault(r => !r.IsAsteroid && ids.All(r.PartsByID.ContainsKey))
            ?? throw new InvalidOperationException($"{what} design: no ship rules hold all its parts");
    }

    /// <summary>Builds a design from a layout, in the given rules. Throws when a part does not fit, so a rules change shows at once.</summary>
    internal static Ship Build(ShipRules rules, IEnumerable<PartAt> layout, string name, string what)
    {
        var ship = new Ship(rules, name, 0, randSeed: 1UL);
        // One update per part: the automatic door rule adds doors only toward parts that the ship already has.
        foreach (var (part, x, y, rot) in layout)
        {
            using var sui = ShipUpdateInfo.Alloc(ship);
            var partRules = rules.PartsByID[new ID<PartRules>(part)];
            if (!sui.TryAddBlueprintPart(partRules, new IntVector2(x, y), rot, flipX: false, mirrorPhysical: false, allowOverwrite: false, addDoors: true,
                    hireCrew: false, out var reasons, force: false))
                throw new InvalidOperationException($"{what} design: cannot add {part} at {x},{y} rot {rot}: {reasons}");
            sui.Apply();
        }
        ship.MatchPhysicalToBlueprints();
        return ship;
    }

    /// <summary>The saved design of a layout, in the rules that hold its parts. <paramref name="decorate"/> can paint the design before it is saved.</summary>
    internal static byte[] DesignBytes(IReadOnlyList<PartAt> layout, string name, string what, Action<Ship>? decorate = null)
    {
        using var ship = Build(RulesFor(layout, what), layout, name, what);
        decorate?.Invoke(ship);
        using var stream = new MemoryStream();
        ship.SaveDesign(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// A full ship of a saved design with crew, power and empty storage bays, at the spot, facing the direction, not yet in the sim. The object
    /// ID and the random seed come from the given random source (sim.Rand in a tick, the spawner's rand in system generation), so every co-op
    /// machine builds the same ship. The caller sets the AI and adds the ship.
    /// </summary>
    internal static Ship Load(SimRoot sim, Rand rand, byte[] design, string name, int playerIndex, Vector2 at, Direction facing)
    {
        ObjectID id;
        do id = ObjectID.NewRandom(rand); while (!sim.IsObjectIDAvailable(id));
        ulong seed = rand.UInt64();
        var ship = Ship.Load(design, name, playerIndex, spawnWithCrew: true, initializeToMax: true, randSeed: seed, id: id);
        ship.WorldCenter = at;
        ship.WorldRotation = facing - (float)ship.FlightDirection;
        return ship;
    }
}

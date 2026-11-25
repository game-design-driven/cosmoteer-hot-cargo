using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Paint;
using Halfling;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Random;
using HotCargo.Core;
using PartAt = HotCargo.Core.ShipBuilder.PartAt;

namespace HotCargo.Missions.Mechanics;

/// <summary>
/// Spec 1868, 1869: the Mining Escort miner, a civilian ore tender built from vanilla parts (<see cref="ShipBuilder"/>). A two-wide corridor
/// spine runs from the nose to the tail, so the collector beam sits on the center line. Rotation 0 faces the nose (up, -y).
/// </summary>
internal static class MinerShip
{
    internal static readonly ID<ShipAIRules> AiId = new("HotCargoMiner");

    /// <summary>
    /// Nose: a mining laser on each side, their 270 degree arcs forward, and the manipulator beam (the collector) between them on the
    /// center line. Behind them the control room and the reactor, with a braking thruster on each flank. Then two storage bays (the hold:
    /// 80 hyperium or 40 iron, spec 1870) with an airlock and a side thruster on each flank close to where the ore falls (the crew
    /// fetch it through them), two crew quarters with their doors into the bays, a fire extinguisher on the spine, and six medium thrusters
    /// at the tail. Each part touches one placed before it. Every small thruster touches a room that crew walk (control room, reactor, bay),
    /// so crew bring it batteries: quarters let no crew through, and crew_quarters_med has doors on its top side only.
    /// </summary>
    internal static readonly PartAt[] Layout = BuildLayout();

    static PartAt[] BuildLayout()
    {
        var parts = new List<PartAt>();
        for (int y = -4; y <= 1; y++)
        {
            // The spine's last tile on the left holds the fire extinguisher, its door up into the spine.
            parts.Add(y == 1 ? new("cosmoteer.fire_extinguisher", 0, y, 2) : new("cosmoteer.corridor", 0, y, 0));
            parts.Add(new("cosmoteer.corridor", 1, y, 0));
        }
        parts.Add(new("cosmoteer.manipulator_beam_emitter", 0, -6, 0));
        parts.Add(new("cosmoteer.control_room_small", -2, -4, 0));
        parts.Add(new("cosmoteer.reactor_small", 2, -4, 0));
        parts.Add(new("cosmoteer.mining_laser_small", -2, -7, 0));
        parts.Add(new("cosmoteer.mining_laser_small", 2, -7, 0));
        parts.Add(new("cosmoteer.thruster_small", -3, -4, 2));
        parts.Add(new("cosmoteer.thruster_small", 4, -4, 2));
        parts.Add(new("cosmoteer.storage_2x2", -2, -2, 0));
        parts.Add(new("cosmoteer.storage_2x2", 2, -2, 0));
        parts.Add(new("cosmoteer.airlock", -3, -2, 3));
        parts.Add(new("cosmoteer.airlock", 4, -2, 1));
        parts.Add(new("cosmoteer.thruster_small", -4, -1, 1));
        parts.Add(new("cosmoteer.thruster_small", 4, -1, 3));
        parts.Add(new("cosmoteer.crew_quarters_med", -2, 0, 0));
        parts.Add(new("cosmoteer.crew_quarters_med", 2, 0, 0));
        parts.Add(new("cosmoteer.armor", -3, 0, 0));
        parts.Add(new("cosmoteer.armor", 4, 0, 0));
        for (int x = -2; x <= 3; x++) parts.Add(new("cosmoteer.thruster_med", x, 2, 0));
        return [.. parts];
    }

    static byte[]? _design;

    /// <summary>The saved design, built once per process from the rules.</summary>
    internal static byte[] Design() => _design ??= ShipBuilder.DesignBytes(Layout, "Ore Tender", "miner", Decorate);

    /// <summary>
    /// Spec 1869: the roof pattern, vanilla decals in the three decal colors of the faction's paint (MinerShip.Paint): light panels on the
    /// side rooms, mid-tone housings on the lasers, dark chevrons down the spine and caution diamonds on the cargo bays. Tile coordinates.
    /// </summary>
    static readonly (int Layer, string Decal, int X, int Y, int W, int H)[] Decals =
    [
        (1, "box", -2, -4, 2, 2), (1, "box", -2, -2, 2, 2), (1, "box", -2, 0, 2, 2),
        (1, "box", 2, -4, 2, 2), (1, "box", 2, -2, 2, 2), (1, "box", 2, 0, 2, 2),
        (0, "box", -2, -7, 2, 3), (0, "box", 2, -7, 2, 3),
        (2, "stripe_chevron", 0, -4, 2, 2), (2, "stripe_chevron", 0, -2, 2, 2), (2, "stripe_chevron", 0, 0, 2, 2),
        (2, "shape_caution_diamond", -2, -2, 2, 2), (2, "shape_caution_diamond", 2, -2, 2, 2),
    ];

    static void Decorate(Ship ship)
    {
        // The headless sim (tests, CI) loads no decal textures, so it has no decal catalog. Decals are only looks, so the design goes without.
        if (ship.Rules.Roofs.RoofDecals.Count == 0) return;
        foreach (var (layer, decal, x, y, w, h) in Decals)
            ship.DecalLayers[layer].AddDecal(new Decal(new ID<Decal>(decal), new IntVector2(x, y), new IntVector2(w, h), 0, flipX: false, invert: false));
    }

    static readonly Dictionary<ID<FactionRules>, PaintScheme?> Paints = new();
    static readonly ID<BuiltinShipTag> TradeTag = new("trade");

    /// <summary>
    /// Spec 1869: the paint of the faction's civilian trade ships: the paint scheme of its trade built-in ship with the lowest ID. Null when the
    /// faction has none; the miner then keeps the default paint.
    /// </summary>
    static PaintScheme? Paint(FactionRules faction)
    {
        if (Paints.TryGetValue(faction.ID, out var cached)) return cached;
        var source = GameApp.Rules.BuiltinShips.Where(b => b.Faction == faction.ID && b.Tags?.Contains(TradeTag) == true)
            .OrderBy(b => b.ID.ToString(), StringComparer.Ordinal).FirstOrDefault();
        PaintScheme? paint = null;
        if (source is not null)
        {
            using var ship = source.File.LoadShipBlueprints();
            paint = ship.Metadata.GetPaintScheme();
        }
        Log.Info($"mining escort: miner paint of {faction.ID} from {source?.ID.ToString() ?? "none"}: base {paint?.RoofBaseColor} texture {paint?.RoofBaseTexture} decals {string.Join(" ", paint?.RoofDecalColors ?? [])}");
        return Paints[faction.ID] = paint;
    }

    /// <summary>
    /// Spec 1868: a full miner of the faction's civilians, with the miner AI, at the spot. Far from the players the game puts it into stasis
    /// like any other ship (vanilla SerializedStasisShip).
    /// </summary>
    internal static Ship Spawn(SimRoot sim, Rand rand, FactionRules faction, Vector2 at, Direction facing)
    {
        var ship = ShipBuilder.Load(sim, rand, Design(), Strings.GetText("HotCargoMechanics/MinerName"), faction.CivilianPlayerIndex, at, facing);
        if (Paint(faction) is { } paint) ship.Metadata.SetPaintScheme(paint);
        ship.AI = new ShipAI(GameApp.Rules.ShipAIs[AiId], new AIParameters { ChanceToConsiderSurrenderingToPlayer = 0f });
        sim.Ships.Add(ship);
        Log.Info($"mining escort: miner {ship.Metadata.PlainShipName} id={ship.UniqueID} player {faction.CivilianPlayerIndex} at {at} parts={ship.Parts.Count} " +
                 $"crew={ship.Crew.Assigned.Count}/{ship.Crew.TotalCrewCapacity}");
        return ship;
    }
}

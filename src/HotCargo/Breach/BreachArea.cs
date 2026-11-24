using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Modes;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Breach;

/// <summary>
/// Entry point of the Breach area (spec 1610 to 1617). A crew member in space can enter a ship only through an airlock (vanilla
/// SpaceCrew, Airlock.GetNearestOnShip). When its home ship has no working airlock, it waits, then cuts a breach: a weak airlock part
/// with a door to a walkable tile behind it. From then on every crew member paths through the breach like any airlock. All of it runs in
/// the sim tick and reads and writes the run state, so every co-op machine places the same breach at the same tick (spec 1616).
/// </summary>
public static class BreachArea
{
    internal static readonly ID<PartRules> PartId = new("yarden_zamir.breach");
    /// <summary>Game seconds between two checks.</summary>
    const double CheckSeconds = 1;
    static readonly IntVector2[] Sides = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    public static void Register()
    {
        Run.Hooks.Tick += (run, sim, dt) => Errors.Guard("breach tick", () => OnTick(run, sim, dt));
        Run.Hooks.SystemEntered += (run, _) => { run.State.Breach.Stuck.Clear(); run.State.Breach.Cuts.Clear(); };
        BreachAutotest.Register();
    }

    static string Text(string key, params object?[] args) => Strings.FormatText("HotCargoBreach/" + key, args);

    /// <summary>Spec 1615: an airlock part that is not destroyed and works (its switch on, not scorched). A breach counts too.</summary>
    internal static bool HasWorkingAirlock(Ship ship)
    {
        foreach (var part in ship.Parts)
            if (!part.IsDestroyed)
                foreach (var c in part.Components)
                    if (c is Airlock { IsOperational: true }) return true;
        return false;
    }

    static void OnTick(Run run, SimRoot sim, double dt)
    {
        if (sim != run.Game.Sim) return;
        var s = run.State.Breach;
        var t = run.T.Breach;
        if (!t.Enabled)
        {
            s.Stuck.Clear();
            s.Cuts.Clear();
            return;
        }
        double now = run.State.GameSeconds;
        if (Math.Floor(now / CheckSeconds) == Math.Floor((now - dt) / CheckSeconds)) return;
        var inSpace = sim.Crew.InSpace.ToDictionary(c => c.Soul.UniqueID);
        TickCuts(run, sim, s, inSpace, now);
        TickStuck(run, sim, s, t, inSpace, now);
    }

    /// <summary>Spec 1611: a cut opens the breach at its time. It stops when its crew member boarded, died or got another home ship, or the ship got a working airlock.</summary>
    static void TickCuts(Run run, SimRoot sim, BreachState s, Dictionary<ObjectID, SpaceCrew> inSpace, double now)
    {
        foreach (var cut in s.Cuts.ToList())
        {
            var ship = sim.Ships[cut.Ship];
            if (!inSpace.TryGetValue(cut.Crew, out var crew) || ship is null || crew.Soul.HomeShip != ship || HasWorkingAirlock(ship))
            {
                s.Cuts.Remove(cut);
                Log.Info($"breach: the cut into {ship?.Metadata.PlainShipName ?? "a ship that is gone"} stops");
                continue;
            }
            if (now < cut.Due) continue;
            s.Cuts.Remove(cut);
            bool placed = Place(ship, cut.Cell, cut.Walk);
            if (!placed && Site(ship, crew.DetWorldLocation, run.T.Breach.MaxSites) is { } other) placed = Place(ship, other.Cell, other.Walk);
            if (!placed)
            {
                // The site went away during the cut (a part was built there). The crew member waits again.
                s.Stuck[cut.Crew] = now;
                Log.Info($"breach: no site takes a breach on {ship.Metadata.PlainShipName}; the crew member waits again");
                continue;
            }
            s.Made++;
            foreach (var c in inSpace.Values.Where(c => c.Soul.HomeShip == ship)) s.Stuck.Remove(c.Soul.UniqueID);
            Log.Info($"breach: {crew.Soul.Name} cut a breach into {ship.Metadata.PlainShipName}");
            if (ship.IsHumanPlayerShip) Ux.UxKit.ChatLine(run.Game, Text("DoneFmt", crew.Soul.Name, ship.Metadata.XmlShipName));
        }
    }

    /// <summary>Spec 1610: crew in space near a home ship with no working airlock wait; after WaitSeconds the first of them starts to cut.</summary>
    static void TickStuck(Run run, SimRoot sim, BreachState s, BreachTunables t, Dictionary<ObjectID, SpaceCrew> inSpace, double now)
    {
        foreach (var id in s.Stuck.Keys.Where(id => !inSpace.ContainsKey(id)).ToList()) s.Stuck.Remove(id);
        foreach (var crew in sim.Crew.InSpace)
        {
            var id = crew.Soul.UniqueID;
            var ship = crew.Soul.HomeShip;
            bool waits = ship is not null && !ship.IsJunk && !HasWorkingAirlock(ship) && s.Cuts.All(c => c.Ship != ship.UniqueID)
                && ship.DetWorldBoundingCircle.DistanceTo(crew.DetWorldLocation) <= t.Range;
            if (!waits)
            {
                s.Stuck.Remove(id);
                continue;
            }
            if (!s.Stuck.TryGetValue(id, out double since))
            {
                s.Stuck[id] = now;
                continue;
            }
            if (now - since < t.WaitSeconds) continue;
            if (Site(ship!, crew.DetWorldLocation, t.MaxSites) is not { } site)
            {
                s.Stuck[id] = now;
                Log.Info($"breach: no breach site on {ship!.Metadata.PlainShipName}; the crew member waits again");
                continue;
            }
            s.Stuck.Remove(id);
            s.Cuts.Add(new BreachCut { Crew = id, Ship = ship!.UniqueID, Cell = site.Cell, Walk = site.Walk, Due = now + t.CutSeconds });
            Log.Info($"breach: {crew.Soul.Name} starts to cut into {ship.Metadata.PlainShipName} at {site.Cell}");
            if (ship.IsHumanPlayerShip) Ux.UxKit.ChatLine(run.Game, Text("StartFmt", crew.Soul.Name, ship.Metadata.XmlShipName));
        }
    }

    static bool Walkable(Part? part) => part is not null && !part.IsDestroyed && part.Rules.CrewSpeedFactor.IsNonZero;

    /// <summary>
    /// Spec 1612: the breach site nearest to a point. A site is a cell next to a walkable tile of the ship (crew speed above 0) that is empty
    /// or holds a part that is not walkable, where the breach part fits. It takes the nearest site of the whole ship where both sides allow
    /// the door: a forced door is invalid in the design (the ship editor shows it red). Only a ship with no such site gets a forced door, at
    /// the nearest of the MaxSites nearest sites where the part fits. Sim state only; ties go by cell.
    /// </summary>
    internal static (IntVector2 Cell, IntVector2 Walk)? Site(Ship ship, Vector2 near, int maxSites)
    {
        var sites = new List<(IntVector2 Cell, IntVector2 Walk, float D)>();
        foreach (var w in ship.Parts.Where(Walkable))
            foreach (var walk in w.PhysicalRect)
                foreach (var side in Sides)
                {
                    var cell = walk + side;
                    if (Walkable(ship.Parts[cell])) continue;
                    sites.Add((cell, walk, ship.DetTransformPointToWorld(new Vector2(cell.X + 0.5f, cell.Y + 0.5f)).DistanceSquaredTo(near)));
                }
        var ordered = sites.OrderBy(x => x.D).ThenBy(x => x.Cell.X).ThenBy(x => x.Cell.Y).ThenBy(x => x.Walk.X).ThenBy(x => x.Walk.Y).ToList();
        foreach (var (cell, walk, _) in ordered)
            if (Fit(ship, cell, walk, force: false) is not null) return (cell, walk);
        foreach (var (cell, walk, _) in ordered.Take(maxSites))
            if (Fit(ship, cell, walk, force: true) is not null) return (cell, walk);
        return null;
    }

    /// <summary>The first rotation at which the breach part and its door fit at the site, or null. Nothing changes.</summary>
    static int? Fit(Ship ship, IntVector2 cell, IntVector2 walk, bool force)
    {
        for (int rot = 0; rot < 4; rot++)
        {
            using var info = Plan(ship, cell, walk, rot, force);
            if (info is not null) return rot;
        }
        return null;
    }

    /// <summary>The update that puts the breach part and its door at the site with this rotation, or null when it does not fit.</summary>
    static ShipUpdateInfo? Plan(Ship ship, IntVector2 cell, IntVector2 walk, int rot, bool force)
    {
        var rules = ship.Rules.PartsByID[PartId];
        var door = ship.Rules.DefaultDoor ?? throw new InvalidOperationException($"ship rules {ship.Rules.ID} have no default door");
        var (doorCell, horizontal) = BreachModel.Door((cell.X, cell.Y), (walk.X, walk.Y));
        var loc = new DoorLocation(new IntVector2(doorCell.X, doorCell.Y), horizontal ? DoorOrientation.Horizontal : DoorOrientation.Vertical);
        // The game's door check (BlueprintDoorsManager.CanAddDoorAt) sees the part that the breach replaces (armor, or no part), not the breach.
        // So the door is checked here with the rule of the door validity (BlueprintDoor.UpdateValidity) for the breach, and added past the
        // game's check. A walkable tile with no blueprint part (not built from the design) allows no valid door.
        var other = ship.BlueprintParts[walk];
        bool breachFirst = loc.Cell == cell;
        bool allowed = other is not null && PartRules.AllowsDoorBetween(loc,
            breachFirst ? rules : other.Rules, breachFirst ? cell : other.Location, breachFirst ? rot : other.Rotation, !breachFirst && other.FlipX,
            breachFirst ? other.Rules : rules, breachFirst ? other.Location : cell, breachFirst ? other.Rotation : rot, breachFirst && other.FlipX);
        if (!allowed && !force) return null;
        var info = ShipUpdateInfo.Alloc(ship);
        if (info.TryAddBlueprintPart(rules, cell, rot, flipX: false, mirrorPhysical: true, allowOverwrite: true, addDoors: false, hireCrew: false, out _)
            && info.TryAddBlueprintDoor(door, loc, mirrorPhysical: true, allowOverwrite: true, force: true))
            return info;
        ((IDisposable)info).Dispose();
        return null;
    }

    /// <summary>
    /// Spec 1613: puts the breach into the ship's design and its physical parts, at no cost: it replaces what is at the cell, and a door
    /// links it to the walkable tile. Returns false when it does not fit (any more).
    /// </summary>
    internal static bool Place(Ship ship, IntVector2 cell, IntVector2 walk)
    {
        foreach (bool force in new[] { false, true })
            for (int rot = 0; rot < 4; rot++)
            {
                using var info = Plan(ship, cell, walk, rot, force);
                if (info is null) continue;
                info.Apply(ConstructionMode.InstantFree, updateDestroyed: true);
                return true;
            }
        return false;
    }
}

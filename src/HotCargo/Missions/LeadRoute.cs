using Cosmoteer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// Spec 1890: the Blind Courier's jump point. Of the destinations that fit the convoy route rules, it takes the one whose straight route
/// passes the most encounters, so the players must lead the courier around them. The encounters come from the system itself: every ship
/// hostile to the players (live or in stasis: bases, field pirates, patrols, bounty targets) and every nebula cell, on a grid.
/// </summary>
/// <summary>Spec 1890: the main kind of danger on the Blind Courier's direct route. It picks the mood line of the offer.</summary>
public enum RouteDanger
{
    Quiet,
    Raiders,
    Base,
    RoughSpace,
}

internal static class LeadRoute
{
    /// <summary>The picked destination and the main kind of danger on its direct route, or null when none fits.</summary>
    internal static (Vector2 Destination, RouteDanger Danger)? Pick(SimRoot sim, SpawnedObjectCollection objects, Vector2 start, Vector2 rulesDestination, float sunClearance)
    {
        var candidates = EscortObjective.RouteCandidates(sim, objects, start, rulesDestination, sunClearance);
        if (candidates.Count == 0) return null;
        var t = MissionArea.TunablesFor(sim.RequireGame());
        float reach = candidates.Max(c => c.Length) + t.LeadEncounterRange;
        var encounters = Encounters(sim, t, reach);
        var best = MissionModel.BestEncounterRoute(N(start), [.. candidates.Select(N)], encounters, t.LeadEncounterRange, t.LeadDestinationClear);
        if (best is not { } b)
        {
            Log.Info($"blind courier offer dropped: every one of {candidates.Count} jump points has a hostile ship within {t.LeadDestinationClear:0}");
            return null;
        }
        var danger = MissionModel.MainGroup(b.Past) is { } g ? Enum.Parse<RouteDanger>(g) : RouteDanger.Quiet;
        Log.Info($"blind courier route: {encounters.Count} encounters in the system, {b.Past.Count} near the route of {candidates.Count} candidates: " +
                 $"{string.Join(", ", b.Past.GroupBy(e => e.Group).Select(g => $"{g.Count()} {g.Key}"))}; main danger {danger}");
        return (new Vector2(b.Destination.X, b.Destination.Y), danger);
    }

    /// <summary>The encounters within LeadEncounterRange of the straight route from start to destination now. For screenshots and tests.</summary>
    internal static List<RouteEncounter> Near(SimRoot sim, Vector2 start, Vector2 destination)
    {
        var t = MissionArea.TunablesFor(sim.RequireGame());
        var all = Encounters(sim, t, Math.Max(start.Length, destination.Length) + t.LeadEncounterRange);
        return MissionModel.RouteEncounters(N(start), N(destination), all, t.LeadEncounterRange, clear: 0f)?.Past ?? [];
    }

    /// <summary>
    /// Every hostile ship (Raiders; a hostile station is a Base and weighs more) and every nebula cell (RoughSpace) within
    /// <paramref name="reach"/> of the system center.
    /// </summary>
    static List<RouteEncounter> Encounters(SimRoot sim, MissionTunables t, float reach)
    {
        var humans = sim.RequireGame().HumanPlayersIndexes;
        bool Hostile(int player) => humans.Any(h => sim.Mode.AreEnemies(player, h));
        var stations = SpaceStation.GetStations(sim, onlyOperating: false).Select(s => s.StationID).ToHashSet();
        var found = new List<RouteEncounter>();
        void Add(Cosmoteer.Game.ObjectID id, Vector2 at)
        {
            bool station = stations.Contains(id);
            found.Add(new(N(at), station ? t.LeadEncounterStationWeight : t.LeadEncounterShipWeight, (station ? RouteDanger.Base : RouteDanger.Raiders).ToString(), Blocks: true));
        }
        for (int i = 0; i < sim.Ships.Count; i++)
            if (sim.Ships[i] is { IsJunk: false, IsHumanPlayerShip: false } s && !s.Rules.IsAsteroid && Hostile(s.Metadata.PlayerIndex))
                Add(s.UniqueID, s.DetWorldCenter);
        foreach (var sp in sim.Stasis.OfType<StasisShipSpawner>())
            if (Hostile(sp.ObjectAllegiance)) Add(sp.ReservedObjectID, sp.Location);
        float step = t.LeadNebulaGridStep;
        for (float x = -reach; x <= reach; x += step)
            for (float y = -reach; y <= reach; y += step)
                if (sim.Nebulas.GetDominantInsideType(new Vector2(x, y)) is not null)
                    found.Add(new(new System.Numerics.Vector2(x, y), t.LeadEncounterNebulaWeight, RouteDanger.RoughSpace.ToString(), Blocks: false));
        return found;
    }

    /// <summary>Spec 1890: the mood line of the offer and the departure line, by the main danger. No counts and no names.</summary>
    internal static string Hint(RouteDanger danger) => Strings.GetText($"HotCargoMissions/LeadRoute{danger}");

    static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);
}

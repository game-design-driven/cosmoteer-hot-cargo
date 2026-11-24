using System.Text.Json;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using HotCargo.Core;
using HotCargo.Events;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 1990 to 1993: where the player fleet and the objects of each open mission are, in sim positions (world units; y points down
/// on the screen). Mission objects: the ships that the active objectives name (vanilla Mission.IsShipAssociated: targets, contacts,
/// escorts, item source ships, live or in stasis), loose mission item crates (hand-overs), and the objective areas
/// (Objective.GetAreaLocation: target area markers, rings). Read only.
/// </summary>
static class MissionPositions
{
    /// <summary>At most this many ships per mission, nearest to the fleet first. A reward that any station of a system pays names them all.</summary>
    const int MaxShipsPerMission = 12;

    /// <summary>For autotests: the mission ships, items and areas that the last write named.</summary>
    internal static int LastObjects;

    static void Point(Utf8JsonWriter w, Halfling.Geometry.Vector2 at)
    {
        Tele.Num(w, "x", at.X);
        Tele.Num(w, "y", at.Y);
    }

    /// <summary>"fleet" (centre and each human ship) and "missions" (the open missions of the log, or only the given one).</summary>
    internal static void Write(Utf8JsonWriter w, Run run, SimRoot sim, Mission? only = null)
    {
        var center = SimQueries.FleetCenter(sim);
        w.WriteStartObject("fleet");
        if (center is { } c) { w.WritePropertyName("center"); w.WriteStartObject(); Point(w, c); w.WriteEndObject(); }
        w.WriteStartArray("ships");
        foreach (Ship ship in sim.Ships.HumanPlayerSignificantShips.OrderBy(s => s.UniqueID))
        {
            w.WriteStartObject();
            w.WriteString("ship", ship.UniqueID.ToString());
            w.WriteString("name", ship.Metadata.PlainShipName);
            w.WriteNumber("player", ship.Metadata.PlayerIndex);
            Point(w, ship.DetWorldCenter);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
        var near = center ?? default;
        var candidates = Candidates(sim);
        LastObjects = 0;
        w.WriteStartArray("missions");
        foreach (Mission m in run.Mode.Missions.Where(m => only is null || m == only).Where(m => m.Completion == CompletionState.InProgress))
            WriteMission(w, run, sim, m, candidates, near);
        w.WriteEndArray();
    }

    /// <summary>Every ship of the system that is not a human ship: live ships and ships in stasis.</summary>
    static List<MetaShip> Candidates(SimRoot sim) =>
    [
        .. sim.Ships.Where(s => !s.IsHumanPlayerShip && !s.IsJunk).Select(s => (MetaShip)s),
        .. sim.Stasis.Where(s => s is StasisShipSpawner or SimStasisManager.SerializedStasisShip).Select(s => (MetaShip)s),
    ];

    static void WriteMission(Utf8JsonWriter w, Run run, SimRoot sim, Mission m, List<MetaShip> candidates, Halfling.Geometry.Vector2 near)
    {
        var active = new List<Objective>();
        m.GetObjectives(active, activeOnly: true);
        // Only leaf objectives: a sequence or an any-order group reports the area of its child again.
        active = active.Where(IsLeaf).ToList();
        w.WriteStartObject();
        w.WriteString("mission", m.MissionID.ToString());
        w.WriteString("type", m.MetatypeID.ToString());
        w.WriteString("name", Ux.MissionTitle.Plain(m));
        w.WriteStartArray("objectives");
        foreach (var o in active.Where(o => o.Completion == CompletionState.InProgress)) w.WriteStringValue(o.GetType().Name);
        w.WriteEndArray();
        w.WriteStartArray("ships");
        foreach (var ship in candidates.Where(m.IsShipAssociated).OrderBy(s => s.DetWorldCenter.DistanceTo(near)).ThenBy(s => s.UniqueID).Take(MaxShipsPerMission))
        {
            w.WriteStartObject();
            w.WriteString("ship", ship.UniqueID.ToString());
            if (ship.Ship is { } live)
            {
                w.WriteString("name", live.Metadata.PlainShipName);
                w.WriteString("side", Session.Side(live));
            }
            else w.WriteBoolean("stasis", true);
            w.WriteNumber("player", ship.PlayerIndex);
            if ((ship.Ship?.Tags ?? SimQueries.StasisTags(ship.Spawner!)) is { } tags)
            {
                w.WriteStartArray("tags");
                foreach (var t in tags) w.WriteStringValue(t.ToString());
                w.WriteEndArray();
            }
            Point(w, ship.DetWorldCenter);
            w.WriteEndObject();
            LastObjects++;
        }
        w.WriteEndArray();
        w.WriteStartArray("items");
        foreach (var h in active.OfType<HandOverObjective>())
            foreach (var unit in h.LooseUnits(sim))
            {
                w.WriteStartObject();
                w.WriteString("item", h.ResourceType?.ToString());
                Point(w, unit.DetWorldLocation);
                w.WriteEndObject();
                LastObjects++;
            }
        w.WriteEndArray();
        w.WriteStartArray("areas");
        foreach (var o in active.Where(o => o.Completion == CompletionState.InProgress))
            if (o.GetAreaLocation(run.Game) is { } area)
            {
                w.WriteStartObject();
                w.WriteString("objective", o.GetType().Name);
                Point(w, area.Center);
                Tele.Num(w, "r", area.Radius);
                w.WriteEndObject();
                LastObjects++;
            }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    static bool IsLeaf(Objective o)
    {
        var own = new List<Objective>();
        o.GetObjectives(own);
        return own.Count == 1 && own[0] == o;
    }

    /// <summary>A short text of the active objectives and their states, to see when an objective of a mission changes.</summary>
    internal static string ObjectiveState(Mission m)
    {
        var all = new List<Objective>();
        m.GetObjectives(all);
        return string.Join(",", all.Select(o => (int)o.Completion));
    }
}

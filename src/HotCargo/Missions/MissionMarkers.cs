using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>A Hot Cargo objective that owns target area markers: they exist for this objective and go with its mission.</summary>
internal interface IMissionMarkers
{
    IEnumerable<ObjectID> MarkerIds { get; }
}

/// <summary>
/// Spec 996: a mission's target area markers go when the objective that owns them ends (completed or failed), even while the mission
/// waits for its pay, and at the latest when the mission leaves the log (completed, failed or abandoned).
/// Vanilla removes a bounty marker only when its ships die (AutoRemoveWhenTagDestroyed), so an abandoned bounty kept its marker too.
/// Runs from the mission log's MissionRemoved event, which every client gets at the same sim step (like ChainRunner).
/// Markers in another system cannot go at once (object IDs belong to one system): the run remembers them, and the next arrival in
/// that system removes them (spec 2250).
/// </summary>
internal static class MissionMarkers
{
    internal static readonly ID<DoodadRules> TargetArea = new("cosmoteer.poi_target_area");
    static MissionList? _watched;

    internal static void Register()
    {
        Run.Hooks.Tick += (run, sim, _) =>
        {
            Watch(run);
            Errors.Guard("mission markers sweep", () => Sweep(run, sim));
            Errors.Guard("mission markers withdrawn offers", () => SweepWithdrawn(run, sim));
        };
        Run.Hooks.SystemEntered += (run, _) => Errors.Guard("mission markers arrival", () => OnSystemEntered(run));
    }

    /// <summary>
    /// The markers that the objectives of a mission own: the POIs that vanilla objectives name (<see cref="VanillaPois"/>) and the markers
    /// of Hot Cargo objectives. A map ring that a mission only uses (a belt pass for Hold the Pass) is not in the list.
    /// </summary>
    internal static IEnumerable<ObjectID> Of(Mission m) => Objectives(m).SelectMany(OwnedBy);

    /// <summary>The markers that the objectives still in progress own. An ended objective's markers go (spec 996).</summary>
    internal static IEnumerable<ObjectID> LiveOf(Mission m) =>
        m.Completion == CompletionState.InProgress ? Objectives(m).Where(o => o.Completion == CompletionState.InProgress).SelectMany(OwnedBy) : [];

    static List<Objective> Objectives(Mission m)
    {
        var objectives = new List<Objective>();
        m.GetObjectives(objectives);
        return objectives;
    }

    internal static IEnumerable<ObjectID> OwnedBy(Objective o)
    {
        foreach (var get in VanillaPois(o.GetType()))
            if (get(o) is { } id) yield return id;
        if (o is IMissionMarkers own)
            foreach (var marker in own.MarkerIds) yield return marker;
    }

    static readonly Dictionary<Type, Func<Objective, ObjectID?>[]> PoiGetters = [];

    /// <summary>
    /// The POIs that a vanilla objective type names, read from the type: every public ObjectID? property named ...DoodadID (POIDoodadID of
    /// DefeatShips, ProtectShips, GotoShip, ReceiveRewards) and every ObjectiveTargetDoodad&lt;POIDoodad&gt; property (SuggestedCollectionPOI
    /// of DeliverResources: the Salvage Race ring). Hot Cargo objectives name theirs with <see cref="IMissionMarkers"/>.
    /// </summary>
    static Func<Objective, ObjectID?>[] VanillaPois(Type type)
    {
        lock (PoiGetters)
        {
            if (PoiGetters.TryGetValue(type, out var cached)) return cached;
            var getters = type.Assembly != typeof(Objective).Assembly ? [] : type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Select<System.Reflection.PropertyInfo, Func<Objective, ObjectID?>?>(p =>
                    p.PropertyType == typeof(ObjectID?) && p.Name.EndsWith("DoodadID", StringComparison.Ordinal) ? o => (ObjectID?)p.GetValue(o)
                    : p.PropertyType == typeof(ObjectiveTargetDoodad<POIDoodad>) ? o => (p.GetValue(o) as ObjectiveTargetDoodad<POIDoodad>)?.DoodadID
                    : null)
                .OfType<Func<Objective, ObjectID?>>().ToArray();
            return PoiGetters[type] = getters;
        }
    }

    /// <summary>
    /// Spec 996, sim tick: removes the target area markers of objectives that ended in missions still in the log (a kill done, the mission
    /// waits for its pay; or the whole mission ended and still shows), unless an objective in progress or an offer owns them too. Markers
    /// of another system are not in this sim; they go here on the next arrival, since the objective stays ended.
    /// </summary>
    static void Sweep(Run run, SimRoot sim)
    {
        List<(Mission Mission, ObjectID Id)>? ended = null;
        ObjectID here = sim.NodeId();
        foreach (Mission m in run.Mode.Missions)
        {
            bool missionEnded = m.Completion != CompletionState.InProgress;
            foreach (var o in Objectives(m))
            {
                if (!missionEnded && o.Completion == CompletionState.InProgress) continue;
                // Object IDs belong to one system: an objective of another system has no marker here.
                if (o.GetFirstMapNode(run.Game) is { } node && !node.UniqueID.Equals(here)) continue;
                foreach (var id in OwnedBy(o))
                    if (sim.Doodads[id] is POIDoodad poi && poi.Rules.ID == TargetArea) (ended ??= []).Add((m, id));
            }
        }
        if (ended is null) return;
        var live = run.Mode.Missions.SelectMany(LiveOf).Concat(Contracts.ContractBook.Offers(sim).SelectMany(o => Of(o.Mission))).ToHashSet();
        foreach (var (m, id) in ended.DistinctBy(e => e.Id).OrderBy(e => e.Id))
        {
            if (live.Contains(id) || sim.Doodads[id] is not POIDoodad poi) continue;
            sim.Doodads.Remove(poi, dispose: true);
            Log.Info($"mission {m.MetatypeID} {m.MissionID}: objective ended, marker {id} removed");
        }
    }

    /// <summary>Missions that left a list other than the mission log since the last tick: offers that the players took, or that went away.</summary>
    static readonly List<Mission> Withdrawn = [];

    /// <summary>
    /// Spec 996: vanilla takes an offer out of its list when the offer expires (AIMissionProviderModule: Mission.CheckExpired, for example
    /// when its escort ship died). A taken offer moves into the log, and a provider that enters stasis moves its offers to another list.
    /// One tick later, an offer that is in neither the log nor an offer list went away, and its markers go with it.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(MissionList), nameof(MissionList.RemoveMission))]
    static class OfferRemoved
    {
        static void Postfix(MissionList __instance, Mission m)
        {
            if (Run.Current is { } run && !ReferenceEquals(__instance, run.Mode.Missions)) Withdrawn.Add(m);
        }
    }

    static void SweepWithdrawn(Run run, SimRoot sim)
    {
        if (Withdrawn.Count == 0) return;
        var gone = Withdrawn.Distinct().ToList();
        Withdrawn.Clear();
        var offered = Contracts.ContractBook.Offers(sim).Select(o => o.Mission.MissionID).ToHashSet();
        foreach (var m in gone.Where(m => !run.Mode.Missions.ContainsMission(m) && !offered.Contains(m.MissionID)).OrderBy(m => m.MissionID))
        {
            Log.Info($"mission {m.MetatypeID} {m.MissionID}: offer withdrawn (expired {m.CheckExpired()})");
            RemoveOf(run, sim, m, "offer withdrawn");
        }
    }

    /// <summary>Subscribes to the mission log of the current run. A new or loaded game has a new log.</summary>
    static void Watch(Run run)
    {
        if (_watched == run.Mode.Missions) return;
        if (_watched is not null) _watched.MissionRemoved -= OnMissionRemoved;
        _watched = run.Mode.Missions;
        _watched.MissionRemoved += OnMissionRemoved;
    }

    static void OnMissionRemoved(object? sender, Mission m) => Errors.Guard("mission markers", () =>
    {
        if (Run.Current is not { } run || !ReferenceEquals(sender, run.Mode.Missions) || run.Game.Sim is not { } sim) return;
        RemoveOf(run, sim, m, "left the log");
        Remember(run, sim, m);
    });

    /// <summary>
    /// Spec 2250: the markers of a mission that ended while its objectives are in another system. An objective's markers lie in its
    /// first map node; the next arrival there removes them.
    /// </summary>
    static void Remember(Run run, SimRoot sim, Mission m)
    {
        ObjectID here = sim.NodeId();
        foreach (var o in Objectives(m))
        {
            var ids = OwnedBy(o).ToList();
            if (ids.Count == 0 || o.GetFirstMapNode(run.Game)?.UniqueID is not { } node || node.Equals(here)) continue;
            if (!run.State.EndedMarkers.TryGetValue(node, out var set)) run.State.EndedMarkers[node] = set = [];
            set.UnionWith(ids);
            Log.Info($"mission {m.MetatypeID} {m.MissionID} left the log in another system: {ids.Count} markers wait for the next arrival there");
        }
    }

    /// <summary>Spec 2250: removes the remembered markers of this system, unless a mission in progress or an offer owns them now.</summary>
    static void OnSystemEntered(Run run)
    {
        if (run.Game.Sim is not { } sim || !run.State.EndedMarkers.Remove(sim.NodeId(), out var ids)) return;
        var owned = run.Mode.Missions.Where(o => o.Completion == CompletionState.InProgress)
            .Concat(Contracts.ContractBook.Offers(sim).Select(o => o.Mission)).SelectMany(Of).ToHashSet();
        foreach (var id in ids.OrderBy(i => i))
        {
            if (owned.Contains(id) || sim.Doodads[id] is not POIDoodad poi || poi.Rules.ID != TargetArea) continue;
            sim.Doodads.Remove(poi, dispose: true);
            Log.Info($"arrival: marker {id} of an ended mission removed");
        }
    }

    /// <summary>
    /// Removes the markers of a mission that ended or of an offer that went away (a dismissed rival, spec 103), unless another mission
    /// in progress or offer owns them. Call it from sim code only.
    /// </summary>
    internal static void RemoveOf(Run run, SimRoot sim, Mission m, string why)
    {
        // Object IDs belong to one system: a marker of a mission in another system is not in this sim, so nothing goes.
        var others = run.Mode.Missions.Where(o => o.MissionID != m.MissionID && o.Completion == CompletionState.InProgress)
            .Concat(Contracts.ContractBook.Offers(sim).Select(o => o.Mission).Where(o => o.MissionID != m.MissionID))
            .SelectMany(Of).ToHashSet();
        foreach (var id in Of(m).Distinct())
        {
            if (others.Contains(id) || sim.Doodads[id] is not POIDoodad poi || poi.Rules.ID != TargetArea) continue;
            sim.Doodads.Remove(poi, dispose: true);
            Log.Info($"mission {m.MetatypeID} {m.MissionID} {why}: marker {id} removed");
        }
    }
}

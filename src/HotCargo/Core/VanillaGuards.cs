using Halfling.Geometry;
using Halfling.Pathfinding;
using HarmonyLib;

namespace HotCargo.Core;

/// <summary>
/// Guards against vanilla crashes that Hot Cargo features can reach (spec 17, Rules). Each guard has a narrow target, turns the
/// crash into a skipped call, and reports through Errors: an ERROR line that fails every test, and the overlay in play.
/// The cause is then still a bug to fix: the guard only keeps the game of the players alive.
/// </summary>
public static class VanillaGuards
{
    /// <summary>Calls that a guard skipped in this game session. For autotests.</summary>
    public static int Hits => _hits;
    static int _hits;

    /// <summary>Test only: the next skipped call is on purpose. It counts in Hits and logs no ERROR.</summary>
    internal static bool ExpectHit;

    internal static void Register()
    {
        Dev.Autotest.Fields["vanilla_guard_hits"] = () => Hits;
        // vg_search_outside: searches for crew of the first player ship from a tile far outside its crew path grid, as a job with
        // a bad search origin does. Without the guard the game crashes.
        Dev.Autotest.Commands["vg_search_outside"] = _ =>
        {
            var ship = Dev.Autotest.RequireRun().Game.Sim.Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).First();
            ExpectHit = true;
            int found = ship.Paths.SearchCrewFrom(new IntRect(5000, 5000, 1, 1), useTraffic: true).Count();
            if (found != 0) throw new InvalidOperationException($"the search found {found} crew");
        };
        // vg_homeless_pathless: one crew member of the first player ship loses its home ship, takes a side that is not human and
        // stands on a part with no crew path (armor, a thruster), as crew of a hunter that the players took over did (issue 350). Then
        // the homeless job asks for its next destination, and the invariant homeless_crew runs. Without the guard the game crashes.
        // The crew member then gets its home, side and place back.
        Dev.Autotest.Commands["vg_homeless_pathless"] = _ =>
        {
            var sim = Dev.Autotest.RequireRun().Game.Sim;
            var ship = sim.Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).First();
            var part = ship.Parts.OrderBy(p => p.Location.Y).ThenBy(p => p.Location.X).FirstOrDefault(p => p.ContiguousPathSets is not { Count: > 0 })
                ?? throw new InvalidOperationException($"{ship.Metadata.PlainShipName} has no part without a crew path");
            var crew = ship.Crew.OnShipAssigned.OrderBy(c => c.Soul.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException($"{ship.Metadata.PlainShipName} has no crew aboard");
            var (soul, place, side) = (crew.Soul, crew.Location, crew.Soul.PlayerIndex);
            ship.Crew.UnassignCrew(soul, killed: false);
            soul.PlayerIndex = Cosmoteer.PlayerIndexes.Neutral;
            crew.SetLocation(part.LocalCenter, lerpFromPrevious: false, queueGeometryRefresh: true, clearPath: true);
            crew.UpdateCurrentPart();
            try
            {
                if (crew.CurrentPart != part) throw new InvalidOperationException($"the crew member stands on {crew.CurrentPart?.Rules.ID}, not on {part.Rules.ID}");
                ExpectHit = true;
                var to = sim.Crew.HomelessJob.GetDestination(soul);
                if (ExpectHit) throw new InvalidOperationException("the guard did not skip the call");
                if (to.Part != part) throw new InvalidOperationException($"the guard sent the crew member to {to.Part?.Rules.ID}, not to its own part");
                Dev.Invariants.HomelessCrew(sim);
            }
            finally
            {
                soul.PlayerIndex = side;
                ship.Crew.AssignCrew(soul);
                crew.SetLocation(place, lerpFromPrevious: false, queueGeometryRefresh: true, clearPath: true);
                crew.UpdateCurrentPart();
            }
        };
    }

    static void Hit(string what, string message)
    {
        // Crew jobs can run on worker threads of the sim (parallel fixed update buckets).
        Interlocked.Increment(ref _hits);
        if (ExpectHit) { ExpectHit = false; Log.Info($"vanilla guard {what} skipped a call as the test expects: {message}"); return; }
        Errors.Report($"vanilla guard {what}", message);
    }

    /// <summary>
    /// Vanilla 0.30.5a: a grid search that starts outside the grid writes outside its arrays (OpenNodeQueue.Enqueue) and the game
    /// crashes with IndexOutOfRangeException. The crew path grid of a ship covers only its walkable tiles and a margin
    /// (PathManager.EnlargeToFit), so a crew job whose search origin (Job.GetSearchOrigin) is a tile far from them crashed the game
    /// (issue 346: a weld joint on armor). Every search of PathManager (crew, cells, parts) starts here. A start outside the grid
    /// reaches no cell, so the search returns nothing. Every co-op machine skips the same call.
    /// </summary>
    [HarmonyPatch]
    static class GridSearchStart
    {
        /// <summary>Both overloads put the start cells into the open queue the same way.</summary>
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var searches = AccessTools.GetDeclaredMethods(typeof(AStarGridPathfinder)).Where(m => m.Name == "FindAllNodeDistancePairs").ToList();
            if (searches.Count != 2 || searches.Any(m => m.GetParameters()[0].ParameterType != typeof(IntRect)))
                throw new MissingMethodException("AStarGridPathfinder.FindAllNodeDistancePairs changed: check the guard against the new game code");
            return searches;
        }

        static bool Prefix(AStarGridPathfinder __instance, IntRect start, ref IEnumerable<(IntVector2 Cell, float Distance)> __result)
        {
            var (width, height) = __instance._costs is { } costs ? (costs.Height, costs.Depth)
                : __instance._adjacency is { } adjacency ? (adjacency.Width, adjacency.Height)
                : throw new InvalidOperationException("the pathfinder has no grid");
            var grid = new IntRect(-__instance.XOffset, -__instance.YOffset, width, height);
            if (grid.Contains(in start)) return true;
            __result = [];
            // The callers, so the line names the job or the feature that asked.
            var callers = new System.Diagnostics.StackTrace(2, false).GetFrames().Take(8)
                .Select(f => f.GetMethod() is { } m ? $"{m.DeclaringType?.Name}.{m.Name}" : "?");
            Hit("grid search", $"a grid search starts at {start}, outside the grid {grid}; it finds nothing. Callers: {string.Join(" < ", callers)}");
            return false;
        }
    }

    /// <summary>
    /// Vanilla 0.30.5a: HomelessJob.GetDestination sends a homeless crew member on a ship to a random part of its crew path area
    /// (Rand.SelectElement of PathContiguityManager.SearchPartsFrom) when no airlock connects to it and the game does not cull it
    /// (SimModeManager.AllowCullHomelessCrew: human crew, and crew of other sides on a live ship). On a part with no crew path (armor,
    /// a thruster, or a part just built or changed) that list is empty, and Rand.Int32(0, -1) crashed the game (issue 350: crew of a
    /// hunter that the players took over). The guard sends the crew member to its own part: it waits there and asks again on the next
    /// update. It reads only the ship and draws no random number, so every co-op machine skips the same call.
    /// </summary>
    [HarmonyPatch(typeof(Cosmoteer.Ships.Crew.Jobs.HomelessJob), nameof(Cosmoteer.Ships.Crew.Jobs.HomelessJob.GetDestination))]
    static class HomelessDestination
    {
        static bool Prefix(Cosmoteer.Crew.CrewSoul crew, ref Cosmoteer.Crew.Destination __result)
        {
            // The same conditions as vanilla, in the same order. Any other case runs vanilla unchanged.
            if (crew.ShipCrew is not { CurrentPart: { Ship: { } ship } part } shipCrew || crew.Sim is not { } sim
                || Cosmoteer.Ships.Parts.Crew.Airlock.HasContiguousWith(part, shipCrew.CurrentCell)
                || sim.Mode.AllowCullHomelessCrew(crew) || ship.PathContiguity.SearchPartsFrom(part).Any())
                return true;
            __result = new Cosmoteer.Crew.Destination(part);
            Hit("homeless destination", $"homeless crew {crew.Name} of player {crew.PlayerIndex} on {ship.Metadata.PlainShipName} " +
                $"(player {ship.Metadata.PlayerIndex}) stands on {part.Rules.ID}, which has no crew path; it waits there");
            return false;
        }
    }
}

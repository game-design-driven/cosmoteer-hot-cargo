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
    public static int Hits { get; private set; }

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
    }

    static void Hit(string what, string message)
    {
        Hits++;
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
}

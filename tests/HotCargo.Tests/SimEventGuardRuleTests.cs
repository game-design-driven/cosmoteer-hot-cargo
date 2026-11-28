using System.Runtime.CompilerServices;
using Xunit;

namespace HotCargo.Tests;

/// <summary>
/// Mod code subscribes to a sim event only through Errors.Guarded (src/HotCargo/Core/Errors.cs). Vanilla raises these events with no
/// catch (EventHub.OnTick from SimRoot.FixedUpdate), so a throw in the handler ends the game.
/// A line passes with the comment "// guard: &lt;reason&gt;" on it, for a handler that guards each of its own steps.
/// Test code (*Autotest*.cs) is out of the rule: a throw there fails its test, which is what a test needs.
/// </summary>
public class SimEventGuardRuleTests
{
    /// <summary>
    /// The sim events: every EventHub event, a target ship's Killed, the system switch, the mission list, the sim's ships and crew.
    /// Limit: event names, not types, because the tests do not load the game assemblies. Revisit when the mod subscribes to another
    /// vanilla event that the sim raises.
    /// </summary>
    static readonly string[] EventEnds = [".Killed", ".SwitchedSimulation", ".MissionAdded", ".MissionRemoved", ".CrewKilled", "Ships.Added"];

    static string Source([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "HotCargo"));

    /// <summary>A subscription (`+=`) to a sim event that does not go through Errors.Guarded and has no guard comment.</summary>
    static bool Unguarded(string line)
    {
        int at = line.IndexOf(" += ", StringComparison.Ordinal);
        if (at < 0 || line.Contains("// guard:", StringComparison.Ordinal)) return false;
        string target = line[..at].TrimEnd();
        bool simEvent = target.Contains("EventHub.", StringComparison.Ordinal) || EventEnds.Any(e => target.EndsWith(e, StringComparison.Ordinal));
        return simEvent && !line[at..].Contains("Errors.Guarded<", StringComparison.Ordinal);
    }

    [Fact]
    public void Sim_event_handlers_run_inside_Errors_Guarded()
    {
        var root = Source();
        string sep = Path.DirectorySeparatorChar.ToString();
        var found = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}") && !Path.GetFileName(f).Contains("Autotest", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (line, i)).Where(x => Unguarded(x.line))
                .Select(x => $"{Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/')}:{x.i + 1}: {x.line.Trim()}"))
            .ToList();
        Assert.True(found.Count == 0, "Subscribe through Errors.Guarded:\n" + string.Join("\n", found));
    }

    [Theory]
    [InlineData("game.EventHub.Tick += OnTick;", true)]
    [InlineData("foreach (var a in Ambushers) a.Killed += OnAmbusherKilled;", true)]
    [InlineData("sim.Ships.Added += OnShipAdded;", true)]
    [InlineData("_log.MissionRemoved += OnMissionRemoved;", true)]
    [InlineData("game.EventHub.Tick += _tick ??= Errors.Guarded<SimRoot>(\"mayday tick\", OnTick);", false)]
    [InlineData("game.EventHub.Tick += run.OnTick; // guard: each step inside is guarded", false)]
    [InlineData("game.EventHub.Tick -= OnTick;", false)]
    [InlineData("s.VisitCrewKilled += newlyKilled;", false)]
    [InlineData("b.Clicked += OnClicked;", false)]
    public void An_unguarded_subscription_is_a_sim_event_with_no_Errors_Guarded(string line, bool unguarded) =>
        Assert.Equal(unguarded, Unguarded(line));
}

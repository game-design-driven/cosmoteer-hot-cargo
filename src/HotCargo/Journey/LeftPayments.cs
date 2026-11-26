using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using HotCargo.Core;

namespace HotCargo.Journey;

/// <summary>
/// Spec 2567: on the run map, mission pay that waits at a station of the system (spec 2252) is lost when the players jump away by relay,
/// like the tech that stays (spec 2522): the run moves forward. Sim logic: the jump switch records the missions on every machine, and the
/// arrival removes them.
/// </summary>
public static class LeftPayments
{
    /// <summary>Missions to drop at the arrival, set when a relay jump starts. Not saved: a save never holds a jump.</summary>
    static (Run Run, ObjectID[] Missions)? _leaving;

    internal static void Register() => Run.Hooks.SystemEntered += OnSystemEntered;

    /// <summary>Spec 2567: missions in progress whose active step is a payment at a station or system here, in mission ID order.</summary>
    internal static List<Mission> Here(Run run)
    {
        var here = run.Game.CurrentMapNode;
        var list = new List<Mission>();
        foreach (var m in run.Mode.Missions.OrderBy(m => m.MissionID))
        {
            if (m.Completion != CompletionState.InProgress) continue;
            var active = new List<ReceiveRewardsObjective>();
            foreach (var o in m.Objectives) o.GetObjectives(active, activeOnly: true);
            if (active.Any(p => p.Completion == CompletionState.InProgress && p.GetFirstMapNode(run.Game) == here)) list.Add(m);
        }
        return list;
    }

    /// <summary>Spec 2567: a relay jump (not the switch to own ships) starts. Called from the jump switch on every machine.</summary>
    internal static void OnSwitch(Run run, bool relayJump) =>
        _leaving = relayJump && Lockdown.Active(run) ? (run, Here(run).Select(m => m.MissionID).ToArray()) : null;

    static void OnSystemEntered(Run run, bool firstVisit)
    {
        if (_leaving is not { } l || l.Run != run) return;
        _leaving = null;
        foreach (var id in l.Missions)
        {
            if (run.Mode.Missions[id] is not { } m) continue;
            Log.Info($"payments left behind: {m.Name.Text} ends without pay");
            run.Mode.Missions.RemoveMission(m);
        }
    }
}

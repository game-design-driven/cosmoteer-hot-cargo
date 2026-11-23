using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Ships;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.ShipAi;
using HotCargo.Tactics;

namespace HotCargo.Ai;

/// <summary>
/// Spec 1920-1927. Enemies learn the players' habits: a ship that calls for help or flees reports what it saw to its personality
/// group, and later ships of that group come prepared. Run state only (RunState.AiReports), changed in the sim tick.
/// </summary>
public static class AiLearn
{
    public const string ReportEvent = "report", PreparedEvent = "prepared";

    static TacticsTunables? Gate() => Run.Current is { } run && run.T.Tactics.LearnEnabled ? run.T.Tactics : null;

    /// <summary>Spec 1924: the habits that the ship's group reported, or none.</summary>
    public static Habits Prepared(Ship ship) =>
        Gate() is not null && Run.Current!.State.AiReports.For(PersonalityStrategy.Group(ship)) is { } report ? report.Habits : default;

    /// <summary>Spec 1924: the part kind that counts as a weak part for the ship's group. Core parts are weak parts already.</summary>
    public static PartKind? FocusPartKind(Ship ship) => Prepared(ship).Focus switch
    {
        FocusKind.Weapons => PartKind.Weapon,
        FocusKind.Thrusters => PartKind.Thruster,
        _ => null,
    };

    /// <summary>
    /// Spec 1922, 1925: the ship reports what it saw (on a completed call or when it starts to flee). The habits join its group's
    /// report, which becomes new again. With a player sensor array in reach, every player sees what was reported.
    /// </summary>
    public static void Report(Ship ship, string why)
    {
        if (Gate() is not { } t || ship.AI?.GetStrategyModule<CounterKiteStrategy>() is not { } module) return;
        var seen = module.Observed(t);
        if (!seen.Any) return;
        var reports = Run.Current!.State.AiReports;
        string group = PersonalityStrategy.Group(ship);
        var report = reports.For(group);
        if (report is null) reports.Groups.Add(report = new GroupReport { Group = group });
        var merged = report.Habits.Merge(seen);
        (report.Kite, report.Cloud, report.Focus, report.Age) = (merged.Kite, merged.Cloud, merged.Focus is { } f ? (int)f : -1, 0);
        TacticsLog.Event(ship, ReportEvent, $"{why}: {group} learns {seen}");
        if (CallStrategy.Interceptor(ship, t) is { } sensor)
            AiText.ChatAll(ship, "ReportFmt", sensor.Part.Rules.NameKey.Text, GroupName(group), HabitsText(seen));
    }

    /// <summary>
    /// Spec 1925: the first time in a system that a prepared ship has a player ship on its radar, every player with a sensor array
    /// part sees what its group knows. Once per group and system.
    /// </summary>
    public static void Notice(Ship ship)
    {
        if (Gate() is not { } t || Run.Current is not { } run) return;
        string group = PersonalityStrategy.Group(ship);
        if (run.State.AiReports.Noticed.Contains(group) || run.State.AiReports.For(group) is not { } report || !report.Habits.Any) return;
        if (CallStrategy.PlayerInterceptPart(HunterOrders.Sim(ship), t) is not { } part) return;
        run.State.AiReports.Noticed.Add(group);
        TacticsLog.Event(ship, PreparedEvent, $"{group} comes prepared: {report.Habits}");
        AiText.ChatAll(ship, "PreparedFmt", part.Rules.NameKey.Text, GroupName(group), HabitsText(report.Habits));
    }

    /// <summary>Spec 1923: each jump makes every report one jump older and drops the faded ones. Notices start again.</summary>
    public static void OnSystemEntered(Run run)
    {
        var reports = run.State.AiReports;
        foreach (var report in reports.Groups) report.Age++;
        reports.Groups.RemoveAll(r => LearnModel.Faded(r.Age, run.T.Tactics.ReportJumps));
        reports.Noticed.Clear();
    }

    static string GroupName(string group) => group switch
    {
        PersonalityStrategy.Hunters => AiText.Text("GroupHunters"),
        PersonalityStrategy.Pirates => AiText.Text("GroupPirates"),
        _ => GameApp.Rules.GetFactionRules(new ID<FactionRules>(group)).NameKey.Text,
    };

    static string HabitsText(Habits h)
    {
        var parts = new List<string>();
        if (h.Kite) parts.Add(AiText.Text("HabitKite"));
        if (h.Cloud) parts.Add(AiText.Text("HabitCloud"));
        if (h.Focus is { } f) parts.Add(AiText.Text($"Habit{f}"));
        return string.Join("; ", parts);
    }
}

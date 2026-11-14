using Cosmoteer.Game;
using Cosmoteer.Input;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Halfling.Gui;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// Spec 1129: a left-click on a mission's name in the Nearby Missions list (vanilla MissionsHud) opens the mission log with that
/// mission selected. The log shows the vanilla mission info panel, the same panel as the station's offer screen, without Accept.
/// Everything is vanilla: the log button's handler opens the log, the missions tab and the item selection are the vanilla widgets.
/// GUI only: no sim state changes.
/// </summary>
internal static class MissionDetails
{
    static WeakReference<MissionLogTab>? _logTab;

    /// <summary>Opens the mission log on the missions tab with the mission selected, as clicks on the log button and the item do.</summary>
    internal static void Open(GameRoot game, Mission m)
    {
        var mode = (CareerGameModeManager)game.Mode;
        // The log button's handler toggles the window, so call it only while the log is closed.
        if (mode._missionLog is not { SelfActive: true }) mode.OnMissionLogClicked(null, EventArgs.Empty);
        var window = mode._missionLog ?? throw new InvalidOperationException("the mission log did not open");
        game.Gui.FloatingWindows.Children.RaiseToFront(window);
        var tabs = window.Children.OfType<TabBox>().FirstOrDefault() ?? throw new InvalidOperationException("the mission log has no tabs");
        var missionsTab = tabs.TabsListBox.Children.FirstOrDefault() ?? throw new InvalidOperationException("the mission log has no missions tab");
        missionsTab.IsSelected = true;
        if (_logTab?.TryGetTarget(out var tab) != true) throw new InvalidOperationException("the mission log built no missions tab");
        var panel = tab!._missionsPanel;
        // "This system only" hides missions in other systems. The Nearby Missions list shows them too, so clear it, as its click does.
        if (!panel._missionItems.ContainsKey(m) && panel._localOnly is { IsSelected: true } localOnly) localOnly.IsSelected = false;
        if (!panel._missionItems.TryGetValue(m, out var item)) throw new InvalidOperationException($"the mission log does not list {m.MetatypeID} {m.MissionID}");
        item.IsSelected = true;
        Log.Info($"ux: mission {m.MetatypeID} {m.MissionID} opened in the mission log");
    }

    /// <summary>Keeps the missions tab of the log. Vanilla builds it when the log window opens.</summary>
    [HarmonyPatch(typeof(MissionLogTab), MethodType.Constructor, typeof(Halfling.Gui.ScrollBox), typeof(GameRoot))]
    static class LogTabBuilt
    {
        static void Postfix(MissionLogTab __instance) => _logTab = new(__instance);
    }

    /// <summary>The name row of each mission in the Nearby Missions list gets a left-click. Its right-click menu stays as it is.</summary>
    [HarmonyPatch(typeof(MissionsHud.MissionWidget), MethodType.Constructor, typeof(GameRoot), typeof(Mission))]
    static class NameClick
    {
        static void Postfix(MissionsHud.MissionWidget __instance, GameRoot game, Mission mission) => Errors.Guard("mission details click", () =>
        {
            var nameRow = __instance.Children.FirstOrDefault() ?? throw new InvalidOperationException("the mission widget has no name row");
            nameRow.ConfigureAltClick(Inputs.General.UIClick, (_, _) => Errors.Guard("mission details open", () =>
            {
                if (UxKit.On(s => s.MissionDetails)) Open(game, mission);
            }));
        });
    }

    /// <summary>Test only: 1 while the open mission log shows this mission.</summary>
    internal static bool Shows(GameRoot game, Mission m) =>
        ((CareerGameModeManager)game.Mode)._missionLog is { SelfActive: true } && _logTab?.TryGetTarget(out var tab) == true && tab._missionsPanel.SelectedMission == m;
}

using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Input;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling.Geometry;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;
using Halfling.Pooling;
using HarmonyLib;
using HotCargo.Core;
using System.Runtime.CompilerServices;

namespace HotCargo.Ux;

/// <summary>
/// Spec 1128: a left-click on a mission's marker (its indicator, in space or on the system map) or inside its area in space highlights
/// that mission in the Nearby Missions list (vanilla MissionsHud). Vanilla has no selection in that list, so the highlight is the
/// vanilla selected list item background, and the mission counts as prioritized (vanilla PrioritizeOnHud) so that the list shows it.
/// GUI only: the highlight is local to each player and changes no sim state.
/// </summary>
internal static class MissionHighlight
{
    static WeakReference<Mission>? _current;

    /// <summary>The highlighted mission while it is in the log and in progress, else null.</summary>
    internal static Mission? Current =>
        _current?.TryGetTarget(out var m) == true && m.Completion == CompletionState.InProgress && Run.Current?.Mode.Missions.ContainsMission(m) == true ? m : null;

    internal static void Set(Mission m, string how)
    {
        if (Current == m || !UxKit.On(s => s.MissionHighlight)) return;
        _current = new(m);
        Log.Info($"ux: mission {m.MetatypeID} {m.MissionID} highlighted ({how})");
    }

    /// <summary>The mission that vanilla associates with a marker (tracked first, MissionManager.GetMissionAssociation).</summary>
    internal static void FromMarker(POIDoodad poi)
    {
        if (Run.Current is not { } run) return;
        if (run.Mode.Missions.GetMissionAssociation(poi, out var m) != MissionAssociation.None && m is not null) Set(m, "marker");
    }

    /// <summary>The mission in progress whose objective area holds the point: a tracked mission first, then the smallest area.</summary>
    internal static void FromPoint(Run run, Vector2 point)
    {
        var hit = run.Mode.Missions.Where(m => m.Completion == CompletionState.InProgress)
            .SelectMany(m => m.Objectives.Select(o => (Mission: m, Area: o.GetAreaLocation(run.Game))))
            .Where(x => x.Area is { } c && c.Contains(in point))
            .OrderByDescending(x => x.Mission.IsTracked).ThenBy(x => x.Area!.Value.Radius).ThenBy(x => x.Mission.MissionID)
            .Select(x => x.Mission).FirstOrDefault();
        if (hit is not null) Set(hit, "area");
    }

    /// <summary>A click on a marker's indicator. Vanilla focuses the camera on the marker first.</summary>
    [HarmonyPatch(typeof(POIDoodad.POIIndicatorSource), nameof(POIDoodad.POIIndicatorSource.OnClicked))]
    static class MarkerClick
    {
        static void Postfix(POIDoodad.POIIndicatorSource __instance) => Errors.Guard("mission highlight marker", () => FromMarker(__instance.Doodad));
    }

    /// <summary>
    /// A left-click in space. Vanilla handles the ship selection as before; after it, a click that hit no ship inside a mission area
    /// highlights that mission.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInputHandler), "InputSelectShipsAndParts")]
    static class AreaClick
    {
        static void Prefix(PlayerInputHandler __instance, bool focusMode, out bool __state) =>
            __state = !focusMode && Inputs.Game.Selection.WasReleased && __instance._camRelSelectionStart.HasValue && __instance._pressedToSelect;

        static void Postfix(PlayerInputHandler __instance, bool __state) => Errors.Guard("mission highlight area", () =>
        {
            if (!__state || Run.Current is not { } run || run.Game.Sim != __instance.Sim) return;
            using var ships = TempList<Ship>.Alloc();
            __instance.GetShipsInSelectionRect(ships, onlySelectable: false, preferLocalPlayerShips: false, preferSignificantShips: false);
            if (ships.Count == 0) FromPoint(run, __instance.Sim.WorldMouseLoc);
        });
    }

    /// <summary>The highlighted mission sorts with the prioritized missions, so the list shows it. Only the list reads this.</summary>
    [HarmonyPatch(typeof(Mission), nameof(Mission.PrioritizeOnHud))]
    static class Prioritize
    {
        static void Postfix(Mission __instance, ref bool __result)
        {
            if (!__result && Current == __instance) __result = true;
        }
    }

    static readonly ConditionalWeakTable<Widget, WidgetNineSlice> Backgrounds = new();

    /// <summary>The highlighted mission's entry in the list gets the vanilla selected list item background.</summary>
    [HarmonyPatch(typeof(MissionsHud), "OnUpdateUIState")]
    static class ListBackground
    {
        static void Postfix(MissionsHud __instance) => Errors.Guard("mission highlight list", () =>
        {
            var current = Current;
            foreach (var (mission, widget) in __instance._widgets)
            {
                bool on = mission == current;
                if (!Backgrounds.TryGetValue(widget, out var background))
                {
                    if (!on) continue;
                    background = new WidgetNineSlice(widget);
                    background.CopySettingsFrom(ListItem.Default.SelectedNineSlice);
                    widget.Components.Insert(0, background);
                    Backgrounds.Add(widget, background);
                }
                background.SelfActive = on;
            }
        });
    }

    /// <summary>Test only: 1 while the list shows the highlighted mission with its background on.</summary>
    internal static bool ShownInList(GameRoot game)
    {
        if (Current is not { } m || ((Cosmoteer.Modes.Career.CareerGameModeManager)game.Mode)._missionsHud is not { } hud) return false;
        return hud._widgets.TryGetValue(m, out var w) && Backgrounds.TryGetValue(w, out var bg) && bg.SelfActive;
    }
}

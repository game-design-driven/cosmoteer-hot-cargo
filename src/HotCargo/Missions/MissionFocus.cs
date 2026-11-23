using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Simulation.Cameras;
using Cosmoteer.Simulation.Doodads;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>What a click on an objective line shows (spec 1873). None: nothing to show.</summary>
internal enum FocusStep { None, Map, Ship, Marker, Place }

/// <summary>Where an objective is: its system, its ship when live, its area marker, and its place in the system (null in another system).</summary>
internal readonly record struct FocusTarget(ObjectID NodeId, Ship? Live, ObjectID? PoiId, Vector2? Location);

/// <summary>
/// Spec 1873: a click on a Hot Cargo objective line shows where it is, in the order of the vanilla objectives
/// (ObjectiveTargetShip.OnFocused, DefeatShipsObjective.OnDisplayLineFocused): in another system the galaxy map opens on that system;
/// in the system, the target ship when it is on radar, else the mission's area marker, else the target's place (also in stasis).
/// UI only: it moves the camera and changes no sim state.
/// </summary>
internal static class MissionFocus
{
    internal static FocusStep Step(GameRoot game, FocusTarget target)
    {
        if (game.CurrentMapNode.UniqueID != target.NodeId) return FocusStep.Map;
        if (target.Live is { IsWithinRadarOfLocalPlayer: true }) return FocusStep.Ship;
        if (Marker(game, target) is not null) return FocusStep.Marker;
        return target.Location is null ? FocusStep.None : FocusStep.Place;
    }

    internal static bool CanFocus(GameRoot game, FocusTarget target) => Step(game, target) != FocusStep.None;

    internal static void Focus(GameRoot game, FocusTarget target)
    {
        switch (Step(game, target))
        {
            case FocusStep.Map:
                (game.Map.Nodes[target.NodeId] ?? throw new InvalidOperationException($"no map node {target.NodeId}")).FocusCamera();
                break;
            case FocusStep.Ship:
                target.Live!.FocusCamera();
                break;
            case FocusStep.Marker:
                Marker(game, target)!.FocusCamera();
                break;
            case FocusStep.Place:
                game.ShowMap = false;
                game.Sim.Camera.ViewController.FocusOn(new FixedPointFocusTarget(target.Location!.Value), playSound: true,
                    clearFrameOfReference: true, clearFrameOfRotation: true);
                break;
        }
    }

    static POIDoodad? Marker(GameRoot game, FocusTarget target) => target.PoiId is { } id ? game.Sim.Doodads[id] as POIDoodad : null;

    /// <summary>A vanilla target ship: its own focus first (radar, discovered landmark, other system), else its place.</summary>
    internal static bool CanFocus(GameRoot game, ObjectiveTargetShip target) => target.CanFocus() || CanFocus(game, Of(target));

    internal static void Focus(GameRoot game, ObjectiveTargetShip target)
    {
        if (target.OnFocused()) return;
        Focus(game, Of(target));
    }

    static FocusTarget Of(ObjectiveTargetShip target) => new(target.MapNodeID, target.TargetShip, null, target.DetLocation());
}

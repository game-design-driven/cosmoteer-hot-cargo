using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Geometry;

namespace HotCargo.Core;

/// <summary>
/// The ship editor's own design checks, so that every ship Hot Cargo designs or changes is one that a player could build and run:
/// - invalid parts and doors (red in the editor): Ship.UpdateBlueprintValidity, BlueprintPart.InvalidReasons;
/// - a missing vital part (BuildToolbox warning, ShipRules.VitalCategories): airlock, command room, reactor, crew quarters, fire extinguisher;
/// - for each part, the build tool tooltip (BaseBuildState.GetCrewPathInfo): no door access, no crew, no resource, and a resource supplier
///   without crew. It uses the game's BlueprintPathfinder and the AccessWarningLevel of the part rules, like the tooltip.
/// The tooltip is a GUI method of a build state, so its decision is repeated here without the GUI text. Revisit after a game update that
/// changes GetCrewPathInfo.
/// </summary>
internal static class ShipCheck
{
    /// <summary>The problems of the ship's blueprints, one line each ("part at x,y: problem"). Empty when the editor shows no warning.</summary>
    internal static List<string> Problems(Ship ship)
    {
        var problems = new List<string>();
        if (!ship.UpdateBlueprintValidity())
        {
            problems.AddRange(ship.BlueprintParts.Where(p => !p.IsValid).Select(p => $"{Name(new PartInfo(p))}: invalid ({p.InvalidReasons})"));
            problems.AddRange(ship.BlueprintDoors.Where(d => !d.IsValid).Select(d => $"door at {d.Location.Cell.X},{d.Location.Cell.Y} {d.Location.Orientation}: invalid"));
            if (problems.Count == 0) problems.Add("blueprints invalid");
        }
        foreach (var vital in ship.Rules.VitalCategories ?? [])
            if (!ship.BlueprintParts.HasPartsInCategory(vital.Category)) problems.Add($"missing a part of category {vital.Category}");
        var finder = new BlueprintPathfinder(ship);
        foreach (var part in ship.BlueprintParts)
        {
            var info = new PartInfo(part);
            problems.AddRange(PathProblems(ship, finder, info, checkDoorAccess: true, depth: 0).Select(why => $"{Name(info)}: {why}"));
        }
        return problems;
    }

    /// <summary>Throws with every problem of the ship. <paramref name="what"/> names the ship in the message.</summary>
    internal static void Require(Ship ship, string what)
    {
        var problems = Problems(ship);
        if (problems.Count > 0) throw new InvalidOperationException($"{what} fails the ship editor checks: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// Throws with the problems that the ship has now and did not have in <paramref name="before"/> (Problems of the ship before a change).
    /// For changes to designs that Hot Cargo did not make: they must not make the design worse.
    /// </summary>
    internal static void RequireNoNew(Ship ship, IReadOnlyCollection<string> before, string what)
    {
        var added = Problems(ship).Except(before).ToList();
        if (added.Count > 0) throw new InvalidOperationException($"{what} adds ship editor problems: {string.Join("; ", added)}");
    }

    static string Name(PartInfo p) => $"{p.Rules.ID} at {p.Location.X},{p.Location.Y}";

    /// <summary>BaseBuildState.GetCrewPathInfo: the door, crew and resource warnings of one part. A resource supplier gets the crew and resource checks too.</summary>
    static IEnumerable<string> PathProblems(Ship ship, BlueprintPathfinder finder, PartInfo part, bool checkDoorAccess, int depth)
    {
        // Vanilla recurses without a limit. A chain this long means a loop of suppliers; fail loudly instead of a stack overflow.
        if (depth > 8) throw new InvalidOperationException($"{Name(part)}: resource supplier chain longer than 8");
        var rules = part.Rules;
        if (checkDoorAccess && !rules.NoAutoDoors && rules.HasDoorLocations && rules.DoorAccessWarningLevel != AccessWarningLevel.None
            && !finder.HasPathToOutside(part, requireWalled: true))
            yield return "no door access" + Weak(rules.DoorAccessWarningLevel);
        var crew = AccessWarningLevel.None;
        var resources = new Dictionary<ID<ResourceRules>, AccessWarningLevel>();
        Scan(ship, part, rules.Components, ref crew, resources);
        if (crew != AccessWarningLevel.None && float.IsPositiveInfinity(finder.GetPathToNearestCrewSource(part, new List<IntVector2>())))
            yield return "no crew" + Weak(crew);
        foreach (var (resource, level) in resources)
        {
            var path = new List<IntVector2>();
            if (float.IsPositiveInfinity(finder.GetPathToNearestResourceSupplier(part, resource, (p, id) => ToggleMode(ship, p, id), path)))
            {
                yield return $"no {resource}" + Weak(level);
                continue;
            }
            var supplier = new PartInfo(ship.BlueprintParts[path[^1]] ?? throw new InvalidOperationException($"{Name(part)}: no part at the end of its {resource} path"));
            foreach (var why in PathProblems(ship, finder, supplier, checkDoorAccess: false, depth + 1)) yield return $"{resource} from {Name(supplier)}: {why}";
        }
    }

    static string Weak(AccessWarningLevel level) => level == AccessWarningLevel.Weak ? " (weak)" : "";

    /// <summary>BaseBuildState.GetCrewPathInfo._ScanComponents: the strongest crew and resource access levels that the part's components ask for.</summary>
    static void Scan(Ship ship, PartInfo part, IEnumerable<PartComponentRules> components, ref AccessWarningLevel crew, Dictionary<ID<ResourceRules>, AccessWarningLevel> resources)
    {
        foreach (var component in components)
        {
            switch (component)
            {
                case PartCrewRules c: crew = Max(crew, c.AccessWarningLevel); break;
                case BaseResourceStorageRules s when s.SuppliesResources: crew = Max(crew, s.AccessWarningLevel); break;
                case BaseResourceStorageRules: break;
                case FlexResourceGridRules g: crew = Max(crew, g.AccessWarningLevel); break;
                case ResourceConsumerRules r when r.AccessWarningLevel != AccessWarningLevel.None:
                    resources[r.ResourceType] = Max(resources.GetValueOrDefault(r.ResourceType), r.AccessWarningLevel);
                    break;
                case ResourceConsumerRules: break;
                case PartToggledComponentsRules t when t.IncludeInBlueprints == PartToggledComponentsRules.IncludeInBlueprintsMode.True
                    || t.IncludeInBlueprints == PartToggledComponentsRules.IncludeInBlueprintsMode.Dynamic && t.IsBlueprintToggleOn(part.Rules, id => ToggleMode(ship, part, id)):
                    Scan(ship, part, t.Components, ref crew, resources);
                    break;
            }
        }
    }

    static AccessWarningLevel Max(AccessWarningLevel a, AccessWarningLevel b) => a > b ? a : b;

    /// <summary>BaseBuildState.GetToggleMode in blueprint mode: the part's saved toggle choice, else the toggle's default.</summary>
    static int ToggleMode(Ship ship, PartInfo part, ID<PartToggleGuiRules> id) =>
        ship.Metadata.PartUIToggleStates.Get((new PartSaveData(part), id), blueprintMode: true) ?? part.Rules.UIToggleComponentsByID[id].Default;
}

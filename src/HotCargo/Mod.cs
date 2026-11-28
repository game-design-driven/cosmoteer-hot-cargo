using HarmonyLib;

namespace HotCargo;

public static class Mod
{
    public const string HarmonyId = "yarden_zamir.hot_cargo";

    /// <summary>Folder that holds mod.rules and tunables.rules. The DLL lives in its csharp subfolder.</summary>
    public static string ModRoot { get; private set; } = "";

    /// <summary>Spec 17: Core, the shared UI and the test commands are the base of every area (tools/area-deps.py SHARED).</summary>
    static readonly string[] BaseNamespaces = ["HotCargo", "HotCargo.Core", "HotCargo.Ui", "HotCargo.Dev"];

    /// <summary>
    /// The areas in their start order. An area is the namespace of its Register method; its Harmony patches are the patch classes of
    /// that namespace.
    /// </summary>
    static readonly Action[] Areas =
    [
        Tech.TechArea.Register,
        Journey.JourneyArea.Register,
        Hunters.HunterArea.Register,
        Missions.MissionArea.Register,
        Missions.Mechanics.MechanicsArea.Register,
        Tactics.TacticsArea.Register,
        // Ai has its own patches, so it is an area of its own; Tactics called its Register before.
        Ai.AiArea.Register,
        Events.EventsArea.Register,
        Friendly.FriendlyArea.Register,
        Chains.ChainsArea.Register,
        SharedChoice.SharedChoiceArea.Register,
        Contracts.ContractsArea.Register,
        Ux.UxArea.Register,
        Worlds.WorldsArea.Register,
        Balance.BalanceArea.Register,
        ShipAi.ShipAiArea.Register,
        Autonomy.AutonomyArea.Register,
        Scavenge.ScavengeArea.Register,
        Scaling.ScalingArea.Register,
        RunModes.RunModesArea.Register,
        Factions.FactionsArea.Register,
        TunablesMenu.TunablesMenuArea.Register,
        MultiChoice.MultiChoiceArea.Register,
        // Relays registers its comms provider before Diplomacy, so a relay page owns its shared choices (spec 958).
        Relays.RelaysArea.Register,
        Debris.DebrisArea.Register,
        Breach.BreachArea.Register,
        Cut.CutArea.Register,
        Weld.WeldArea.Register,
        Thieves.ThievesArea.Register,
        Stash.StashArea.Register,
        Diplomacy.DiplomacyArea.Register,
        Debt.DebtArea.Register,
        // After Hunters: its stage hooks read the ships that the Hunters area just spawned.
        Pursuit.PursuitArea.Register,
        Storyteller.StorytellerArea.Register,
        Telemetry.TelemetryArea.Register,
        Dev.Invariants.Register,
        Core.VanillaGuards.Register,
        Dev.Playthrough.Register,
        // Last: its run log hooks run after the hooks of the other areas.
        RunRecord.RunRecordArea.Register,
        Onboarding.OnboardingArea.Register,
    ];

    /// <summary>The namespaces of the areas that failed at boot and are off for this game process.</summary>
    public static readonly HashSet<string> AreasOff = [];

    /// <summary>
    /// Spec 17, Rules: a failing Harmony patch never stops the game. The base patches first: when it fails, Hot Cargo stays off and the
    /// game starts as vanilla. Then each area patches and registers on its own: a failure unpatches that area, turns it off and reports
    /// it in the overlay, and the other areas start.
    /// Limit: an area whose Register throws half-way keeps the hooks that it added before the throw. Revisit if such an area does harm.
    /// </summary>
    public static void Boot(string dllDir)
    {
        ModRoot = Path.GetFullPath(Path.Combine(dllDir, ".."));
        var harmony = new Harmony(HarmonyId);
        var patchTypes = AccessTools.GetTypesFromAssembly(typeof(Mod).Assembly).ToLookup(t => t.Namespace ?? "");
        try
        {
            foreach (string ns in BaseNamespaces) Patch(harmony, patchTypes[ns]);
            Core.TunablesDefaults.WriteFile();
        }
        catch (Exception ex)
        {
            harmony.UnpatchAll(HarmonyId);
            AreasOff.Add("all");
            Core.Errors.Report("boot", $"the base failed, Hot Cargo is off: {ex.Message}", ex);
            return;
        }
        var patched = BaseNamespaces.ToHashSet();
        foreach (var register in Areas)
        {
            string ns = register.Method.DeclaringType?.Namespace ?? throw new InvalidOperationException($"area {register.Method.Name} has no namespace");
            if (AreasOff.Contains(ns)) continue;
            try
            {
                if (patched.Add(ns)) Patch(harmony, patchTypes[ns]);
                register();
            }
            catch (Exception ex) when (BaseNamespaces.Contains(ns))
            {
                Core.Errors.Report($"boot {register.Method.DeclaringType?.Name}", $"failed: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Unpatch(harmony, ns);
                AreasOff.Add(ns);
                Core.Errors.Report($"boot {ns}", $"failed, this area is off: {ex.Message}", ex);
            }
        }
        foreach (var group in patchTypes.Where(g => !patched.Contains(g.Key) && g.Any(t => t.IsDefined(typeof(HarmonyPatch), false))))
            Core.Errors.Report($"boot {group.Key}", "has Harmony patches but no area in Mod.Areas; they stay off");
        Log.Info($"harmony patched {harmony.GetPatchedMethods().Count()} methods; areas off: {(AreasOff.Count == 0 ? "none" : string.Join(", ", AreasOff))}; mod root {ModRoot}");
    }

    /// <summary>
    /// Applies the Harmony patch classes of one namespace, as Harmony.PatchAll does for an assembly. Only classes with [HarmonyPatch]:
    /// a class processor reads the methods of any other class too, and a method named Prepare or Prefix there breaks the patch.
    /// </summary>
    static void Patch(Harmony harmony, IEnumerable<Type> types)
    {
        foreach (var type in types.Where(t => t.IsDefined(typeof(HarmonyPatch), false))) harmony.CreateClassProcessor(type).Patch();
    }

    /// <summary>Removes every patch of this mod whose patch method lies in the namespace, also the ones of a class that patched half-way.</summary>
    static void Unpatch(Harmony harmony, string ns)
    {
        foreach (var original in harmony.GetPatchedMethods().ToList())
        {
            if (Harmony.GetPatchInfo(original) is not { } info) continue;
            foreach (var p in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
                if (p.owner == HarmonyId && p.PatchMethod.DeclaringType?.Namespace == ns) harmony.Unpatch(original, p.PatchMethod);
        }
    }
}

using System.Reflection;
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
    /// Spec 2261: a failing Harmony patch never stops the game. The patch classes apply in the order of Harmony.PatchAll (assembly
    /// order), so patches of the same priority on one method run in the same order as before. A class of the base that fails removes
    /// every patch: Hot Cargo stays off, and the game starts as vanilla. A class of an area that fails removes the patches of that area
    /// and turns the area off; the overlay and the log report it, and the other areas start. Then the areas register in their order.
    /// Limit: an area whose Register throws half-way keeps the hooks that it added before the throw. Revisit if such an area does harm.
    /// </summary>
    public static void Boot(string dllDir)
    {
        ModRoot = Path.GetFullPath(Path.Combine(dllDir, ".."));
        var harmony = new Harmony(HarmonyId);
        // Only classes with [HarmonyPatch]: a class processor reads the methods of any other class too, and a method named Prepare or
        // Prefix there breaks the patch.
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Mod).Assembly).Where(t => t.IsDefined(typeof(HarmonyPatch), false)))
        {
            string ns = type.Namespace ?? "";
            if (AreasOff.Contains(ns)) continue;
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception ex) when (BaseNamespaces.Contains(ns))
            {
                harmony.UnpatchAll(HarmonyId);
                AreasOff.Add("all");
                Core.Errors.Report("boot", $"the base failed, Hot Cargo is off: {ex.Message}", ex);
                return;
            }
            catch (Exception ex) { TurnOff(harmony, ns, ex); }
        }
        Core.TunablesDefaults.WriteFile();
        var areas = Areas.Select(r => r.Method.DeclaringType?.Namespace ?? throw new InvalidOperationException($"area {r.Method.Name} has no namespace")).ToHashSet();
        foreach (string ns in harmony.GetPatchedMethods().SelectMany(PatchesOf).Select(p => p.PatchMethod.DeclaringType?.Namespace ?? "").Distinct())
            if (!BaseNamespaces.Contains(ns) && !areas.Contains(ns)) Core.Errors.Report($"boot {ns}", "has Harmony patches but no area in Mod.Areas");
        foreach (var register in Areas)
        {
            string ns = register.Method.DeclaringType!.Namespace!;
            if (AreasOff.Contains(ns)) continue;
            try { register(); }
            catch (Exception ex) when (BaseNamespaces.Contains(ns)) { Core.Errors.Report($"boot {register.Method.DeclaringType.Name}", $"failed: {ex.Message}", ex); }
            catch (Exception ex) { TurnOff(harmony, ns, ex); }
        }
        Log.Info($"harmony patched {harmony.GetPatchedMethods().Count()} methods; areas off: {(AreasOff.Count == 0 ? "none" : string.Join(", ", AreasOff))}; mod root {ModRoot}");
    }

    /// <summary>Removes the patches of the area, also the ones of a class that patched half-way, and turns the area off.</summary>
    static void TurnOff(Harmony harmony, string ns, Exception ex)
    {
        foreach (var original in harmony.GetPatchedMethods().ToList())
            foreach (var p in PatchesOf(original).Where(p => p.owner == HarmonyId && p.PatchMethod.DeclaringType?.Namespace == ns))
                harmony.Unpatch(original, p.PatchMethod);
        AreasOff.Add(ns);
        Core.Errors.Report($"boot {ns}", $"failed, this area is off: {ex.Message}", ex);
    }

    static IEnumerable<Patch> PatchesOf(MethodBase original) =>
        Harmony.GetPatchInfo(original) is { } info ? info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers) : [];
}

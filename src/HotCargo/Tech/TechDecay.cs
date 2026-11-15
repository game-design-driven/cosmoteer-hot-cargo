using System.Reflection;
using Cosmoteer.Crew;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Simulation;
using Halfling.Gui.Components.Text;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 1547-1551: tech outside a vault breaks down in air and heat. The sim tick lists every stack of the players' tech
/// (vault tiles, normal storage tiles, crew hands), TechDecayModel follows each stack and its wear, and a stack in normal storage
/// whose time ran out is destroyed. Sim state only: the wear is saved in RunState.Tech.Decay and hashed, so every client agrees.
/// Limit: crates in space do not decay (they are sealed); revisit if players park tech as crates to wait out a decay.
/// </summary>
public static class TechDecay
{
    /// <summary>True when at least one stack was exposed in the last tick. Only for the "tech decays" toast, which is UI only.</summary>
    static bool _wasExposed;

    /// <summary>Stacks destroyed by decay since start. For autotests.</summary>
    internal static int Destroyed;

    internal static void OnTick(Run run, SimRoot sim, double dt)
    {
        var t = run.T.Tech;
        var state = run.State.Tech;
        if (t.DecaySeconds <= 0) { state.Decay.Clear(); return; }
        var (spots, storage) = Spots(sim);
        var prev = state.Decay.Select(d => new TechWear(d.Key, d.Wear, d.To, d.Group)).ToList();
        var (next, destroy) = TechDecayModel.Step(prev, spots, dt, t.DecaySeconds, t.DecayInVault, s => storage.ContainsKey(s.Key));
        state.Decay = next.Select(w => new DecayEntry { Key = w.Key, Wear = w.Wear, To = w.To, Group = w.Group }).ToList();

        bool exposed = spots.Any(s => s.Exposed);
        if (exposed && !_wasExposed)
        {
            string text = Strings.FormatText("HotCargoTech/DecayStartedFmt", LossTimer.FormatTime(t.DecaySeconds - Worst(run)));
            Run.OnNextFrame(() => Ux.UxKit.CenterText(run.Game, text));
        }
        _wasExposed = exposed;
        if (destroy.Count == 0) return;

        int units = 0;
        foreach (var key in destroy)
        {
            var tile = storage[key];
            int n = ((Cosmoteer.Ships.Resources.IResourceSource)tile).SubtractResources(tile.Resources);
            units += n;
            Log.Info($"tech decay: {n} tech at {tile.ShipCell} on {tile.Grid.Ship?.Metadata.PlainShipName} broke down after {t.DecaySeconds:0} s outside a vault");
        }
        Destroyed += destroy.Count;
        if (Telemetry.Tele.On(Telemetry.Cat.Tech)) Telemetry.Session.Current?.AddTechHint("decay");
        string line = Strings.FormatText("HotCargoTech/DecayedFmt", units);
        Onboarding.RunLog.Add(run, Onboarding.RunLogKind.Loss, line);
        // A co-op tick can run on a worker thread, so the toast and the chat line wait for the next frame.
        Run.OnNextFrame(() =>
        {
            Ux.UxKit.CenterText(run.Game, line);
            Ux.UxKit.ChatLine(run.Game, line);
        });
    }

    /// <summary>
    /// The players' tech stacks this tick, in a stable order, and the normal storage tiles among them by key.
    /// Ships that hold for the players (human and derelict), by ID; then crew that carry tech for the players, by ID.
    /// </summary>
    static (List<TechSpot> Spots, Dictionary<string, FlexResourceGrid.ResourceTile> Storage) Spots(SimRoot sim)
    {
        var spots = new List<TechSpot>();
        var storage = new Dictionary<string, FlexResourceGrid.ResourceTile>();
        foreach (var ship in sim.Ships.AllShips.Where(s => Vaults.HoldsForPlayers(sim, s)).OrderBy(s => s.UniqueID))
        {
            string group = ShipKey(ship);
            foreach (var tile in Vaults.OnShip(ship).SelectMany(g => g).Where(x => x.Resources > 0))
                spots.Add(new TechSpot(Key(tile), Exposed: false, Group: group));
            foreach (var tile in Vaults.StorageTiles(ship).Where(x => x.ResourceType == Vaults.TechId && x.Resources > 0))
            {
                string key = Key(tile);
                spots.Add(new TechSpot(key, Exposed: true, Group: group));
                storage[key] = tile;
            }
        }
        foreach (var crew in sim.Crew._allSouls.Where(c => c.CarriedResource.QuantityOfType(Vaults.TechId) > 0 && Vaults.CarriesForPlayers(sim, c))
                     .OrderBy(c => c.UniqueID))
        {
            var job = crew.Job as ResourceTransferJob;
            spots.Add(new TechSpot(Key(crew), Exposed: true, From: TileKey(job?.Source), To: TileKey(job?.Sink)));
        }
        return (spots, storage);
    }

    static string ShipKey(Ship ship) => ((uint)ship.UniqueID).ToString();
    static string Key(TypedResourceGrid.ResourceTile tile) => $"v{ShipKey(tile.Grid.Ship!)}:{tile.ShipCell.X},{tile.ShipCell.Y}";
    internal static string Key(FlexResourceGrid.ResourceTile tile) => $"s{ShipKey(tile.Grid.Ship!)}:{tile.ShipCell.X},{tile.ShipCell.Y}";
    static string Key(CrewSoul crew) => $"c{(uint)crew.UniqueID}";

    static string? TileKey(object? end) => end switch
    {
        TypedResourceGrid.ResourceTile { Grid.Ship: not null } v => Key(v),
        FlexResourceGrid.ResourceTile { Grid.Ship: not null } s => Key(s),
        _ => null,
    };

    /// <summary>Game seconds until the tech on the normal storage tile breaks down.</summary>
    internal static double SecondsLeft(Run run, FlexResourceGrid.ResourceTile tile) => TechDecayModel.SecondsLeft(Wear(run, Key(tile)), run.T.Tech.DecaySeconds);

    /// <summary>Saved wear of the stack at the key, 0 when it has none.</summary>
    static double Wear(Run run, string key) => run.State.Tech.Decay.FirstOrDefault(d => d.Key == key)?.Wear ?? 0;

    /// <summary>The highest wear of any exposed stack (storage or crew hands), 0 when none.</summary>
    static double Worst(Run run) => run.State.Tech.Decay.Where(d => d.Key[0] != 'v').Select(d => d.Wear).DefaultIfEmpty(0).Max();

    /// <summary>Tech units in normal storage of the players' ships and the seconds until the first of them breaks down. Null without any.</summary>
    public static (int Units, double SecondsLeft)? Exposed(Run run)
    {
        var sim = run.Game.Sim;
        int units = sim.Ships.AllShips.Where(s => Vaults.HoldsForPlayers(sim, s)).Sum(Vaults.StorageTech);
        if (units == 0 || run.T.Tech.DecaySeconds <= 0) return null;
        return (units, TechDecayModel.SecondsLeft(Worst(run), run.T.Tech.DecaySeconds));
    }

    /// <summary>Spec 1550: the heat panel line while tech sits in normal storage, else null.</summary>
    public static string? PanelLine(Run run) =>
        run.State.Result == RunResult.Running && Exposed(run) is { } e
            ? Strings.FormatText("HotCargoTech/DecayPanelFmt", e.Units, LossTimer.FormatTime(e.SecondsLeft))
            : null;

    /// <summary>Spec 1549: the tooltip lines of a tile with tech: why it decays and how long it has, or that the vault keeps it.</summary>
    internal static string TileLine(Run run, string key, bool inVault)
    {
        double limit = run.T.Tech.DecaySeconds, wear = Wear(run, key);
        if (inVault)
            return wear > 0 && run.T.Tech.DecayInVault == DecayInVault.Pause
                ? Strings.FormatText("HotCargoTech/TipVaultWornFmt", LossTimer.FormatTime(TechDecayModel.SecondsLeft(wear, limit)))
                : Strings.GetText("HotCargoTech/TipVaultSafe");
        return Strings.FormatText("HotCargoTech/TipDecayFmt", LossTimer.FormatTime(TechDecayModel.SecondsLeft(wear, limit)));
    }

    /// <summary>The tile tooltip of normal storage (spec 1549). GUI only: it reads the run state and changes nothing.</summary>
    [HarmonyPatch]
    static class StorageTipPatch
    {
        static MethodBase TargetMethod() => Getter(typeof(FlexResourceGrid.ResourceTile));

        static void Postfix(FlexResourceGrid.ResourceTile __instance, ref ITextProvider __result)
        {
            if (__instance.ResourceType != Vaults.TechId || __instance.Grid.Ship is not { } ship || Run.Current is not { } run
                || run.Game.Sim != ship.Sim || !Vaults.HoldsForPlayers(ship.Sim, ship) || run.T.Tech.DecaySeconds <= 0) return;
            __result = new SimpleTextProvider(__result.Text + "\n" + TileLine(run, Key(__instance), inVault: false), null);
        }
    }

    /// <summary>The tile tooltip of a vault (spec 1549).</summary>
    [HarmonyPatch]
    static class VaultTipPatch
    {
        static MethodBase TargetMethod() => Getter(typeof(TypedResourceGrid.ResourceTile));

        static void Postfix(TypedResourceGrid.ResourceTile __instance, ref ITextProvider __result)
        {
            if (__instance.Grid.ResourceType != Vaults.TechId || __instance.Resources == 0 || __instance.Grid.Ship is not { } ship
                || Run.Current is not { } run || run.Game.Sim != ship.Sim || run.T.Tech.DecaySeconds <= 0) return;
            __result = new SimpleTextProvider(__result.Text + "\n" + TileLine(run, Key(__instance), inVault: true), null);
        }
    }

    static MethodBase Getter(Type tile) =>
        AccessTools.PropertyGetter(tile, "Cosmoteer.Game.Gui.IGameObjectToolTip.ToolTipText")
        ?? throw new MissingMethodException(tile.FullName, "IGameObjectToolTip.ToolTipText");
}

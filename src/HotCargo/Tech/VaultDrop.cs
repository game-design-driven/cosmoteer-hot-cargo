using System.Reflection;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 65, 445: a vault loses tech only when it is destroyed. Then it drops its tech as crates (the resource drop rate StorageDestroyed = 100 %).
/// With chance VaultTechLossChance the tech burns instead: this empties the tile before the game drops its nuggets, and a toast and
/// a chat line tell the players. The roll is a FastRand seeded from the ship, the vault location and the sim tick, so every client rolls the same; one roll for all tiles of a vault.
/// </summary>
[HarmonyPatch]
static class VaultDropPatch
{
    const string Method = "Cosmoteer.Ships.Parts.Resources.TypedResourceGrid.IResourceTileMembers.OnDestroying";

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(TypedResourceGrid.ResourceTile), Method) ?? throw new MissingMethodException("TypedResourceGrid.ResourceTile", Method);

    static void Prefix(TypedResourceGrid.ResourceTile __instance) => Errors.Guard("vault drop", () =>
    {
        var grid = __instance.Grid;
        if (grid.ResourceType != Vaults.TechId) return;
        if (grid.Ship is not { IsHumanPlayerShip: true } ship || ship.Sim is not { } sim || Run.Current is not { } run || run.Game.Sim != sim) return;
        // The game calls this for each tile; the vault decides once for all its tiles (spec 1540: one unit per tile).
        var at = grid.Part.Location;
        var key = (ship.UniqueID, at, sim.Tick);
        lock (Gate) { if (_last == key) return; _last = key; }
        int units = grid.Resources;
        if (units == 0) return;
        if ((grid.Part.Flags & PartFlags.Salvaged) != 0) { Rehome(run, ship, grid, units); return; }
        ulong seed = ((ulong)(uint)ship.UniqueID << 32) | (uint)((at.X & 0xFFFF) << 16 | (at.Y & 0xFFFF));
        float roll = new FastRand(seed, (ulong)sim.Tick).Single();
        if (roll >= run.T.Tech.VaultTechLossChance)
        {
            Log.Info($"destroyed vault at {at} drops {units} tech as crates (roll {roll:0.000})");
            Onboarding.RunLog.VaultDestroyed(run, units, burned: false);
            return;
        }
        foreach (var tile in grid) tile.Resources = 0;
        LostMessages++;
        Log.Info($"destroyed vault at {at}: {units} tech burned (roll {roll:0.000} < {run.T.Tech.VaultTechLossChance})");
        Onboarding.RunLog.VaultDestroyed(run, units, burned: true);
        string text = string.Format(Strings.GetText("HotCargoTech/VaultBurnedFmt"), units);
        // Destruction can run on a worker thread, so the toast and the chat line wait for the next frame.
        Run.OnNextFrame(() =>
        {
            Ux.UxKit.CenterText(run.Game, text);
            Ux.UxKit.ChatLine(run.Game, text);
        });
    });

    /// <summary>
    /// Spec 1554: crew take the vault apart (a rebuild, a move with the grab tool). That is no destruction, so the tech stays aboard:
    /// first into the other vaults of the ship, then into its normal storage, where it decays. Only tech that finds no room at all
    /// stays in the tiles, and the game drops it as crates. A toast and a chat line say where the tech went. Telemetry names the cause `vault_deconstructed` (TelemetryPatches.VaultHitPatch).
    /// </summary>
    static void Rehome(Run run, Cosmoteer.Ships.Ship ship, TypedResourceGrid grid, int units)
    {
        foreach (var tile in grid) tile.Resources = 0;
        int toVaults = Vaults.Add(Vaults.OnShip(ship).Where(g => g != grid), units);
        int toStorage = Vaults.AddToStorage([ship], units - toVaults);
        int left = units - toVaults - toStorage;
        int back = left;
        foreach (var tile in grid)
        {
            int n = Math.Min(back, tile.MaxResources);
            tile.Resources = n;
            back -= n;
        }
        Rehomed++;
        Log.Info($"vault at {grid.Part.Location} taken apart: {toVaults} tech to other vaults, {toStorage} to storage, {left} dropped as crates");
        string text = Strings.FormatText(left > 0 ? "HotCargoTech/VaultApartCratesFmt" : "HotCargoTech/VaultApartFmt", toVaults, toStorage, left);
        Run.OnNextFrame(() =>
        {
            Ux.UxKit.CenterText(run.Game, text);
            Ux.UxKit.ChatLine(run.Game, text);
        });
    }

    /// <summary>Vaults taken apart since start whose tech moved (spec 1554). For autotests.</summary>
    internal static int Rehomed;

    static readonly object Gate = new();
    static (Cosmoteer.Game.ObjectID, Halfling.Geometry.IntVector2, long)? _last;

    /// <summary>Destroyed vaults whose tech burned since start. For autotests.</summary>
    internal static int LostMessages;
}

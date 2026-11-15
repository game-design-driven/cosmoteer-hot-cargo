using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 1545: a selected vault shows its contents on the vanilla parts card, after the health: "Tech 3/4".
/// A selected normal storage part with tech shows its tech and the time until the first unit breaks down (spec 1549).
/// GUI only: it reads the sim and changes nothing.
/// </summary>
[HarmonyPatch(typeof(PartsCard), nameof(PartsCard.UpdateForSinglePart))]
static class VaultCard
{
    static void Postfix(PartsCard __instance, Part part) => Errors.Guard("vault card", () =>
    {
        if (Run.Current is not { } run || part.Ship is not { } ship || ship.Sim != run.Game.Sim || !Vaults.HoldsForPlayers(ship.Sim, ship)) return;
        string? line = null;
        if (part.Rules.ID == Vaults.VaultId)
        {
            var grids = part.Components.OfType<TypedResourceGrid>().Where(g => g.ResourceType == Vaults.TechId).ToList();
            line = Strings.FormatText("HotCargoTech/VaultContentsFmt", grids.Sum(g => g.Resources), grids.Sum(g => g.MaxResources));
        }
        else if (run.T.Tech.DecaySeconds > 0)
        {
            var tiles = part.Components.OfType<FlexResourceGrid>().SelectMany(g => g).Where(t => t.ResourceType == Vaults.TechId && t.Resources > 0).ToList();
            if (tiles.Count > 0)
                line = Strings.FormatText("HotCargoTech/StorageContentsFmt", tiles.Sum(t => t.Resources), LossTimer.FormatTime(tiles.Min(t => TechDecay.SecondsLeft(run, t))));
        }
        if (line is not null) __instance._partHealth.Text += "    " + line;
    });
}

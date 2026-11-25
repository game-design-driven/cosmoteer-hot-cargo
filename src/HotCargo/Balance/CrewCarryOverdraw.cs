using System.Reflection;
using Cosmoteer;
using Cosmoteer.Crew;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Balance;

/// <summary>
/// #307: a crew member that is asked for more resources than it carries gives what it carries. Vanilla
/// CrewSoul.SubtractResources returns that amount, but CarriedResource.SubtractQuantity throws first, and the game crashes.
/// The path (vanilla 0.30.5a): a cancelled NPC trade hands the carried fuel back with the crew member as the source
/// (ResourceTransferJob, TradeType stays NpcTrade). When a ship then goes into stasis, ResourceManager.InstantlyFinishInProgressTrades
/// takes the full rest of the trade from that crew member, also when it carries less. Seen with 5 asked and 0 carried: both
/// co-op machines crashed (PC run on 2026-11-25, step missions.escort-ambush, right after a station went into stasis).
/// Limit: the sink of that trade already got the full amount, so the trade makes up to the difference out of nothing. Revisit if
/// trade stock counts start to drift.
/// </summary>
[HarmonyPatch]
static class CrewCarryOverdraw
{
    const string Method = "Cosmoteer.Ships.Resources.IResourceSource.SubtractResources";

    static MethodBase TargetMethod() => AccessTools.Method(typeof(CrewSoul), Method) ?? throw new MissingMethodException(nameof(CrewSoul), Method);

    static void Prefix(CrewSoul __instance, ref int amount)
    {
        int carried = __instance.CarriedResource.Quantity;
        if (amount <= carried) return;
        Log.Info($"crew carry overdraw: {amount} asked, {carried} carried, ship {__instance.CurrentShip?.Metadata.PlainShipName}");
        amount = carried;
    }

    /// <summary>Test only: a crew member of the first player ship carries 2 fuel and is asked for 5. It must give 2 and carry nothing.</summary>
    internal static void Check(Run run)
    {
        var ship = SimQueries.FirstHumanShip(run.Game.Sim) ?? throw new InvalidOperationException("no human ship");
        var soul = ship.Crew.Assigned.OrderBy(c => c.UniqueID).FirstOrDefault() ?? throw new InvalidOperationException("the player ship has no crew");
        var before = soul.CarriedResource;
        soul.CarriedResource = new CarriedResource(2, GameApp.Rules.FtlFuelResource);
        int given = ((Cosmoteer.Ships.Resources.IResourceSource)soul).SubtractResources(5);
        int left = soul.CarriedResource.Quantity;
        soul.CarriedResource = before;
        if (given != 2 || left != 0) throw new InvalidOperationException($"crew carry overdraw: gave {given} of 2, {left} left");
    }
}

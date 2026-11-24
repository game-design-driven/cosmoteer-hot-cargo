using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Pooling;
using Halfling.Random;
using Halfling.Timing;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// Spec 1880: every resource delivery is a goods hand-over at its contact (HandOverObjective.NewGoods), never a sale in the trade tab.
/// Both ways that the game makes a DeliverResources objective become one: a rules spawner (vanilla or Hot Cargo: Salvage Race, Sunside Ore,
/// Reactor Shielding) and the vanilla Resource Delivery offers that career mode builds in code. A DeliverResources objective from an older
/// save stays a trade delivery until it ends.
/// </summary>
static class GoodsDelivery
{
    /// <summary>A rules spawner: its Amount range becomes the hand-over's MinAmount to MaxAmount (a slider when they differ).</summary>
    [HarmonyPatch(typeof(DeliverResourcesObjective.Spawner), "CreateObjectiveImpl")]
    static class FromRules
    {
        static bool Prefix(DeliverResourcesObjective.Spawner __instance, SimRoot sim, Rand rand, SpawnedObjectCollection objects, Vector2 loc,
            out bool invalid, ref Objective? __result)
        {
            var resource = rand.SelectElement(__instance.RandomResourceTypes);
            // Unused (the range stays a range), but drawn like vanilla: the same random stream keeps world generation as it was.
            _ = rand.Int32(__instance.Amount);
            var contact = objects.FindObject(__instance.DeliveryLocationTag, loc, rand);
            var collection = __instance.SuggestedCollectionAreaTag is { } area ? objects.FindObject(area, loc, rand) : null;
            var contactShip = new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), contact.UniqueID ?? throw new InvalidOperationException("delivery contact has no ID"));
            __result = HandOverObjective.NewGoods(sim, resource, __instance.Amount.Min, __instance.Amount.Max, contactShip, contact.Location.Center, collection?.UniqueID);
            invalid = false;
            return false;
        }
    }

    /// <summary>
    /// The vanilla Resource Delivery offer (CareerSimModeManager.CreateResourceDeliveryMission, 0.30.5a): the same mission, with the goods
    /// hand-over in place of the trade delivery. Kept in step with the vanilla method: pick, amount, time limit, pay at the station, reward.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), "CreateResourceDeliveryMission")]
    static class FromCode
    {
        static bool Prefix(CareerSimModeManager __instance, MetaShip toShip, Rand rand, Time timeLimit, MissionList existingMissions, ref Mission __result)
        {
            var rules = __instance.SectorType.ResourceDeliveryMissions;
            // The vanilla roulette wheel, so the random stream stays as in vanilla.
            using var wheel = TempFloatRouletteWheel<ResourceDeliveryMissionRules.DeliveryTypeRules>.Alloc();
            foreach (var t in rules.DeliveryTypes) wheel.Add(t, t.RandomWeight);
            ResourceDeliveryMissionRules.DeliveryTypeRules type;
            // Like vanilla: a resource that an offer of this list delivers already is not picked again.
            do type = wheel.RemoveRandom(rand).Object;
            while (existingMissions.Any(m => m.HasObjective(o => o is HandOverObjective { Goods: true } h && h.ResourceType == type.ResourceType
                                                               || o is DeliverResourcesObjective d && d.ResourceType == type.ResourceType)));
            var mission = new Mission(__instance.Game, rules.Metatype, Cosmoteer.Factions.FactionRules.GetShipFaction(toShip)?.ID);
            int amount = rand.Int32(type.Quantity);
            amount = (int)(Mathx.Round((float)amount / type.RoundQuantityToMultiple) * type.RoundQuantityToMultiple);
            var goods = HandOverObjective.NewGoods(__instance.Sim, type.ResourceType, amount, amount, new ObjectiveTargetShip(__instance.Sim, toShip), toShip.DetWorldCenter, null);
            mission.AddObjective(new SequentialObjective([
                new TimeLimitObjective([goods], __instance.Sim.LogicalTime, timeLimit),
                new ReceiveRewardsObjective(new ObjectiveTargetShip(__instance.Sim, toShip)),
            ]));
            int refund = __instance.GetResourceSellRefund(type.ResourceType, toShip.PlayerIndex);
            int money = (int)Mathx.Ceiling(amount * refund * rules.RewardMoneyPerSellPrice);
            money = (int)(Mathx.Round((float)money / rules.RoundRewardMoneyToMultiple) * rules.RoundRewardMoneyToMultiple);
            mission.AddReward(new MoneyReward(mission, money));
            __result = mission;
            return false;
        }
    }
}

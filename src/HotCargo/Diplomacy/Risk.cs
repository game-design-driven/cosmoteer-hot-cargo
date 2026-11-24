using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.CommsModules;
using Cosmoteer.Simulation;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Ai;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Diplomacy;

/// <summary>Spec 1520-1524: what one AI ship sees of the fight now.</summary>
public readonly record struct RiskView(float Own, float Players, int Allies, float Odds, float Bravery, float Confidence, Stance Stance);

/// <summary>
/// Spec 1520-1528. The one risk model of the enemy AI: odds, bravery and stance, for demands, flee and surrender of every faction.
/// Reads sim state (sim positions, spec 1512) and run state only, in sim order, so every co-op machine decides the same.
/// </summary>
public static class Risk
{
    /// <summary>Spec 1521-1524: the sides, odds, bravery and stance of an AI ship.</summary>
    public static RiskView Read(Run run, SimRoot sim, Ship ai)
    {
        var t = run.T.Diplomacy;
        var hired = run.State.Diplomacy.Hires.Select(h => h.Ship).ToHashSet();
        float own = Situation.Strength(ai), players = 0f;
        float ownRange = HunterOrders.MaxWeaponRange(ai, onlyActive: true), playersRange = 0f;
        int allies = 0;
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var s = sim.Ships[i];
            if (s == ai || !s.IsSignificant || s.IsJunk) continue;
            float d = s.DetWorldCenter.DistanceTo(ai.DetWorldCenter);
            if (s.IsHumanPlayerShip || hired.Contains(s.UniqueID))
            {
                if (d > t.AreaRadius) continue;
                players += Situation.Strength(s);
                playersRange = Math.Max(playersRange, HunterOrders.MaxWeaponRange(s, onlyActive: false));
            }
            else if (s.AI is not null && s.Metadata.Team == ai.Metadata.Team && !Situation.IsStation(s) && d <= t.RiskJoinRadius)
            {
                own += Situation.Strength(s);
                ownRange = Math.Max(ownRange, HunterOrders.MaxWeaponRange(s, onlyActive: true));
                allies++;
            }
        }
        float odds = RiskModel.Odds(own, players, RiskModel.RangeFactor(ownRange, playersRange, t.RiskRangeWeight));
        float bravery = Bravery(run, ai);
        float confidence = RiskModel.Confidence(odds, bravery);
        return new RiskView(own, players, allies, odds, bravery, confidence, RiskModel.Decide(confidence, t.Stances, proud: Situation.IsHunter(ai)));
    }

    /// <summary>Spec 1523: the group base ± the spread, from a hash of the ship ID.</summary>
    public static float Bravery(Run run, Ship ship)
    {
        var t = run.T.Diplomacy;
        string group = PersonalityStrategy.Group(ship);
        float groupBase = t.RiskBravery.FirstOrDefault(g => g.Group == group)?.Bravery ?? t.RiskBraveryDefault;
        return RiskModel.Bravery(groupBase, t.RiskBraverySpread, new FastRand((uint)ship.UniqueID, 0x4252_4156UL).Single());
    }

    /// <summary>The run, when the risk model decides for this sim, else null.</summary>
    public static Run? Active(SimRoot? sim) =>
        Run.Current is { } run && run.T.Diplomacy.RiskEnabled && sim is not null && run.Game.Sim == sim ? run : null;

    /// <summary>
    /// Spec 1528: the line about the allies that the risk model counted, in the voice of the ship's group, or null when it counted none.
    /// </summary>
    public static string? AlliesLine(Ship ship, RiskView view)
    {
        string key = "HotCargoDiplomacy/AlliesSoonFmt_" + PersonalityStrategy.Group(ship);
        string format = Strings.TryGetText(key, out var text) ? text : DipText.Text("AlliesSoonFmt");
        return RiskModel.AlliesLine(format, view.Allies);
    }
}

/// <summary>
/// Spec 1525-1526. The vanilla rule (CareerSimModeManager.GetSurrenderForPrice) decides whether an AI ship demands the players'
/// surrender or offers its own, for its hails and its dialogue choices. The risk stance decides on top: a demand only in the demand
/// stance (outside it, a wanted player can still pay off the bounty when they call), an offer only in the surrender stance.
/// </summary>
[HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetSurrenderForPrice), [typeof(Ship), typeof(Ship), typeof(bool)], [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out])]
static class RiskSurrenderPatch
{
    static void Postfix(CareerSimModeManager __instance, Ship surrenderer, Ship surrenderingTo, ref bool aiDefaultingToAcceptSurrender, ref int? __result)
    {
        if (__result is null) return;
        try
        {
            if (Risk.Active(__instance.Sim) is not { } run) return;
            var sim = __instance.Sim;
            bool playerSurrenders = surrenderer.IsHumanPlayerShip;
            var ai = playerSurrenders ? surrenderingTo : surrenderer;
            var player = playerSurrenders ? surrenderer : surrenderingTo;
            var stance = Risk.Read(run, sim, ai).Stance;
            if (playerSurrenders ? stance == Stance.Demand : stance == Stance.Surrender) return;
            // A wanted player can still pay off the bounty when they call the ship (vanilla "default accept"); the ship does not demand it.
            if (playerSurrenders && Situation.Wanted(sim, player.Metadata.PlayerIndex, ai))
            {
                aiDefaultingToAcceptSurrender = true;
                return;
            }
            __result = null;
            aiDefaultingToAcceptSurrender = false;
        }
        catch (Exception ex) { Errors.Report("risk surrender", ex.Message, ex); }
    }
}

/// <summary>
/// Spec 2220: an AI ship offers its own surrender (hail and dialogue) only after it fought long enough or took enough damage,
/// with or without the risk model. Vanilla allows it after any damage, so a ship hit once at the start of a fight surrendered at once.
/// </summary>
[HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetSurrenderForPrice), [typeof(Ship), typeof(Ship), typeof(bool)], [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out])]
static class SurrenderReadyPatch
{
    static void Postfix(CareerSimModeManager __instance, Ship surrenderer, ref int? __result)
    {
        if (__result is null || surrenderer.IsHumanPlayerShip) return;
        try
        {
            if (Run.Current is not { } run || run.Game.Sim != __instance.Sim || !run.T.Diplomacy.Enabled) return;
            if (!DiplomacyTick.SurrenderReady(run, surrenderer)) __result = null;
        }
        catch (Exception ex) { Errors.Report("surrender ready", ex.Message, ex); }
    }
}

/// <summary>Spec 1528: the vanilla demand dialogue names the allies that the risk model counted.</summary>
[HarmonyPatch(typeof(AIDialogueModule), nameof(AIDialogueModule.GetDialogueExchange))]
static class RiskDemandTextPatch
{
    static void Postfix(Ship player, Ship aiShip, AIDialogueModule.DialogueExchange? __result)
    {
        if (__result is null) return;
        try
        {
            if (Risk.Active(aiShip.Sim) is not { } run || aiShip.Sim is not { } sim) return;
            var career = Situation.Career(sim);
            // The exchange is the demand when the ship does not offer its own surrender (vanilla shows that one first).
            if (career.GetSurrenderForPrice(aiShip, player).HasValue || !career.GetSurrenderForPrice(player, aiShip).HasValue) return;
            if (Risk.AlliesLine(aiShip, Risk.Read(run, sim, aiShip)) is { } line)
                __result.PrimaryDialogue += "\n" + Halfling.StringTools.EscapeForXml(line);
        }
        catch (Exception ex) { Errors.Report("risk demand text", ex.Message, ex); }
    }
}

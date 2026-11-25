using System.Text.Json;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.CommsModules;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Diplomacy;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 1583: diplomacy events. Vanilla AI hails (demand or offer of surrender), vanilla surrenders, and the Hot Cargo hail choices
/// (Deals.Decided), each with the odds and stance of the risk model (spec 1520 to 1524). Reads only: Risk.Read and
/// GetSurrenderForPrice are queries.
/// </summary>
static class DiplomacyTelemetry
{
    internal static void Register() => Deals.Decided += OnDecided;

    static void WriteAi(Utf8JsonWriter w, Session s, Ship ai)
    {
        w.WriteString("ship", ai.UniqueID.ToString());
        w.WriteString("name", ai.Metadata.PlainShipName);
        w.WriteString("side", Session.Side(ai));
        if (Cosmoteer.Factions.FactionRules.GetFaction(ai.Metadata.PlayerIndex) is { } f) w.WriteString("faction", f.ID.ToString());
        if (ai.Sim is not { } sim) return;
        var view = Risk.Read(s.Run, sim, ai);
        Tele.Num(w, "odds", view.Odds);
        Tele.Num(w, "confidence", view.Confidence);
        Tele.Num(w, "bravery", view.Bravery);
        w.WriteString("stance", view.Stance.ToString());
        w.WriteNumber("allies", view.Allies);
    }

    /// <summary>A vanilla AI hail: it offers its own surrender, or demands the players' (AIDialogueModule hail rule).</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.AIHailPlayer))]
    static class HailPatch
    {
        static void Prefix(CareerSimModeManager __instance, Ship aiCaller, Ship playerReceiver)
        {
            if (Session.Current is not { } s || __instance.Sim != s.Game.Sim || !Tele.On(Cat.Diplomacy)) return;
            long t0 = Stats.Enter();
            try
            {
                // Issue #327: GetSurrenderForPrice reads the caller's AIDialogueModule without a null check. A ship without one (an
                // event carrier such as the escort Courier, a miner) is a valid caller; it has no surrender price.
                bool talks = aiCaller.AI?.GetCommsModule<AIDialogueModule>() is not null;
                int? offer = talks ? __instance.GetSurrenderForPrice(aiCaller, playerReceiver) : null;
                int? demand = talks && offer is null ? __instance.GetSurrenderForPrice(playerReceiver, aiCaller) : null;
                Tele.Emit(Cat.Diplomacy, "dip.hail", w =>
                {
                    WriteAi(w, s, aiCaller);
                    w.WriteNumber("player", playerReceiver.Metadata.PlayerIndex);
                    w.WriteString("kind", offer is not null ? "offer_surrender" : demand is not null ? "demand_surrender" : "other");
                    w.WriteBoolean("dialogue", talks);
                    if ((offer ?? demand) is { } price) w.WriteNumber("price", price);
                });
            }
            catch (Exception ex) { Errors.Report("telemetry hail", ex.Message, ex); }
            finally { Stats.Exit(t0); }
        }
    }

    /// <summary>A vanilla surrender (comms button or hail answer): who gives up, the price, and whether the sim took it.</summary>
    [HarmonyPatch(typeof(MPShipSurrenderedInput), nameof(MPShipSurrenderedInput.Execute))]
    static class SurrenderPatch
    {
        static void Prefix(MPShipSurrenderedInput __instance, out int __state) => __state = Count(__instance);

        static int Count(MPShipSurrenderedInput i) => i.Surrenderer?.Sim?.Mode is CareerSimModeManager m ? m.SurrenderedAIs.Count + m.SurrenderedToAIs.Count : -1;

        static void Postfix(MPShipSurrenderedInput __instance, int playerIndex, int __state)
        {
            if (Session.Current is not { } s || !Tele.On(Cat.Diplomacy) || __instance.Surrenderer is not { } from || __instance.SurrenderingTo is not { } to) return;
            if (from.Sim != s.Game.Sim) return;
            long t0 = Stats.Enter();
            try
            {
                bool players = from.IsHumanPlayerShip;
                var ai = players ? to : from;
                // Vanilla adds the AI ship to one of these sets when the price check passes; a stale price does nothing.
                bool accepted = __state >= 0 && Count(__instance) > __state;
                Tele.Emit(Cat.Diplomacy, "dip.surrender", w =>
                {
                    WriteAi(w, s, ai);
                    w.WriteNumber("player", playerIndex);
                    w.WriteString("by", players ? "players" : "ai");
                    w.WriteNumber("price", __instance.SurrenderPrice);
                    w.WriteBoolean("accepted", accepted);
                });
            }
            catch (Exception ex) { Errors.Report("telemetry surrender", ex.Message, ex); }
            finally { Stats.Exit(t0); }
        }
    }

    static void OnDecided(Run run, SimRoot sim, Deals.DealResult d)
    {
        if (Session.Current is not { } s || s.Run != run || sim != s.Game.Sim || !Tele.On(Cat.Diplomacy)) return;
        long t0 = Stats.Enter();
        try
        {
            Tele.Emit(Cat.Diplomacy, "dip.deal", w =>
            {
                WriteAi(w, s, d.Callee);
                w.WriteNumber("player", d.Player);
                w.WriteString("offer", d.Offer.ToString());
                w.WriteNumber("amount", d.Amount);
                w.WriteNumber("price", d.Price);
                if (d.Chance is { } c) Tele.Num(w, "chance", c);
                if (d.Roll is { } r) Tele.Num(w, "roll", r);
                if (d.Accepted is { } a) w.WriteBoolean("accepted", a);
            });
        }
        finally { Stats.Exit(t0); }
    }
}

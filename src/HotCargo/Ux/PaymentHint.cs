using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Halfling;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// V22, spec PAYHINT: a mission paid by "any station in a system" (vanilla Visit missions) stays open until the players press the receive
/// payment button in the comms of a station there. The vanilla line does not say to call a station. While the payment waits in the current
/// system, the mission line says so, and one chat line and one banner tell the players once per mission. GUI only.
/// </summary>
static class PaymentHint
{
    static bool On => UxKit.On(s => s.PaymentHint);

    static bool DueHere(GameRoot game, ReceiveRewardsObjective payment) =>
        payment.Mission is { } mission && payment.ContactNodeID is { } node && game.CurrentMapNode is { } here &&
        PaymentHintModel.DueHere(
            payment.Completion == CompletionState.InProgress && mission.Completion == CompletionState.InProgress,
            mission.Objectives.TakeWhile(o => o != payment).Select(o => o.Completion == CompletionState.Completed),
            node.Equals(here.UniqueID));

    /// <summary>
    /// The objective line in the Nearby Missions list and the mission log. Limit: vanilla formats the line when it builds it (a system
    /// arrival, a change of an objective), so the line changes at those moments, not in every frame. Both happen at the arrival.
    /// </summary>
    [HarmonyPatch(typeof(ReceiveRewardsObjective), "GetDisplayLinesImpl")]
    static class LinePatch
    {
        static void Postfix(ReceiveRewardsObjective __instance, List<Objective.DisplayLine> lines, GameRoot game)
        {
            if (lines.Count == 0 || __instance.ContactNodeID is null) return;
            UxKit.Guard("payment line", () =>
            {
                var line = lines[^1];
                var vanilla = line.Text;
                line.Text = new LiveText(() => On && DueHere(game, __instance) ? UxKit.Text("PaymentHereLine") : vanilla.Text);
                lines[^1] = line;
            });
        }
    }

    static readonly HashSet<Cosmoteer.Game.ObjectID> Told = [];
    static double _nextPoll;
    internal static int HintsShown;

    /// <summary>
    /// One chat line and one banner when a payment becomes due here, once per mission. Polled once a second.
    /// Limit: the told set lives in memory, so after a load an open payment hints once more. Revisit if players find that noisy.
    /// </summary>
    internal static void OnFrame(GameRoot game)
    {
        double now = App.Clock.Time.Seconds;
        if (now < _nextPoll || !On || Run.Current is not { } run || run.Game != game) return;
        _nextPoll = now + 1;
        foreach (var mission in run.Mode.Missions.OrderBy(m => m.MissionID))
            foreach (var payment in mission.Objectives.OfType<ReceiveRewardsObjective>())
            {
                if (!PaymentHintModel.Tell(DueHere(game, payment), Told, mission.MissionID)) continue;
                HintsShown++;
                string name = mission.Name.Text;
                Log.Info($"ux: payment hint for {name}");
                UxKit.ChatLine(game, $"<fame_color>{UxKit.Format("PaymentHintFmt", Halfling.StringTools.EscapeForXml(name))}</fame_color>");
                // The banner label shows plain text.
                ShipAlerts.Banner(game, UxKit.Format("PaymentHintFmt", name), WidgetRules.Instance.LimitedTextColor);
            }
    }
}

using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Simulation;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// One button on an event page. Index is the choice index in the rules, or -1 for "Continue" on a result page.
/// Submit: the choice submits the page's options (spec 811); Enter picks the first enabled one (UX V15).
/// </summary>
public sealed record ChoiceView(int Index, string Text, bool Enabled, bool Submit = false);

/// <summary>
/// One option row of a page (spec 810): Id is the option index in the rules and the multiselect option ID. Selected and Value come
/// from the shared draft; Value is the slider steps. Active is false while the option's need is not met (Tooltip says why).
/// </summary>
public sealed record OptionView(int Id, string Text, bool Active, string? Tooltip, Ui.SliderSpec? Slider, bool Selected, int Value);

/// <summary>What the dialog shows. Built from sim state without widgets, so autotests check it headless.</summary>
public sealed record EventPage(string Key, int Serial, string Title, string Body, IReadOnlyList<ChoiceView> Choices, IReadOnlyList<OptionView> Options);

public static class EventView
{
    /// <summary>The page to show now: the pending event, else an unseen result, else null.</summary>
    public static EventPage? Current(Run run, SimRoot sim, int localPlayer, int lastResultShown)
    {
        var s = run.State.Events;
        // A page carried by a ship shows in that ship's comms (EventHailProvider), not here.
        if (s.Pending is { } p && !EventHail.IsCarried(p)) return Pending(run, sim, p, localPlayer);
        if (s.LastResult is { } r && r.Serial > lastResultShown) return Result(r);
        return null;
    }

    public static EventPage Result(EventResult r)
    {
        string body = r.Lines.Length > 0 ? $"{r.Text}\n\n{string.Join("\n", r.Lines)}" : r.Text;
        return new EventPage($"result {r.Serial}", r.Serial, r.Title, body, [new ChoiceView(-1, "Continue.", true)], []);
    }

    public static EventPage Pending(Run run, SimRoot sim, PendingEvent p, int localPlayer)
    {
        var e = EventLibrary.Get(p.EventId);
        int money = run.Game.GetPlayerState(localPlayer, deterministic: false).Money;
        var snapshot = EventRunner.PageSnap(run, sim, money, p);
        var picks = EventRunner.Picks(run, e, p.Serial);
        var choices = new List<ChoiceView>();
        for (int i = 0; i < e.Choices.Length; i++)
        {
            var c = e.Choices[i];
            var unmet = EventRunner.ChoiceUnmet(run, c, p, c.IsSubmit ? picks : [], snapshot);
            if (unmet is not null && c.HideIfUnmet) continue;
            choices.Add(new ChoiceView(i, ChoiceText(run, e, c, p, unmet, c.IsSubmit ? picks : []), unmet is null, c.IsSubmit));
        }
        string body = EventText.Fill(p.Lead.Length > 0 ? $"{p.Lead}\n\n{e.Text}" : e.Text, run.State.FleetTech, p);
        if (e.OfferLine.Length > 0 && e.Choices.FirstOrDefault(c => c.IsSubmit) is { } submit)
            body += "\n\n" + string.Format(e.OfferLine, EventModel.OfferPercent(EventRunner.PointsOf(picks), EventModel.NeedPoints(submit.NeedPoints, submit.NeedPointsPerHeat, p.Heat)));
        return new EventPage($"event {p.Serial}", p.Serial, e.Title, body, choices, Options(run, e, p, picks, snapshot));
    }

    /// <summary>The page's option rows: hidden ones left out, the rest with the draft's selection and value.</summary>
    static List<OptionView> Options(Run run, EventRules e, PendingEvent p, IReadOnlyList<EventRunner.OptionPick> picks, Snapshot snapshot)
    {
        var rows = new List<OptionView>();
        var options = EventRunner.OptionsOf(run, e, p.Serial);
        // Spec 2384: the goods option also says what its amount pays.
        var goods = p.Carrier is { } contact && HandOverObjective.ForContact(run, contact) is { Goods: true } h ? h : null;
        for (int i = 0; i < options.Length; i++)
        {
            var o = options[i];
            bool paysJob = goods is not null && e.Options[i].Resource == OptionRules.HandOverResource;
            string Value(float units) => ValueText(run, o, units, p) + (!paysJob ? ""
                : goods!.Completes(units) ? ", " + EventText.Money(goods.GoodsPay(units))
                : ", " + Cosmoteer.Localization.Strings.FormatText("HotCargoEventMissions/GoodsSoFarFmt", goods.Delivered + units, goods.MinAmount));
            var unmet = EventModel.FirstUnmet(EventRunner.ToNeed(run, o.Need, p.Tier, p.TechValue), snapshot);
            if (unmet is not null && o.HideIfUnmet) continue;
            var pick = picks.FirstOrDefault(k => k.Index == i);
            int steps = pick?.Steps ?? o.MinSteps;
            var slider = o.MaxSteps > o.MinSteps
                ? new Ui.SliderSpec($"ev{p.Serial}.{i}", "", o.MinSteps, o.MaxSteps, 1, o.MinSteps, v => Value(v * o.StepUnits))
                : null;
            string text = EventText.Fill(o.Text, run.State.FleetTech, p);
            if (slider is null && Value(o.MinUnits) is { Length: > 0 } fixedValue) text += $" <gray>({fixedValue})</gray>";
            rows.Add(new OptionView(i, unmet is null ? text : $"<gray>{text}</gray>", unmet is null, unmet is null ? null : EventText.Reason(unmet), slider, pick is not null, steps));
        }
        return rows;
    }

    /// <summary>What an option's amount pays or gives: "3,000 credits", "2 Stolen Tech", "15 Hyperium", or its own ValueText.</summary>
    public static string ValueText(Run run, OptionRules o, float units, PendingEvent p)
    {
        var pay = EventRunner.PayOf(run, o, units, p);
        return o.Pay switch
        {
            PayKind.Money => $"{pay.Money:n0} credits",
            PayKind.Tech => $"{pay.Tech} tech",
            PayKind.Crew => $"{pay.Crew} crew",
            PayKind.CrewWork => $"{pay.CrewWork} crew",
            PayKind.Resource => EventText.ResourceName(o.Resource, pay.Resource),
            PayKind.None => o.ValueText.Length > 0 ? string.Format(o.ValueText, EventModel.Whole(units, EventRunner.UnitSize(o, p))) : "",
            _ => throw new ArgumentOutOfRangeException(nameof(o), o.Pay, null),
        };
    }

    /// <summary>
    /// "[Sensor Array] Scan it first. (-5,000 credits)" with the requirement tag in FTL blue, or grey text with the reason in red.
    /// No number prefix: the UX area draws the key numbers next to the buttons (Ux/DialogueKeys).
    /// </summary>
    static string ChoiceText(Run run, EventRules e, ChoiceRules c, PendingEvent p, Unmet? unmet, IReadOnlyList<EventRunner.OptionPick> picks)
    {
        string tag = RequirementTag(c.Need);
        string cost = CostText(run, c.Cost, p);
        if (unmet is not null)
            // No requirement tag here: the reason names what is missing, so the tag would say it twice.
            return $"<gray>{EventText.Fill(c.Text, run.State.FleetTech, p)}{(cost.Length > 0 ? $" ({cost})" : "")}</gray>   <bad>{EventText.Reason(unmet)}</bad>";
        string tagXml = tag.Length > 0 ? EventText.Unlocked.FormatWithXml($"[{tag}]") + " " : "";
        string costXml = cost.Length > 0 ? $" <gray>({cost})</gray>" : "";
        // Spec 2230: a paid heat cut shows the cut that works now.
        float heatCutFactor = EventRunner.IsPaidHeatCut(c, picks) ? run.HeatBuyFactor() : 1f;
        string gain = e.HideEffects || c.Outcomes.Length != 1 ? "" : GainText(run, c.Outcomes[0], p, picks.Sum(k => k.Units), heatCutFactor);
        return $"{tagXml}{EventText.Fill(c.Text, run.State.FleetTech, p)}{costXml}{(gain.Length > 0 ? "   " + gain : "")}";
    }

    /// <summary>
    /// Spec 2270: the sure effects of a choice with one outcome, in the words of its result lines (EventText): the money (with a
    /// hand-over's job pay), fame, heat, crew and resources. A choice with several outcomes is a gamble and shows none. An offer price
    /// is in the choice text already.
    /// </summary>
    static string GainText(Run run, OutcomeRules o, PendingEvent p, float pickedUnits, float heatCutFactor)
    {
        var handOver = o.HandOver && p.Carrier is { } contact ? HandOverObjective.ForContact(run, contact) : null;
        // Spec 2384: a goods delivery pays for the picked units; with no amount in range picked yet, its pay is not shown.
        if (handOver is { Goods: true } && (pickedUnits < 1 || pickedUnits > handOver.Remaining || !handOver.Completes(pickedUnits))) handOver = null;
        var job = handOver?.Mission;
        float handOverPay = handOver is { Goods: true } ? handOver.GoodsPayFactor(pickedUnits) : o.HandOverPay;
        var rewards = job?.Rewards ?? [];
        var g = EventModel.Gain(
            EventRunner.Credits(run, o.Money, o.MoneyTech, p.Tier, p.TechValue),
            o.Fame != 0 ? EventRunner.FameAmount(run, o.Fame, p.Tier) : 0,
            o.Heat * run.T.Events.HeatScale * (o.Heat < 0f ? heatCutFactor : 1f),
            o.Crew - o.CrewLeaves,
            job is not null,
            rewards.OfType<Cosmoteer.Modes.Career.Missions.Rewards.MoneyReward>().Select(r => r.Money),
            rewards.OfType<Cosmoteer.Modes.Career.Missions.Rewards.FameReward>().Sum(r => r.Fame),
            handOverPay);
        var parts = new List<string>();
        if (g.Money != 0) parts.Add(EventText.Money(g.Money));
        if (g.Fame != 0) parts.Add(EventText.Fame(g.Fame));
        if (MathF.Abs(g.Heat) >= 0.5f) parts.Add(EventText.Heat(g.Heat));
        if (g.Crew != 0) parts.Add(EventText.Crew(g.Crew));
        parts.AddRange(o.Resources.Select(r => r.At(p.Tier)).Zip(o.Resources).Where(x => x.First != 0)
            .Select(x => EventText.Resource(new ID<ResourceRules>(x.Second.ID), x.First)));
        return string.Join("  ", parts);
    }

    /// <summary>
    /// The visible requirements of a choice. Heat, sector and flag needs stay hidden, like story conditions in FTL.
    /// Tech needs stay hidden too: in events.rules they only keep the last crate safe, next to a tech Cost that the button shows.
    /// </summary>
    static string RequirementTag(NeedRules? n)
    {
        if (n is null) return "";
        var parts = new List<string>();
        parts.AddRange(n.Parts.Select(EventText.PartName));
        if (n.Crew > 0) parts.Add($"{n.Crew} crew");
        if (n.VaultSpace > 0) parts.Add($"vault room {n.VaultSpace}");
        parts.AddRange(n.Resources.Select(r => EventText.ResourceName(r.ID, r.Amount)));
        return string.Join(", ", parts);
    }

    static string CostText(Run run, CostRules? cost, PendingEvent p)
    {
        if (cost is null) return "";
        var parts = new List<string>();
        int money = EventRunner.CostCredits(run, cost, p);
        if (money > 0) parts.Add($"-{money:n0} credits");
        int tech = cost.Tech + (cost.OfferUnits ? p.OfferUnits : 0);
        if (tech > 0) parts.Add($"-{tech} Stolen Tech");
        if (cost.Crew > 0) parts.Add($"-{cost.Crew} crew");
        parts.AddRange(cost.Resources.Select(r => $"-{EventText.ResourceName(r.ID, r.At(p.Tier))}"));
        return string.Join(", ", parts);
    }

    /// <summary>Plain text of a page for the log: XML tags removed.</summary>
    public static string Plain(string xml)
    {
        var sb = new System.Text.StringBuilder(xml.Length);
        bool inTag = false;
        foreach (char ch in xml)
        {
            if (ch == '<') inTag = true;
            else if (ch == '>') inTag = false;
            else if (!inTag) sb.Append(ch);
        }
        return sb.ToString();
    }
}

using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.Hunters;

namespace HotCargo.Events;

/// <summary>
/// Spec 1801 to 1809: Off the Books. A station's dock officer quotes a price and an informant chance and sets up a meet with a fence, away
/// from the station. The players fly the crates there and hand them over in the fence's comms. The sale pays the quote and cuts heat, or it
/// is a sting: heat rises and corporate ships jump in. All state is run state (EventsState.OffBooks); every change runs in the choice input
/// or the sim tick, so every co-op machine does the same.
/// </summary>
static class OffBooks
{
    static readonly ID<SimObjectSpawner> FenceTag = new("hc_ob_fence");
    const string MeetSpawn = "OffBooksMeet", StingSpawn = "CorporatePatrol";
    const double CheckSeconds = 1;
    /// <summary>Salt of the sting roll from the page seed.</summary>
    const ulong StingSalt = 0x4F46_4642_4F4FUL;

    static EventsTunables T(Run run) => run.T.Events;
    /// <summary>The price per crate of the last sale. For autotests; not sim state.</summary>
    static int LastPrice;
    static string Text(string key, params object?[] args) => Strings.FormatText("HotCargoOffBooks/" + key, args);

    internal static void Register()
    {
        EventRunner.Effects.Add("off_books_meet", SetUp);
        EventRunner.Effects.Add("off_books_sale", Sale);
        Run.Hooks.Tick += OnTick;
        Run.Hooks.SystemEntered += (run, _) => run.State.Events.OffBooks = null;
        EventText.Tokens.Add(Fill);
        // Test fields (spec 1809): 1 while a meet runs, the meet's price per crate, the fence live.
        Dev.Autotest.Fields["ob_meet"] = () => Dev.Autotest.RequireRun().State.Events.OffBooks is null ? 0 : 1;
        Dev.Autotest.Fields["ob_price"] = () => Dev.Autotest.RequireRun().State.Events.OffBooks?.PricePerCrate ?? 0;
        Dev.Autotest.Fields["ob_last_price"] = () => LastPrice;
        Dev.Autotest.Fields["ob_fence"] = () => Dev.Autotest.RequireRun().State.Events.OffBooks?.Fence is { } f && Dev.Autotest.RequireRun().Game.Sim.Ships[f] is not null ? 1 : 0;
    }

    // ---- The quote (spec 1802, 1803)

    static float Reputation(Run run, Ship? station) => station is null ? 0f : Situation.Reputation(run, station);

    /// <summary>Spec 1802: credits per crate now, from the tech value and the situation at the station.</summary>
    internal static int Price(Run run, SimRoot sim, Ship? station)
    {
        var t = T(run);
        float share = OffBooksModel.PriceShare(run.State.Heat, HunterSpawner.Hunters(sim).Count(), Reputation(run, station), t.OffBooksPriceBase,
            t.OffBooksPricePerHeat, t.OffBooksPricePerHunter, t.OffBooksPricePerReputation, t.OffBooksPriceMin, t.OffBooksPriceMax);
        int round = Math.Max(1, t.MoneyRoundTo);
        return (int)Math.Round(share * EventRunner.CurrentTechValue(run) / round) * round;
    }

    internal static float Risk(Run run, Ship? station)
    {
        var t = T(run);
        return OffBooksModel.RiskPerCrate(run.State.Heat, Reputation(run, station), t.OffBooksRiskBase, t.OffBooksRiskPerHeat, t.OffBooksRiskPerReputation);
    }

    /// <summary>Spec 1805: corporate ships within OffBooksCorporateRadius of the fence: hunters and military ships of the corporation's ally.</summary>
    internal static List<Ship> CorporateNear(Run run, SimRoot sim, Ship fence)
    {
        float r = T(run).OffBooksCorporateRadius;
        return sim.Ships.Where(s => !s.IsJunk && s.IsSignificant && HunterOrders.EdgeDistance(s, fence) <= r
                && (s.HasTag(HunterSpawner.HunterTag) || Cosmoteer.PlayerIndexes.IsFactionMilitary(s.Metadata.PlayerIndex)
                    && Factions.FactionsArea.IsAlly(run, Situation.Faction(s))))
            .OrderBy(s => s.UniqueID).ToList();
    }

    // ---- Effects

    /// <summary>
    /// Spec 1804, effect "off_books_meet" (the station page): fixes the quote and spawns the fence 3,000 to 4,000 from the chooser's ship
    /// with a log mission to meet it (OffBooksMeet in spawns.rules).
    /// </summary>
    static void SetUp(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines)
    {
        var station = p.Carrier is { } c ? sim.Ships[c] : null;
        run.State.Events.OffBooks = new OffBooksMeet
        {
            Station = p.Carrier ?? throw new InvalidOperationException("off_books_meet needs the station as the page's carrier"),
            PricePerCrate = Price(run, sim, station), RiskPerCrate = Risk(run, station), Until = run.State.GameSeconds + T(run).OffBooksMeetSeconds,
        };
        var before = SimQueries.TaggedShips(sim, FenceTag).Select(x => x.Id).ToHashSet();
        var at = SimQueries.AnchorShip(sim, s => s.Metadata.PlayerIndex == player ? 1f : 0f)?.DetWorldCenter
            ?? throw new InvalidOperationException("off_books_meet: no player ship");
        EventLibrary.Spawn(MeetSpawn).Generate(sim, new FastRand(p.Seed, 0x4D45_4554UL), loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
        var fence = SimQueries.TaggedShips(sim, FenceTag).Select(x => x.Id).Where(id => !before.Contains(id)).ToList();
        if (fence.Count != 1) throw new InvalidDataException($"{MeetSpawn} made {fence.Count} ships tagged {FenceTag}, not 1");
        run.State.Events.OffBooks.Fence = fence[0];
        lines.Add(Text("MeetLineFmt", (int)Math.Ceiling(T(run).OffBooksMeetSeconds / 60.0)));
        Log.Info($"off the books: meet set up, {run.State.Events.OffBooks.PricePerCrate} per crate, risk {run.State.Events.OffBooks.RiskPerCrate:0.000} per crate");
    }

    /// <summary>
    /// Spec 1805 to 1807, effect "off_books_sale" (the fence's page, after the crates were paid): pays the quote for each crate, then rolls
    /// the sting chance from the page seed. Clean: heat changes by OffBooksHeatPerCrate per crate. Sting: heat changes by OffBooksStingHeat
    /// and corporate ships jump in beside the fence. The fence leaves either way.
    /// </summary>
    static void Sale(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines)
    {
        var t = T(run);
        var meet = run.State.Events.OffBooks ?? throw new InvalidOperationException("off_books_sale with no meet");
        var fence = (p.Carrier is { } c ? sim.Ships[c] : null) ?? throw new InvalidOperationException("off_books_sale: the fence is gone");
        int crates = (int)MathF.Round(p.PickedUnits);
        if (crates <= 0) throw new InvalidOperationException("off_books_sale with no crates picked");
        int corporate = CorporateNear(run, sim, fence).Count;
        float chance = OffBooksModel.StingChance(meet.RiskPerCrate, crates, corporate, t.OffBooksRiskPerCorporate, t.OffBooksRiskCap);
        float roll = new FastRand(p.Seed, StingSalt).Single();
        EventRunner.ChangeMoney(run, player, crates * meet.PricePerCrate, lines);
        LastPrice = meet.PricePerCrate;
        bool sting = roll < chance;
        Log.Info($"off the books: {crates} crates, {corporate} corporate near, sting roll {roll:0.000} vs {chance:0.000}: {(sting ? "sting" : "clean")}");
        if (sting)
        {
            EventRunner.ChangeHeat(run, sim, t.OffBooksStingHeat * t.HeatScale, lines);
            EventLibrary.Spawn(StingSpawn).Generate(sim, new FastRand(p.Seed, StingSalt + 1), loadInstantly: true, rootLoc: fence.DetWorldCenter, addExistingSpawnTags: true);
            lines.Add(Text("Sting"));
            Ux.UxKit.CenterText(run.Game, Text("Sting"));
            Ux.UxKit.ChatLine(run.Game, Text("Sting"));
        }
        else EventRunner.ChangeHeat(run, sim, t.OffBooksHeatPerCrate * crates * t.HeatScale, lines);
        Leave(run, fence.UniqueID);
        run.State.Events.OffBooks = null;
    }

    static void Leave(Run run, ObjectID fence)
    {
        if (!run.State.Diplomacy.Leaving.Any(l => l.Ship == fence))
            run.State.Diplomacy.Leaving.Add(new Leaving { Ship = fence, Due = run.State.GameSeconds + 3 });
    }

    /// <summary>Spec 1804: at the meet's time the fence leaves (the hand-over fails with it); a fence that is gone ends the meet.</summary>
    static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("off the books", () =>
    {
        if (sim != run.Game.Sim || run.State.Events.OffBooks is not { Fence: { } fence } meet) return;
        double now = run.State.GameSeconds;
        if (Math.Floor(now / CheckSeconds) == Math.Floor((now - dt) / CheckSeconds)) return;
        bool live = sim.Ships[fence] is { IsJunk: false };
        bool frozen = !live && sim.Stasis.OfType<StasisShipSpawner>().Any(x => x.ReservedObjectID == fence);
        if (!live && !frozen)
        {
            run.State.Events.OffBooks = null;
            Ux.UxKit.ChatLine(run.Game, Text("FenceGone"));
            Log.Info("off the books: the fence is gone; the meet is off");
            return;
        }
        if (now < meet.Until) return;
        if (live) Leave(run, fence);
        else foreach (var x in sim.Stasis.OfType<StasisShipSpawner>().Where(x => x.ReservedObjectID == fence).ToList()) sim.Stasis.RemoveStasisSpawner(x);
        run.State.Events.OffBooks = null;
        Ux.UxKit.ChatLine(run.Game, Text("FenceLeft"));
        Log.Info("off the books: the fence waited long enough and leaves");
    });

    // ---- Page text (spec 1802, 1805), UI only

    /// <summary>Tokens of the two pages: {ob_price} and {ob_risk} (the station's quote now), {ob_quote} and {ob_sting} (the meet's quote and the corporate ships near the fence).</summary>
    static string Fill(string text)
    {
        if (!text.Contains("{ob_", StringComparison.Ordinal) || Run.Current is not { } run || run.Game.Sim is not { } sim) return text;
        var t = T(run);
        var meet = run.State.Events.OffBooks;
        var station = run.State.Events.Pending?.Carrier is { } c ? sim.Ships[c] : null;
        text = text.Replace("{ob_price}", Money(run, Price(run, sim, station)), StringComparison.Ordinal)
            .Replace("{ob_risk}", OffBooksModel.Percent(Risk(run, station)).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (meet is null) return text;
        var fence = meet.Fence is { } f ? sim.Ships[f] : null;
        var near = fence is null ? [] : CorporateNear(run, sim, fence);
        string sting = near.Count == 0 ? Text("NoCorporate")
            : Text("CorporateFmt", near.Count, string.Join(", ", near.Select(s => s.Metadata.XmlShipName)), OffBooksModel.Percent(t.OffBooksRiskPerCorporate));
        return text.Replace("{ob_quote}", Money(run, meet.PricePerCrate), StringComparison.Ordinal)
            .Replace("{ob_quote_risk}", OffBooksModel.Percent(meet.RiskPerCrate).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{ob_sting}", sting, StringComparison.Ordinal);
    }

    static string Money(Run run, int credits) => Cosmoteer.Extensions.ToXmlCostString(credits, run.Game);
}

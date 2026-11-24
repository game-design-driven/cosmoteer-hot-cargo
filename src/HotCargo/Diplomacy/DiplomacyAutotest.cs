using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Factions;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.AI.CommsModules;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;
using HotCargo.Ui;

namespace HotCargo.Diplomacy;

/// <summary>
/// Autotest commands and fields of the Diplomacy area. Targets: hunter (first hunter by ID), hunterN, friend or enemy (the last ship
/// that dip_spawn made), fresh (the first hunter that got no offer yet), focus (the hunter that dip_focus pinned; hunter IDs of new waves
/// can be lower, so "hunter" can change ship). Commands that change the sim run with "mp" in co-op tests; "dip" sends the real MP input like a click.
/// </summary>
static class DiplomacyAutotest
{
    static ObjectID? _friend, _enemy, _focus;
    static string _pageText = "";
    static int _pageButtons, _pageActive, _pageSlider, _pageToggles, _holdLast;
    static HashSet<Offer> _pageOffers = [];

    static int _markedAllies;

    static Ship FirstSpawned() => Ai.PersonalityAutotest.LastSpawned.Select(id => Sim().Ships[id]).OfType<Ship>().FirstOrDefault(s => !s.IsJunk)
                                ?? throw new InvalidOperationException("no aispawn ship");

    static double DemandCheck(bool surrender)
    {
        var ai = FirstSpawned();
        // The player ship nearest to the enemy, not the acting ship (lowest ID): ships that aiplayership adds get random IDs, so one of
        // them far away could become the acting ship (#209 moved the sim random stream).
        var player = Sim().Ships.HumanPlayerShips.MinBy(s => s.DetWorldCenter.DistanceTo(ai.DetWorldCenter)) ?? throw new InvalidOperationException("no player ship");
        var career = Situation.Career(Sim());
        if (surrender) return career.GetSurrenderForPrice(ai, player).HasValue ? 1 : 0;
        return career.GetSurrenderForPrice(player, ai, out bool aiDefaulting).HasValue && !aiDefaulting ? 1 : 0;
    }

    static Run R() => Autotest.RequireRun();
    static EventHandler<SimRoot>? _shield;
    static SimRoot Sim() => R().Game.Sim;
    static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    static float Float(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    internal static Ship Target(string name)
    {
        var sim = Sim();
        if (name == "fresh")
        {
            var used = R().State.Diplomacy.Used;
            return HunterSpawner.Hunters(sim).OrderBy(h => h.UniqueID)
                .FirstOrDefault(h => !Enum.GetValues<Offer>().Any(o => used.Contains(DiplomacyState.Key(h.UniqueID, o))))
                ?? throw new InvalidOperationException("no hunter without offers");
        }
        if (name.StartsWith("hunter", StringComparison.Ordinal))
        {
            int i = name.Length > 6 ? Int(name[6..]) : 0;
            return HunterSpawner.Hunters(sim).OrderBy(h => h.UniqueID).ElementAtOrDefault(i) ?? throw new InvalidOperationException($"no hunter {i}");
        }
        if (name == "aispawn") return FirstSpawned();
        ObjectID? id = name switch { "friend" => _friend, "enemy" => _enemy, "focus" => _focus, _ => throw new InvalidOperationException($"unknown target '{name}'") };
        return (id is { } x ? sim.Ships[x] : null) ?? throw new InvalidOperationException($"no live {name} ship");
    }

    static Ship Me() => Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("no player ship");

    internal static void Register()
    {
        // dip <offer> <target> [amount]: sends the choice as the local player, through the shared choice input like a click.
        Autotest.Commands["dip"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length is < 2 or > 3) throw new InvalidOperationException("dip needs: offer target [amount]");
            var offer = Enum.Parse<Offer>(p[0], ignoreCase: true);
            var target = Target(p[1]);
            R().Game.NetManager.EnqueueInput(DiplomacyChoiceDomain.Pick(target, offer, p.Length == 3 ? Int(p[2]) : 0));
        };
        // dip_focus <target>: pins a ship as the target "focus". dip_picks and dip_paychance read it (else the first hunter).
        Autotest.Commands["dip_focus"] = arg => _focus = Target(arg.Trim()).UniqueID;
        Autotest.Commands["dip_set"] = arg => Autotest.SetField(R().T.Diplomacy, arg);
        // dip_spawn friend|ally|enemy|trader [distance]: a combat ship near the acting ship: of the system faction (friend), of the corporation's
        // ally faction (ally, military), on a new hostile side (enemy), or a trade ship (trader). friend, ally and trader become "friend".
        Autotest.Commands["dip_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float distance = p.Length > 1 ? Float(p[1]) : 500f;
            var ship = Spawn(p[0], distance);
            if (p[0] == "enemy") _enemy = ship.UniqueID; else _friend = ship.UniqueID;
        };
        // dip_friend_alone <radius>: the dip_spawn friend ship holds still (no AI), and every other ship that is not a player ship or a
        // station within the radius of it leaves the system. For tests that talk to that ship: the game picks the nearest ship.
        Autotest.Commands["dip_friend_alone"] = arg =>
        {
            var sim = Sim();
            var friend = (_friend is { } id ? sim.Ships[id] : null) ?? throw new InvalidOperationException("no live dip_spawn friend");
            float radius = Float(arg.Trim());
            friend.AI = null;
            friend.Commands.Clear();
            var others = sim.Ships.Where(s => s != friend && !s.IsHumanPlayerShip && !Situation.IsStation(s)
                && s.DetWorldCenter.DistanceTo(friend.DetWorldCenter) <= radius).OrderBy(s => s.UniqueID).ToList();
            foreach (var s in others) sim.Ships.Remove(s, dispose: true, handleLostShip: false);
            Log.Info($"AUTOTEST dip_friend_alone: {friend.Metadata.PlainShipName} holds still; {others.Count} other ships within {radius:0} removed");
        };
        // dip_expect <offer> <target> <amount> <op> <value>: checks the chance that the dialogue shows.
        Autotest.Commands["dip_expect"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 5) throw new InvalidOperationException("dip_expect needs: offer target amount op value");
            float chance = Chance(Enum.Parse<Offer>(p[0], ignoreCase: true), Target(p[1]), Int(p[2]));
            double expected = double.Parse(p[4], CultureInfo.InvariantCulture);
            bool ok = p[3] switch
            {
                "==" => Math.Abs(chance - expected) < 0.005, ">=" => chance >= expected, "<=" => chance <= expected,
                ">" => chance > expected, "<" => chance < expected, _ => throw new InvalidOperationException($"unknown op {p[3]}"),
            };
            Autotest.Check(ok, $"chance {p[0]} {p[1]} {p[2]} {p[3]} {p[4]} (actual {chance:0.000})");
        };
        // dip_page <target>: builds the dialogue page as the local player sees it and logs it: text, option rows (sliders) and buttons.
        Autotest.Commands["dip_page"] = arg =>
        {
            var target = Target(arg.Trim());
            var page = target.AI?.GetDialogueExchange(Me()) as HailPage
                ?? throw new InvalidOperationException($"{target.Metadata.PlainShipName} has no hail page");
            _pageText = page.PrimaryDialogue;
            _pageButtons = page.Buttons.Count + page.Rows.Count;
            _pageActive = page.Buttons.Count(b => b.SelfInputActive) + page.Rows.Count(r => r.Active);
            _pageSlider = page.Rows.Count(r => r.Slider is not null);
            _holdLast = page.Rows.Count > 0 && page.Rows[^1].Key == Offer.Hold.ToString() ? 1 : 0;
            _pageToggles = page.Rows.Count(r => r.Toggle);
            _pageOffers = page.Buttons.OfType<HailChoice>().Select(b => DiplomacyChoiceDomain.Decode(b.Option).Offer).ToHashSet();
            Log.Info($"AUTOTEST PAGE {target.Metadata.PlainShipName}: {_pageText.Replace('\n', ' ')} | rows: " +
                     string.Join(" | ", page.Rows.Select(r => (r.Active ? "" : "(off) ") + (r.Toggle ? (r.Selected ? "[X] " : "[ ] ") : "") + r.Text + (r.Slider is { } sl ? $" <{sl.Key}>" : ""))) +
                     " | buttons: " + string.Join(" | ", page.Buttons.Select(b => (b.SelfInputActive ? "" : "(off) ") + b.Text)));
        };
        // dip_slider <target> <key> <value>: sets the slider value of a callee's option row as if the player moved it.
        Autotest.Commands["dip_slider"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            CommsHail.SetSliderValue(Target(p[0]), p[1], Int(p[2]));
        };
        // dip_open <target>: opens the comms window to the target (for screenshots). dip_close closes every comms window.
        Autotest.Commands["dip_open"] = arg => R().Game.Gui.FloatingWindows.AddChild(new CommWindow(R().Game, Me(), Target(arg.Trim())));
        // comms_rows_overflow: option rows of open comms pages whose text is taller than their box (a layout check, also headless).
        Autotest.Fields["comms_rows_overflow"] = () => CommsHail.OverflowingRows();
        // dip_pick <target> <option> [value] | dip_unpick <target> <option>: changes a hunter's pay-off draft through its MP input, like a click on a row.
        Autotest.Commands["dip_pick"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length is < 2 or > 3) throw new InvalidOperationException("dip_pick needs: target option [value]");
            var option = Enum.Parse<PayOption>(p[1], ignoreCase: true);
            R().Game.NetManager.EnqueueInput(new MultiChoice.MultiChoiceInput(DiplomacyMultiDomain.Name, DiplomacyChoiceDomain.Serial(Target(p[0])), (int)option, true, p.Length > 2 ? Int(p[2]) : 0));
        };
        Autotest.Commands["dip_unpick"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 2) throw new InvalidOperationException("dip_unpick needs: target option");
            var option = Enum.Parse<PayOption>(p[1], ignoreCase: true);
            R().Game.NetManager.EnqueueInput(new MultiChoice.MultiChoiceInput(DiplomacyMultiDomain.Name, DiplomacyChoiceDomain.Serial(Target(p[0])), (int)option, false, 0));
        };
        // dip_payoff <target>: the pay-off button, through the shared choice input. dip_bluff <target>: the bluff button with its amount now.
        Autotest.Commands["dip_payoff"] = arg => R().Game.NetManager.EnqueueInput(DiplomacyChoiceDomain.Pick(Target(arg.Trim()), Offer.PayOff, 0));
        Autotest.Commands["dip_bluff"] = arg => R().Game.NetManager.EnqueueInput(DiplomacyChoiceDomain.Pick(Target(arg.Trim()), Offer.Bluff, Quotes.BluffUnits(R(), Sim())));
        Autotest.Commands["dip_close"] = _ =>
        {
            foreach (var w in R().Game.Gui.FloatingWindows.Children.OfType<CommWindow>().ToList()) Cosmoteer.Game.Gui.Extensions.Unparent(w);
        };
        // dip_damage <target> <fraction>: every part of the target to this fraction of its health.
        Autotest.Commands["dip_damage"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var ship = Target(p[0]);
            foreach (var part in ship.Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * Float(p[1])));
        };
        // dip_fillfuel: fills every human storage that takes FTL fuel, so surrender fuel must drop into space.
        Autotest.Commands["dip_fillfuel"] = _ =>
        {
            int added = 0;
            foreach (var s in Tech.Vaults.HumanShips(Sim()).ToList()) added += s.Resources.AddResources(GameApp.Rules.FtlFuelResource, 100000, excludeConstructionSinks: true);
            Log.Info($"AUTOTEST filled {added} fuel");
        };
        // dip_dropfuel <n>: removes FTL fuel from the human ships, so loot has room.
        Autotest.Commands["dip_dropfuel"] = arg =>
        {
            int left = Int(arg);
            foreach (var s in Tech.Vaults.HumanShips(Sim()).OrderBy(s => s.UniqueID).ToList()) left -= s.Resources.RemoveResources(GameApp.Rules.FtlFuelResource, left);
            Log.Info($"AUTOTEST removed {Int(arg) - left} fuel");
        };
        // dip_near <target> [distance]: moves the target next to the acting ship, inside comms and sight range. "hunters" moves every hunter.
        // dip_shield <target>|off: keeps that hunter at full part health each sim tick, like godmode for one ship, so the fleet's guns
        // cannot kill it between a pick and its pay-off. Run it with "mp" in co-op, so both sims do it. Test only.
        Autotest.Commands["dip_shield"] = arg =>
        {
            var game = R().Game;
            if (_shield is not null) { game.EventHub.Tick -= _shield; _shield = null; }
            if (arg.Trim() == "off") return;
            var id = Target(arg.Trim()).UniqueID;
            _shield = (_, sim) =>
            {
                if (sim.Ships[id] is not { } ship) return;
                foreach (var part in ship.Parts)
                    if (part.Health < part.Rules.MaxHealth) part.Health = part.Rules.MaxHealth;
            };
            game.EventHub.Tick += _shield;
            Log.Info($"AUTOTEST dip_shield on {id}");
        };
        Autotest.Commands["dip_near"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float distance = p.Length > 1 ? Float(p[1]) : 250f;
            var ships = p[0] == "hunters" ? HunterSpawner.Hunters(Sim()).OrderBy(h => h.UniqueID).ToList() : [Target(p[0])];
            foreach (var ship in ships)
                Autotest.Teleport(ship, Autotest.ClearSpotNear(Sim(), Me().DetWorldCenter, distance, ship.WorldBoundingRadius));
        };
        // dip_mark: remembers the fuel of the human fleet and the acting player's money, for dip_fuelgain and dip_moneyspent.
        Autotest.Commands["dip_mark"] = _ => { _fuelMark = HumanFuel(); _moneyMark = Money(); };
        // dip_hire_report: logs each hired ship's command and target, and the enemies within 3,000 of the acting ship with their side.
        Autotest.Commands["dip_hire_report"] = _ =>
        {
            var sim = Sim();
            var me = Me();
            foreach (var h in R().State.Diplomacy.Hires)
                Log.Info($"AUTOTEST HIRE {sim.Ships[h.Ship]?.Metadata.PlainShipName ?? "gone"} cmd={sim.Ships[h.Ship]?.Commands.CurConfirmedCommand?.GetType().Name} " +
                         $"target={(sim.Ships[h.Ship]?.Commands.CurConfirmedCommand as Cosmoteer.Ships.Commands.IAttackCommand)?.Target?.Metadata.PlainShipName} flee={h.Fleeing} player={h.Player}");
            foreach (var s in sim.Ships.Where(s => s.IsSignificant && !s.IsJunk && !s.IsHumanPlayerShip && s.DetWorldCenter.DistanceTo(me.DetWorldCenter) < 3000f).OrderBy(s => s.UniqueID))
                Log.Info($"AUTOTEST NEAR {s.Metadata.PlainShipName} id={s.UniqueID} player={s.Metadata.PlayerIndex} d={s.DetWorldCenter.DistanceTo(me.DetWorldCenter):0} " +
                         $"enemyOfMe={me.IsEnemiesWith(s)} cmd={s.Commands.CurConfirmedCommand?.GetType().Name} target={(s.Commands.CurConfirmedCommand as Cosmoteer.Ships.Commands.IAttackCommand)?.Target?.Metadata.PlainShipName}");
        };
        Autotest.Commands["dip_log"] = arg =>
        {
            var s = R().State.Diplomacy;
            Log.Info($"AUTOTEST DIP {arg}: used={s.Used.Count} held=[{string.Join(",", s.Held.Select(h => $"{h.ShipName}:{h.Units}:p{h.Player}"))}] " +
                     $"hires=[{string.Join(",", s.Hires.Select(h => $"{h.Ship}:{h.Until:0}:{(h.Merc ? "merc" : "hire")}{(h.Fleeing ? ":flee" : "")}"))}] " +
                     $"mercs={s.Mercs.Count} leaving={s.Leaving.Count} asks={s.FeeAsks.Count} tech={Situation.VaultTech(Sim())} fleet={R().State.FleetTech}");
        };

        // Surrender odds (spec 1520 and later). dip_demands: 1 when the first aispawn ship would demand the surrender of the local
        // player's ship now (the vanilla rule CareerSimModeManager.GetSurrenderForPrice, with this mod's patches). dip_offers: 1 when it
        // would offer its own surrender.
        Autotest.Fields["dip_demands"] = () => DemandCheck(surrender: false);
        // dip_ai_accepts: the first aispawn ship considers the players' surrender. Vanilla rolls this once from the ship ID, and the ID
        // comes from the sim random stream, so a test that checks the vanilla demand rule sets it.
        Autotest.Commands["dip_ai_accepts"] = _ =>
        {
            var module = FirstSpawned().AI?.GetCommsModule<AIDialogueModule>() ?? throw new InvalidOperationException("the aispawn ship has no dialogue module");
            module.WouldConsiderAcceptingSurrenderFromPlayer = true;
        };
        Autotest.Fields["dip_offers"] = () => DemandCheck(surrender: true);
        // dip_ai_offers: the first aispawn ship considers its own surrender at any value ratio (the vanilla rolls from the ship ID, set
        // like dip_ai_accepts), so only its damage and the Hot Cargo rules decide.
        Autotest.Commands["dip_ai_offers"] = _ =>
        {
            var module = FirstSpawned().AI?.GetCommsModule<AIDialogueModule>() ?? throw new InvalidOperationException("the aispawn ship has no dialogue module");
            module.WouldConsiderSurrenderingToPlayer = true;
            module.ValueRatioDifferenceToSurrender = 1e6f;
        };
        // Spec 2220, 2221: the page that dip_page built offers the players' surrender, or a demand.
        Autotest.Fields["dip_page_offersurrender"] = () => _pageOffers.Contains(Offer.OfferSurrender) ? 1 : 0;
        Autotest.Fields["dip_page_demand"] = () => _pageOffers.Contains(Offer.DemandSurrender) ? 1 : 0;
        // The risk view of the first aispawn ship (spec 1520-1524): stance (0 surrender, 1 flee, 2 fight, 3 demand), confidence, allies.
        Autotest.Fields["dip_stance"] = () => (int)Risk.Read(R(), Sim(), FirstSpawned()).Stance;
        Autotest.Fields["dip_confidence"] = () => Risk.Read(R(), Sim(), FirstSpawned()).Confidence;
        // dip_mark_allies: remembers dip_risk_allies; dip_risk_allies_gain is the change since (other ships of the system can be near).
        Autotest.Fields["dip_risk_allies"] = () => Risk.Read(R(), Sim(), FirstSpawned()).Allies;
        Autotest.Commands["dip_mark_allies"] = _ => _markedAllies = Risk.Read(R(), Sim(), FirstSpawned()).Allies;
        Autotest.Fields["dip_risk_allies_gain"] = () => Risk.Read(R(), Sim(), FirstSpawned()).Allies - _markedAllies;
        Autotest.Fields["dip_picks"] = () => PayTarget() is { } h ? Deals.PayDraft(R(), h).Count : 0;
        Autotest.Fields["dip_paychance"] = () => PayTarget() is { } h ? Quotes.PayOff(R(), Sim(), h, Deals.PayDraft(R(), h)) : 0;
        Autotest.Fields["dip_crew"] = () => R().Game.HumanPlayersIndexes.Sum(Sim().Crew.CountCrewForPlayer);
        Autotest.Fields["dip_pagetoggles"] = () => _pageToggles;
        Autotest.Fields["dip_used"] = () => R().State.Diplomacy.Used.Count;
        Autotest.Fields["dip_held"] = () => R().State.Diplomacy.Held.Sum(h => h.Units);
        Autotest.Fields["dip_asks"] = () => R().State.Diplomacy.FeeAsks.Count;
        Autotest.Fields["dip_hires"] = () => R().State.Diplomacy.Hires.Count;
        Autotest.Fields["dip_friendhired"] = () => _friend is { } f && R().State.Diplomacy.Hires.Any(h => h.Ship == f) ? 1 : 0;
        Autotest.Fields["dip_hirefleeing"] = () => R().State.Diplomacy.Hires.Count(h => h.Fleeing);
        Autotest.Fields["dip_mercs"] = () => R().State.Diplomacy.Mercs.Count;
        Autotest.Fields["dip_friendmerc"] = () => _friend is { } f && R().State.Diplomacy.Mercs.Contains(f) ? 1 : 0;
        Autotest.Fields["dip_yieldoffers"] = () => R().State.Diplomacy.Rolled.Count(k => k.EndsWith(":yieldok", StringComparison.Ordinal));
        Autotest.Fields["dip_leaving"] = () => R().State.Diplomacy.Leaving.Count;
        Autotest.Fields["dip_vaulttech"] = () => Situation.VaultTech(Sim());
        Autotest.Fields["dip_results"] = () => R().State.Diplomacy.Serial;
        Autotest.Fields["dip_hailing"] = () => Situation.Career(Sim()).ShipsHailingPlayer.Values.Sum(l => l.Count);
        Autotest.Fields["dip_surrendered"] = () => Situation.Career(Sim()).SurrenderedAIs.Count;
        Autotest.Fields["dip_fuelgain"] = () => HumanFuel() - _fuelMark;
        Autotest.Fields["dip_moneyspent"] = () => _moneyMark - Money();
        // 1 when a full mercenary contract costs less than hired help for the same time (spec 583).
        Autotest.Fields["dip_merc_cheaper"] = () =>
        {
            var t = R().T.Diplomacy;
            // The merc price before trust (spec 2224) against the hire rate.
            return Quotes.MercPrice(R(), t.MercMaxMinutes) * Quotes.HireTrust(R()) < Quotes.HireSure(R()) * t.MercMaxMinutes * 60f / t.HireSeconds ? 1 : 0;
        };
        // Spec 2224: trust after paid hires and hired ship damage, and the counts it comes from.
        Autotest.Fields["dip_trust"] = () => Quotes.HireTrust(R());
        Autotest.Fields["dip_paidhires"] = () => R().State.Diplomacy.PaidHires;
        Autotest.Fields["dip_shipslost"] = () => R().State.Diplomacy.HireShipsLost;
        // dip_endhire <target>: test only. The target's contract ends now, as if its time ran out (the next hire check removes it).
        Autotest.Commands["dip_endhire"] = arg =>
        {
            var id = Target(arg).UniqueID;
            var hire = R().State.Diplomacy.Hires.FirstOrDefault(h => h.Ship == id) ?? throw new InvalidOperationException($"{arg} has no contract");
            hire.Until = R().State.GameSeconds;
        };
        Autotest.Fields["dip_enemyhostile"] = () => _enemy is { } e && Sim().Ships[e] is { } s && Me().IsEnemiesWith(s) ? 1 : 0;
        Autotest.Fields["dip_technuggets"] = () => Sim().Nuggets.Where(n => n.ResourceType == Tech.Vaults.TechId).Sum(n => n.Quantity);
        Autotest.Fields["dip_friendlive"] = () => _friend is { } f && Sim().Ships[f] is { } s && s.ExistsInSim(Sim()) ? 1 : 0;
        Autotest.Fields["dip_friendattacking"] = () =>
            _friend is { } f && Sim().Ships[f]?.Commands.CurConfirmedCommand is Cosmoteer.Ships.Commands.AttackCommand ? 1 : 0;
        Autotest.Fields["dip_pagebuttons"] = () => _pageButtons;
        Autotest.Fields["dip_pageactive"] = () => _pageActive;
        Autotest.Fields["dip_pageslider"] = () => _pageSlider;
        // 1 when the hold row is the last option row of the page that dip_page built (spec 2223).
        Autotest.Fields["dip_holdlast"] = () => _holdLast;
        Autotest.Fields["dip_answers"] = () => _friend is { } f && Sim().Ships[f] is { } s && Situation.Career(Sim()).WillShipAnswerCall(Me(), s) ? 1 : 0;
    }

    static int _fuelMark, _moneyMark;

    /// <summary>The hunter whose pay-off draft the fields read: the focus ship while it lives, else the first hunter, else none.</summary>
    static Ship? PayTarget() =>
        _focus is { } f && Sim().Ships[f] is { } s && s.ExistsInSim(Sim()) ? s : HunterSpawner.Hunters(Sim()).OrderBy(h => h.UniqueID).FirstOrDefault();

    static int Money() => R().Game.GetPlayerState(Autotest.ActingPlayerIndex, deterministic: true).Money;

    static int HumanFuel() => Tech.Vaults.HumanShips(Sim()).Sum(s => s.Resources.GetResourceTotal(GameApp.Rules.FtlFuelResource, includeStored: true, includeCarried: false));

    static float Chance(Offer offer, Ship target, int amount)
    {
        var run = R();
        var sim = Sim();
        int player = Autotest.ActingPlayerIndex;
        return offer switch
        {
            Offer.Bluff => Quotes.Bluff(run, sim, player),
            Offer.DemandSurrender => Quotes.DemandSurrender(run, sim, player, target),
            Offer.OfferSurrender => Quotes.OfferSurrender(run, sim, player, target),
            Offer.Hold => Quotes.Hold(run, sim, target, amount),
            Offer.Hire => Quotes.Hire(run, sim, player, target, amount * Quotes.HireStep(run)),
            _ => throw new InvalidOperationException($"no chance for {offer}"),
        };
    }

    /// <summary>
    /// Spawns a tier-1 ship near the acting ship: "friend" a combat ship of the system's faction military, "ally" one of the corporation's ally faction, "trader" a faction trade
    /// ship (civilian), "enemy" a combat ship on a new hostile side. Deterministic: the seed comes from the sim tick.
    /// </summary>
    static Ship Spawn(string kind, float distance)
    {
        var sim = Sim();
        var me = Me();
        var faction = kind == "ally" ? Factions.FactionsArea.Roles(R()).Ally
            : sim.MapNode?.ControllingFaction is { } fid ? GameApp.Rules.Factions.First(f => f.ID == fid) : GameApp.Rules.Factions.First();
        var rand = new FastRand((ulong)(uint)sim.Tick, 0x4449_5053UL);
        var tags = new[] { new ID<BuiltinShipTag>(kind == "trader" ? "civilian" : "combat") };
        var built = GameApp.Rules.BuiltinShips.GetRandomMatchingShip(rand, [faction.ID], new Range<int>(1, 3), null, tags)
            ?? GameApp.Rules.BuiltinShips.GetRandomMatchingShip(rand, null, new Range<int>(1, 3), null, tags)
            ?? throw new InvalidOperationException($"no built-in {tags[0]} ship of tier 1 to 3");
        var loc = Autotest.ClearSpotNear(sim, me.DetWorldCenter, distance, 120f);
        int allegiance = kind switch
        {
            "enemy" => Situation.Career(sim).AllocUniquePlayerIndex(),
            "trader" => faction.CivilianPlayerIndex,
            _ => faction.MilitaryPlayerIndex,
        };
        var spawner = new StasisShipSpawner(sim, built.ID, loc, SpawnedShipFeatures.None, rand.UInt64(), [])
        {
            AI = new ID<ShipAIRules>(kind == "trader" ? "TradeShip" : "Normal"),
            Direction = loc.DirectionTo(me.DetWorldCenter),
            ObjectAllegiance = allegiance,
        };
        spawner.Spawn(sim);
        var ship = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{built.ID} did not spawn");
        Log.Info($"AUTOTEST spawned {kind} {ship.Metadata.PlainShipName} id={ship.UniqueID} player={allegiance} at {loc.DistanceTo(me.DetWorldCenter):0}");
        return ship;
    }
}

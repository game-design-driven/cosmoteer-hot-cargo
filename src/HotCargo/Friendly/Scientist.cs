using Cosmoteer;
using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.Events;
using HotCargo.Hunters;

namespace HotCargo.Friendly;

/// <summary>
/// Spec 486 to 499: the scientist chain. A scientist joins the crew. Later they research a tech for the players, or steal processors and
/// defect to the next ship the players pass. A scientist who dies aboard brings their partner, who challenges the players to a duel.
/// All state is run state; it changes only in sim logic (choice inputs, the sim tick, the AI tick), so every co-op machine agrees.
/// </summary>
static class Scientist
{
    /// <summary>
    /// The partner's and the revenge fleet's player index, next to the Wayfarers' (99,000; see PayItForward.Allegiance for the range).
    /// Hostile to the players only during the duel and the revenge (spec 492, 495), never to anyone else.
    /// </summary>
    public const int Allegiance = 99_001;
    static readonly ID<SimObjectSpawner> PartnerTag = new("hc_ev_partner"), RevengeTag = new("hc_ev_revenge");
    static readonly ID<ResourceRules> Processor = new("processor");
    const double CheckSeconds = 1;
    /// <summary>Game seconds before a leaving partner jumps out.</summary>
    const double LeaveDelaySeconds = 5;

    static FriendlyState S(Run run) => run.State.Friendly;
    static PartnerState P(Run run) => run.State.Friendly.Partner;
    static string Text(string key, params object?[] args) => FriendlyArea.Text(key, args);

    internal static void Register()
    {
        EventRunner.Effects.Add("scientist", (run, sim, player, p, lines) =>
            Join(run, sim, player, p.Seed, run.T.Friendly.ScientistResearchChance, canDefect: true, hasPartner: true, lines));
        EventRunner.Effects.Add("partner_accept", Accept);
        EventRunner.Effects.Add("partner_decline", (run, sim, player, p, lines) => Revenge(run, sim, Text("PartnerDeclinedFmt", P(run).Name)));
        EventRunner.Effects.Add("partner_join", (run, sim, player, p, lines) =>
        {
            Join(run, sim, player, p.Seed, run.T.Friendly.PartnerResearchChance, canDefect: false, hasPartner: false, lines);
            End(run, sim, leave: true);
        });
        EventRunner.Effects.Add("partner_home", (run, sim, player, p, lines) => End(run, sim, leave: true));
        Run.Hooks.Tick += OnTick;
        Run.Hooks.SystemEntered += OnSystemEntered;
        HunterOrders.Overrides.Add(Override);
        ScientistAutotest.Register();
    }

    /// <summary>
    /// Spec 487, 496: a scientist joins a ship of the chooser with a free bunk, and their research roll is due in ScientistMinutes.
    /// </summary>
    static void Join(Run run, SimRoot sim, int player, ulong seed, float researchChance, bool canDefect, bool hasPartner, List<string> lines)
    {
        var ship = sim.Ships.HumanPlayerSignificantShips.Where(s => s.Commands.CanExecuteCrewCommandsFrom(player) && s.Crew.RemainingCrewCapacity > 0)
            .OrderBy(s => s.UniqueID).FirstOrDefault()
            ?? throw new InvalidOperationException("scientist: no ship of the chooser has a free bunk (the choice needs Bunks = 1)");
        var before = ship.Crew.Assigned.Select(c => c.UniqueID).ToHashSet();
        if (ship.Crew.CreateCrewsAtRandomParts(1) != 1) throw new InvalidOperationException($"scientist: no crew member could join {ship.Metadata.PlainShipName}");
        var soul = ship.Crew.Assigned.Single(c => !before.Contains(c.UniqueID));
        S(run).Scientists.Add(new ScientistState
        {
            Soul = soul.UniqueID, Player = player, Name = soul.Name, Due = run.State.GameSeconds + run.T.Friendly.ScientistMinutes * 60.0,
            Seed = seed, ResearchChance = researchChance, CanDefect = canDefect, HasPartner = hasPartner,
        });
        lines.Add(EventText.Crew(1));
        Log.Info($"friendly: scientist {soul.Name} joins {ship.Metadata.PlainShipName}; research chance {researchChance:0.00}");
    }

    static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("scientist tick", () =>
    {
        if (sim != run.Game.Sim) return;
        double now = run.State.GameSeconds;
        if (Math.Floor(now / CheckSeconds) == Math.Floor((now - dt) / CheckSeconds)) return;
        foreach (var st in S(run).Scientists.ToList()) TickScientist(run, sim, st, now);
        TickPartner(run, sim, now);
    });

    static void TickScientist(Run run, SimRoot sim, ScientistState st, double now)
    {
        var t = run.T.Friendly;
        var soul = sim.Crew[st.Soul];
        switch (st.Phase)
        {
            case ScientistPhase.Aboard:
                if (soul is null)
                {
                    // Spec 490: gone from the sim while aboard means dead. Limit: a crew member that a player fires counts too; revisit if
                    // the game gets another way to lose crew without death.
                    S(run).Scientists.Remove(st);
                    Ux.UxKit.ChatLine(run.Game, Text("ScientistDiedFmt", st.Name));
                    Log.Info($"friendly: scientist {st.Name} died aboard; partner {(st.HasPartner && P(run).Phase == PartnerPhase.None ? "comes" : "does not come")}");
                    if (st.HasPartner && P(run).Phase == PartnerPhase.None)
                        run.State.Friendly.Partner = new PartnerState { Phase = PartnerPhase.Due, At = now + t.PartnerMinutes * 60.0, Player = st.Player, Name = st.Name };
                    return;
                }
                if (soul.HomeShip is not { IsHumanPlayerShip: true })
                {
                    S(run).Scientists.Remove(st);
                    return;
                }
                if (now < st.Due) return;
                if (new FastRand(st.Seed, 0x5343_4945UL).Single() < st.ResearchChance)
                {
                    S(run).Scientists.Remove(st);
                    Research(run, st);
                }
                else if (st.CanDefect) st.Phase = ScientistPhase.Defecting;
                else
                {
                    S(run).Scientists.Remove(st);
                    Log.Info($"friendly: scientist {st.Name} finds nothing");
                }
                return;
            case ScientistPhase.Defecting:
                if (soul?.HomeShip is not { IsHumanPlayerShip: true } home) { S(run).Scientists.Remove(st); return; }
                var target = sim.Ships.Where(s => !s.IsHumanPlayerShip && !s.IsJunk && s.IsSignificant && s.Crew.Assigned.Count > 0
                        && s.Metadata.PlayerIndex != Allegiance && HunterOrders.EdgeDistance(s, home) <= t.ScientistDefectRange)
                    .OrderBy(s => HunterOrders.EdgeDistance(s, home)).ThenBy(s => s.UniqueID).FirstOrDefault();
                if (target is null) return;
                // Spec 489: they take the processors and go over, out through an airlock, like any crew member with a new home ship.
                st.Stolen = home.Resources.RemoveResources(Processor, t.ScientistStealProcessors);
                home.Crew.UnassignCrew(soul, killed: false);
                target.Crew.AssignCrew(soul);
                st.Target = target.UniqueID;
                st.Phase = ScientistPhase.Flying;
                Log.Info($"friendly: scientist {st.Name} takes {st.Stolen} processors and leaves for {target.Metadata.PlainShipName}");
                return;
            case ScientistPhase.Flying:
                var to = st.Target is { } id ? sim.Ships[id] : null;
                if (soul is null || to is null)
                {
                    S(run).Scientists.Remove(st);
                    Log.Info($"friendly: scientist {st.Name} never reached the other ship");
                    return;
                }
                if (soul.CurrentShip != to) return;
                S(run).Scientists.Remove(st);
                FriendlyArea.Say(run.Game, Text("ScientistDefectedFmt", st.Name, st.Stolen, to.Metadata.XmlShipName));
                return;
        }
    }

    /// <summary>Spec 488: one random tech of the ScientistCheapestTechs cheapest that the players can research now, at no cost.</summary>
    static void Research(Run run, ScientistState st)
    {
        var techs = run.Mode.Techs;
        var cheapest = GameApp.Rules.CareerMode.Techs.Where(x => x.Cost > 0 && !techs.IsTechUnlocked(x.ID) && techs.HasPrerequisites(x))
            .OrderBy(x => x.Cost).ThenBy(x => x.ID.ToString(), StringComparer.Ordinal).Take(Math.Max(1, run.T.Friendly.ScientistCheapestTechs)).ToList();
        if (cheapest.Count == 0)
        {
            Ux.UxKit.ChatLine(run.Game, Text("ScientistNothingFmt", st.Name));
            return;
        }
        var tech = cheapest[new FastRand(st.Seed, 0x5445_4348UL).Int32(0, cheapest.Count - 1)];
        techs.UnlockTech(tech.ID);
        FriendlyArea.Say(run.Game, Text("ScientistResearchFmt", st.Name, tech.NameKey.Text));
        Log.Info($"friendly: scientist {st.Name} researches {tech.ID}");
    }

    static void TickPartner(Run run, SimRoot sim, double now)
    {
        var p = P(run);
        var ship = p.Ship is { } id ? sim.Ships[id] : null;
        switch (p.Phase)
        {
            case PartnerPhase.Due when now >= p.At && run.State.Events.Pending is null:
                Arrive(run, sim);
                return;
            case PartnerPhase.Challenge or PartnerPhase.Yield when ship is not { IsJunk: false }:
                End(run, sim, leave: false);
                return;
            case PartnerPhase.Yield when !p.Hailed && run.State.Events.Pending is null:
                p.Hailed = true;
                EventRunner.Open(run, sim, EventLibrary.Get("scientist_partner_yield"), lead: "", carrier: ship!.UniqueID, carrierHails: true);
                return;
            case PartnerPhase.Duel:
                Duel(run, sim, p, ship);
                return;
            case PartnerPhase.RevengeDue when now >= p.At && run.State.Events.Pending is null:
                Avenge(run, sim);
                return;
            case PartnerPhase.Revenge:
                p.Fleet.RemoveAll(f => sim.Ships[f] is not { IsJunk: false });
                if (p.Fleet.Count > 0) return;
                FriendlyArea.Say(run.Game, Text("RevengeBeatenFmt", p.Name));
                run.State.Friendly.Partner = new PartnerState();
                return;
        }
    }

    /// <summary>
    /// Spec 491: the partner spawns near the players and challenges them. Vanilla puts a ship that spawns out of the live range into stasis;
    /// then the partner is removed and tries again at the next check.
    /// </summary>
    static void Arrive(Run run, SimRoot sim)
    {
        var p = P(run);
        var ships = Spawn(run, sim, "ScientistPartner", PartnerTag);
        if (ships.Count == 0) return;
        p.Ship = ships[0];
        p.Phase = PartnerPhase.Challenge;
        EventRunner.Open(run, sim, EventLibrary.Get("scientist_partner"), lead: "", carrier: p.Ship, carrierHails: true);
        Log.Info($"friendly: the partner of {p.Name} arrives and challenges the players");
    }

    /// <summary>Spawns a generator near the players' anchor ship with the partner allegiance. Returns the live ships; stasis ships are removed.</summary>
    static List<ObjectID> Spawn(Run run, SimRoot sim, string spawn, ID<SimObjectSpawner> tag)
    {
        // Spec 1813: with no working player ship, nothing spawns; the partner or the fleet tries again at the next check.
        if (SimQueries.AnchorShip(sim)?.DetWorldCenter is not { } at) return [];
        var before = sim.Ships.Where(x => x.HasTag(tag)).Select(x => x.UniqueID).ToHashSet();
        var stasisBefore = sim.Stasis.OfType<StasisShipSpawner>().Where(x => x.Tags?.Contains(tag) == true).Select(x => x.ReservedObjectID).ToHashSet();
        EventLibrary.Spawn(spawn).Generate(sim, new FastRand(sim.Rand.UInt64()), loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
        foreach (var x in sim.Stasis.OfType<StasisShipSpawner>().Where(x => x.Tags?.Contains(tag) == true && !stasisBefore.Contains(x.ReservedObjectID)).ToList())
            sim.Stasis.RemoveStasisSpawner(x);
        var live = sim.Ships.Where(x => x.HasTag(tag) && !before.Contains(x.UniqueID)).OrderBy(x => x.UniqueID).ToList();
        foreach (var ship in live)
        {
            ship.Metadata.PlayerIndex = Allegiance;
            foreach (var crew in ship.Crew.Assigned) crew.PlayerIndex = Allegiance;
        }
        if (live.Count == 0) Log.Info($"friendly: the {spawn} spawn made no live ship; it tries again");
        return live.Select(x => x.UniqueID).ToList();
    }

    /// <summary>Spec 492: effect "partner_accept". The chooser's ship nearest to the partner fights the duel.</summary>
    static void Accept(Run run, SimRoot sim, int player, PendingEvent page, List<string> lines)
    {
        var p = P(run);
        if (p.Ship is not { } id || sim.Ships[id] is not { } ship) { End(run, sim, leave: false); return; }
        var champion = sim.Ships.HumanPlayerSignificantShips.Where(s => s.Commands.CanExecuteCrewCommandsFrom(player))
            .OrderBy(s => HunterOrders.EdgeDistance(s, ship)).ThenBy(s => s.UniqueID).FirstOrDefault();
        if (champion is null) { End(run, sim, leave: true); return; }
        p.Champion = champion.UniqueID;
        p.Phase = PartnerPhase.Duel;
        FriendlyArea.Say(run.Game, Text("PartnerDuelFmt", ship.Metadata.XmlShipName, champion.Metadata.XmlShipName));
    }

    /// <summary>Spec 493, 494, 497: the duel goes on until the partner yields, a side is gone, or someone else joins in.</summary>
    static void Duel(Run run, SimRoot sim, PartnerState p, Ship? ship)
    {
        var t = run.T.Friendly;
        var champion = p.Champion is { } c ? sim.Ships[c] : null;
        if (ship is not { IsJunk: false })
        {
            FriendlyArea.Say(run.Game, Text("PartnerDeadFmt", p.Name));
            run.State.Friendly.Partner = new PartnerState();
            return;
        }
        if (champion is not { IsJunk: false, IsHumanPlayerShip: true })
        {
            FriendlyArea.Say(run.Game, Text("PartnerWonFmt", ship.Metadata.XmlShipName));
            End(run, sim, leave: true);
            return;
        }
        if (Interferer(run, sim, ship, champion) is { } other)
        {
            Log.Info($"friendly: {other.Metadata.PlainShipName} joins the duel; the partner flees");
            Revenge(run, sim, Text("PartnerCursesFmt", ship.Metadata.XmlShipName));
            return;
        }
        if (HunterOrders.HealthFraction(ship) < t.PartnerYieldHealth)
        {
            p.Phase = PartnerPhase.Yield;
            Log.Info("friendly: the partner yields");
        }
    }

    /// <summary>
    /// Spec 493: another player ship, or a ship hired by a player, that fired within the last 2 game seconds and is within
    /// PartnerInterferenceRange of the partner's hull. Limit: it does not know what that ship fired at.
    /// </summary>
    static Ship? Interferer(Run run, SimRoot sim, Ship partner, Ship champion)
    {
        float r = run.T.Friendly.PartnerInterferenceRange;
        var hired = run.State.Diplomacy.Hires.Select(h => sim.Ships[h.Ship]).OfType<Ship>();
        return sim.Ships.HumanPlayerSignificantShips.Concat(hired)
            .Where(s => s != champion && !s.IsJunk && HunterOrders.EdgeDistance(s, partner) <= r
                && PatrolListenModel.FiredRecently(sim.LogicalTime, s.Weapons.LastWeaponFireTime, 2f))
            .OrderBy(s => s.UniqueID).FirstOrDefault();
    }

    /// <summary>Spec 493, 495: the partner leaves cursing; the revenge fleet comes in PartnerRevengeMinutes.</summary>
    static void Revenge(Run run, SimRoot sim, string line)
    {
        var p = P(run);
        Leave(run, sim, p.Ship);
        FriendlyArea.Say(run.Game, line);
        p.Phase = PartnerPhase.RevengeDue;
        p.At = run.State.GameSeconds + run.T.Friendly.PartnerRevengeMinutes * 60.0;
        p.Ship = null;
        p.Champion = null;
    }

    /// <summary>Spec 495: the revenge fleet comes, hails in harsh words and attacks.</summary>
    static void Avenge(Run run, SimRoot sim)
    {
        var p = P(run);
        var fleet = Spawn(run, sim, "ScientistRevenge", RevengeTag);
        if (fleet.Count == 0) return;
        p.Fleet = fleet;
        p.Phase = PartnerPhase.Revenge;
        EventRunner.Open(run, sim, EventLibrary.Get("scientist_revenge"), lead: "", carrier: fleet[0], carrierHails: true);
        Log.Info($"friendly: the revenge fleet of {fleet.Count} ships comes for {p.Name}");
    }

    static void Leave(Run run, SimRoot sim, ObjectID? id)
    {
        if (id is { } ship && sim.Ships[ship] is not null && !run.State.Diplomacy.Leaving.Any(l => l.Ship == ship))
            run.State.Diplomacy.Leaving.Add(new Leaving { Ship = ship, Due = run.State.GameSeconds + LeaveDelaySeconds });
    }

    /// <summary>The chain ends; the partner's ship jumps out when leave is set.</summary>
    static void End(Run run, SimRoot sim, bool leave)
    {
        if (leave) Leave(run, sim, P(run).Ship);
        run.State.Friendly.Partner = new PartnerState();
    }

    /// <summary>Spec 497: a jump ends a challenge, a duel or a revenge attack. The timers of a coming partner or fleet go on.</summary>
    static void OnSystemEntered(Run run, bool firstVisit)
    {
        if (P(run).Phase is PartnerPhase.Challenge or PartnerPhase.Duel or PartnerPhase.Yield or PartnerPhase.Revenge)
            run.State.Friendly.Partner = new PartnerState();
    }

    /// <summary>
    /// Spec 492, 494, a hunter override: the partner keeps still with its weapons off while it challenges or yields, and attacks the
    /// champion in the duel.
    /// The revenge fleet keeps the hunter AI. AI tick: sim state only.
    /// </summary>
    static bool Override(Ship ship)
    {
        if (Run.Current is not { } run || ship.Sim is not { } sim || run.Game.Sim != sim) return false;
        var p = P(run);
        if (p.Ship != ship.UniqueID) return false;
        // Its guns stay cold while it challenges or yields, also with a target left over from the duel. Set each AI tick, so a load keeps it.
        ship.Weapons.SuppressAllWeapons = p.Phase is PartnerPhase.Challenge or PartnerPhase.Yield;
        if (p.Phase == PartnerPhase.Duel && p.Champion is { } c && sim.Ships[c] is { } champion)
        {
            HunterOrders.Attack(ship, champion);
            return true;
        }
        if (p.Phase is PartnerPhase.Challenge or PartnerPhase.Yield)
        {
            HunterOrders.MoveTo(ship, ship.DetWorldCenter, 200f);
            return true;
        }
        return false;
    }

    /// <summary>Spec 492, 495: the partner's allegiance is hostile to the players only in the duel and the revenge, and never to anyone else.</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.AreEnemies))]
    static class AreEnemiesPatch
    {
        static void Postfix(CareerSimModeManager __instance, int player1, int player2, ref bool __result)
        {
            if (player1 != Allegiance && player2 != Allegiance) return;
            int other = player1 == Allegiance ? player2 : player1;
            bool hostilePhase = Run.Current?.State.Friendly.Partner.Phase is PartnerPhase.Duel or PartnerPhase.Revenge;
            __result = hostilePhase && other != Allegiance && __instance.GameMode.IsHumanPlayerIndex(other);
        }
    }
}

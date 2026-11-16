using Cosmoteer.Data;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Diplomacy;

namespace HotCargo.Friendly;

/// <summary>Test commands of the scientist chain (spec 499). Partner phases as numbers: None 0, Due 1, Challenge 2, Duel 3, Yield 4, RevengeDue 5, Revenge 6.</summary>
static class ScientistAutotest
{
    static readonly ID<ResourceRules> Processor = new("processor");
    static int _techs, _processors;
    static string _name = "";

    static Run R() => Autotest.RequireRun();
    static SimRoot Sim() => R().Game.Sim;
    static Ship Me() => Sim().Ships.HumanPlayerSignificantShips.OrderBy(s => s.UniqueID).First();
    static Ship Partner() => (R().State.Friendly.Partner.Ship is { } id ? Sim().Ships[id] : null) ?? throw new InvalidOperationException("no partner ship");
    static int Techs() => GameApp_Techs().Count(t => R().Mode.Techs.IsTechUnlocked(t.ID));
    static IEnumerable<Cosmoteer.Modes.Career.TechTree.TechRules> GameApp_Techs() => Cosmoteer.GameApp.Rules.CareerMode.Techs;
    static int Processors() => Sim().Ships.HumanPlayerSignificantShips.Sum(s => s.Resources.GetUsableConstructionResourceTotal(Processor, false));

    internal static void Register()
    {
        // sc_mark: notes the unlocked techs, the fleet's processors and the first scientist's name.
        Autotest.Commands["sc_mark"] = _ =>
        {
            _techs = Techs();
            _processors = Processors();
            _name = R().State.Friendly.Scientists.FirstOrDefault()?.Name ?? _name;
        };
        // sc_processors <n>: puts n processors into the first player ship.
        Autotest.Commands["sc_processors"] = arg =>
        {
            int n = int.Parse(arg);
            int added = Me().Resources.AddResources(Processor, n, excludeConstructionSinks: true);
            Autotest.Check(added == n, $"{n} processors fit into {Me().Metadata.PlainShipName} (added {added})");
        };
        // sc_offer: the scientist offer opens on the nearest trade station, which hails, as the Station trigger does (screenshots).
        Autotest.Commands["sc_offer"] = _ =>
        {
            var tag = new ID<Cosmoteer.Generators.Simulation.SimObjectSpawner>("trade_station");
            var station = Sim().Ships.Where(s => s.HasTag(tag)).OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(Me().DetWorldCenter)).First();
            Events.EventRunner.Open(R(), Sim(), Events.EventLibrary.Get("scientist"), lead: "", carrier: station.UniqueID, carrierHails: true);
            Autotest.Wait(1);
        };
        // sc_kill: the first scientist dies.
        Autotest.Commands["sc_kill"] = _ =>
        {
            var st = R().State.Friendly.Scientists.FirstOrDefault() ?? throw new InvalidOperationException("no scientist aboard");
            (Sim().Crew[st.Soul] ?? throw new InvalidOperationException("the scientist is gone")).Delete(killed: true);
        };
        // sc_hurt: every part of the partner's ship drops to a fifth of its health (below the yield health, nothing destroyed).
        Autotest.Commands["sc_hurt"] = _ => { foreach (var part in Partner().Parts.ToList()) part.Health = Math.Max(1, part.Rules.MaxHealth / 5); };
        // sc_ally: a friendly ship spawns next to the partner, a player hires it, and it attacks the partner (spec 493).
        Autotest.Commands["sc_ally"] = _ =>
        {
            var partner = Partner();
            Autotest.Commands["dip_spawn"]("friend 300");
            var ally = Sim().Ships.Where(s => !s.IsHumanPlayerShip && !s.IsJunk && s != partner && s.Metadata.PlayerIndex != Scientist.Allegiance)
                .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(Me().DetWorldCenter)).First();
            Autotest.Teleport(ally, partner.DetWorldCenter + new Halfling.Geometry.Vector2(partner.WorldBoundingRadius + ally.WorldBoundingRadius + 100f, 0));
            R().State.Diplomacy.Hires.Add(new Hire { Ship = ally.UniqueID, Player = R().Game.HumanPlayersIndexes.Min(), Until = R().State.GameSeconds + 600 });
            Hunters.HunterOrders.Attack(ally, partner);
            Log.Info($"AUTOTEST {ally.Metadata.PlainShipName} is hired and attacks the partner");
        };
        Autotest.Fields["sc_scientists"] = () => R().State.Friendly.Scientists.Count;
        Autotest.Fields["sc_phase"] = () => R().State.Friendly.Scientists.FirstOrDefault() is { } st ? (int)st.Phase : -1;
        Autotest.Fields["sc_partner"] = () => (int)R().State.Friendly.Partner.Phase;
        Autotest.Fields["sc_fleet"] = () => R().State.Friendly.Partner.Fleet.Count;
        Autotest.Fields["sc_techs_delta"] = () => Techs() - _techs;
        Autotest.Fields["sc_processors_delta"] = () => Processors() - _processors;
        // 1 when an AI ship has the marked scientist in its crew (they defected and landed there).
        Autotest.Fields["sc_landed"] = () => Sim().Ships.Any(s => !s.IsHumanPlayerShip && s.Crew.OnShip.Any(c => c.Soul.Name == _name)) ? 1 : 0;
        Autotest.Fields["sc_hostile"] = () => ((CareerSimModeManager)Sim().Mode).AreEnemies(Scientist.Allegiance, R().Game.LocalPlayerIndex) ? 1 : 0;
    }
}

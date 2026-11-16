using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Events;
using HotCargo.Hunters;

namespace HotCargo.Friendly;

/// <summary>
/// Spec 480 to 485: the troublemaker. A ship pays the players to take a troublemaker aboard. The troublemaker joins as one crew member.
/// A seeded roll decides if they are a thief: then, at the next station visit, they steal twice the pay and slip off. The state lives in
/// the run state and changes only in sim logic (the choice input and the sim tick).
/// </summary>
static class Troublemaker
{
    static readonly ID<SimObjectSpawner> StationTag = new("trade_station");
    /// <summary>Game seconds between two station checks.</summary>
    const double CheckSeconds = 1;

    static FriendlyState S(Run run) => run.State.Friendly;

    /// <summary>
    /// Spec 481: effect "troublemaker" (the accept). The chooser gets TroublemakerPay event money units, split like a mission reward. One crew
    /// member joins a ship of the chooser with a free bunk, and the roll from the page seed decides if they are a thief. The carrier leaves.
    /// </summary>
    internal static void Apply(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines)
    {
        var t = run.T.Friendly;
        int pay = EventRunner.Credits(run, t.TroublemakerPay, 0f, p.Tier, 0);
        EventRunner.ChangeMoney(run, player, pay, lines);
        var ship = sim.Ships.HumanPlayerSignificantShips.Where(s => s.Commands.CanExecuteCrewCommandsFrom(player) && s.Crew.RemainingCrewCapacity > 0)
            .OrderBy(s => s.UniqueID).FirstOrDefault()
            ?? throw new InvalidOperationException("troublemaker: no ship of the chooser has a free bunk (the choice needs Bunks = 1)");
        var before = ship.Crew.Assigned.Select(c => c.UniqueID).ToHashSet();
        if (ship.Crew.CreateCrewsAtRandomParts(1) != 1) throw new InvalidOperationException($"troublemaker: no crew member could join {ship.Metadata.PlainShipName}");
        var soul = ship.Crew.Assigned.Single(c => !before.Contains(c.UniqueID));
        bool thief = new FastRand(p.Seed, 0x5452_4F55UL).Single() < t.TroublemakerThiefChance;
        S(run).Troublemakers.Add(new TroublemakerState { Soul = soul.UniqueID, Player = player, Pay = pay, Thief = thief, Name = soul.Name });
        lines.Add(EventText.Crew(1));
        FriendlyArea.Leave(run, sim, player, p, lines);
        Log.Info($"friendly: troublemaker {soul.Name} joins {ship.Metadata.PlainShipName} for {pay} credits; thief {thief}");
    }

    /// <summary>
    /// Spec 482 to 484, sim tick: a troublemaker who died is forgotten. At the first station visit (their ship within
    /// TroublemakerStationDistance of a trade station's hull) a thief steals and slips off; an honest one stays as normal crew.
    /// </summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("troublemaker tick", () =>
    {
        if (sim != run.Game.Sim || S(run).Troublemakers.Count == 0) return;
        double now = run.State.GameSeconds;
        if (Math.Floor(now / CheckSeconds) == Math.Floor((now - dt) / CheckSeconds)) return;
        var t = run.T.Friendly;
        foreach (var tm in S(run).Troublemakers.ToList())
        {
            if (sim.Crew[tm.Soul] is not { HomeShip: { IsHumanPlayerShip: true } home } soul)
            {
                S(run).Troublemakers.Remove(tm);
                Log.Info($"friendly: troublemaker {tm.Name} is gone before a station visit (dead or left)");
                continue;
            }
            var station = sim.Ships.Where(s => s.HasTag(StationTag) && !s.IsJunk && HunterOrders.EdgeDistance(s, home) <= t.TroublemakerStationDistance)
                .OrderBy(s => s.UniqueID).FirstOrDefault();
            if (station is null) continue;
            S(run).Troublemakers.Remove(tm);
            if (!tm.Thief)
            {
                Log.Info($"friendly: troublemaker {tm.Name} stays on at {station.Metadata.PlainShipName}");
                continue;
            }
            // Spec 483: from the player who took them in; a loss stops at 0 credits.
            int money = run.Game.GetPlayerState(tm.Player, deterministic: true).Money;
            int stolen = Math.Min((int)MathF.Round(tm.Pay * t.TroublemakerTheftFactor), Math.Max(0, money));
            EventRunner.ChangeMoney(run, tm.Player, -stolen, []);
            soul.Delete(killed: false);
            FriendlyArea.Say(run.Game, FriendlyArea.Text("TroublemakerFledFmt", tm.Name, stolen.ToString("N0"), station.Metadata.XmlShipName));
            Log.Info($"friendly: troublemaker {tm.Name} steals {stolen} credits and slips off at {station.Metadata.PlainShipName}");
        }
    });
}

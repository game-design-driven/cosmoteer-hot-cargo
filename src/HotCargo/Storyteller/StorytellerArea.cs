using System.Globalization;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Storyteller;

/// <summary>
/// Entry point of the storyteller (spec 1720 to 1731): adapt minutes rise with game time and fall with losses, and the pressure from
/// them scales hunter stage responses and the Strike interval (Pursuit/Director.cs).
/// </summary>
public static class StorytellerArea
{
    /// <summary>Game seconds between two updates of the adapt minutes.</summary>
    const double UpdateSeconds = 1.0;

    public static void Register()
    {
        Run.Hooks.Tick += (run, sim, _) => Errors.Guard("storyteller tick", () => Update(run, sim));
        Run.Hooks.SystemEntered += OnSystemEntered;
        // Spec 1732: the pressure scales the other fights too. By game, not by sim: a new system generates in a new sim.
        Scaling.EnemyScale.FightPressure = sim => Run.Current is { } run && run.Game == sim.Game && run.T.Storyteller.AllFightsEnabled ? Pressure(run) : 1;
        AddAutotest();
    }

    /// <summary>Spec 1720, 1724, 1725: the pressure now. 1 while the storyteller or the Pursuit is off.</summary>
    public static float Pressure(Run run)
    {
        var t = run.T.Storyteller;
        if (!t.Enabled || !run.T.Pursuit.Enabled) return 1f;
        return StorytellerModel.Pressure(run.State.Storyteller.AdaptMinutes, Impact(run), t.Curve);
    }

    /// <summary>Spec 1725: the impact of the career's combat difficulty level; 1 for a level without an entry.</summary>
    static float Impact(Run run)
    {
        string level = run.Mode.CombatDifficulty.ID.ToString();
        return run.T.Storyteller.Impact.FirstOrDefault(i => i.Level == level)?.Impact ?? 1f;
    }

    /// <summary>Spec 1721 to 1723. Sim tick: time and losses since the last update. The first update after a system entry only sets the baselines.</summary>
    static void Update(Run run, SimRoot sim)
    {
        if (sim != run.Game.Sim || !run.T.Storyteller.Enabled) return;
        var s = run.State.Storyteller;
        double now = run.State.GameSeconds;
        if (s.LastUpdate >= 0 && now - s.LastUpdate < UpdateSeconds) return;
        var ships = sim.Ships.HumanPlayerSignificantShips.Select(x => x.UniqueID).OrderBy(id => id).ToList();
        int killed = sim.Ships.HumanPlayerSignificantShips.Sum(x => x.Crew.KilledCrew);
        if (s.LastUpdate < 0)
        {
            (s.LastUpdate, s.HumanShips, s.KilledCrew) = (now, ships, killed);
            return;
        }
        var current = ships.ToHashSet();
        int lost = s.HumanShips.Count(id => !current.Contains(id));
        int newlyKilled = Math.Max(0, killed - s.KilledCrew);
        var t = run.T.Storyteller;
        float before = s.AdaptMinutes;
        s.AdaptMinutes = StorytellerModel.Next(before, (now - s.LastUpdate) / 60.0, lost, newlyKilled, run.State.SectorIndex <= 1,
            t.AdaptPerMinute, t.AdaptLossShip, t.AdaptLossCrew, t.Curve);
        (s.LastUpdate, s.HumanShips, s.KilledCrew) = (now, ships, killed);
        s.VisitShipsLost += lost;
        s.VisitCrewKilled += newlyKilled;
        if (lost > 0 || newlyKilled > 0)
            Log.Info($"storyteller: {lost} ships lost, {newlyKilled} crew killed: adapt {before:0.0} -> {s.AdaptMinutes:0.0}, pressure {Pressure(run):0.00}");
    }

    /// <summary>
    /// Spec 1730: one telemetry event per system visit, with the losses of the last visit. Ships that stay in the old system are not lost,
    /// so the next update sets new baselines.
    /// </summary>
    static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = run.State.Storyteller;
        float pressure = Pressure(run), impact = Impact(run);
        Telemetry.Tele.Emit(Telemetry.Cat.Story, "story.profile", (s, pressure, impact, run.State.SectorIndex), static (w, x) =>
        {
            w.WriteNumber("adapt", Math.Round(x.s.AdaptMinutes, 2));
            w.WriteNumber("pressure", Math.Round(x.pressure, 3));
            w.WriteNumber("impact", x.impact);
            w.WriteNumber("sector", x.SectorIndex);
            w.WriteNumber("ships_lost", x.s.VisitShipsLost);
            w.WriteNumber("crew_killed", x.s.VisitCrewKilled);
        });
        Log.Info($"storyteller: system entry, adapt {s.AdaptMinutes:0.0}, pressure {pressure:0.00}");
        (s.LastUpdate, s.VisitShipsLost, s.VisitCrewKilled) = (-1, 0, 0);
        s.HumanShips.Clear();
    }

    static void AddAutotest()
    {
        // story_set <field> <value>: sets one Storyteller tunable for this run. story_adapt <minutes>: sets the adapt minutes (sim state:
        // use "mp" in co-op). Fields: story_adapt, story_pressure (×1000, rounded), story_ships_lost (this visit).
        Autotest.Commands["story_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Storyteller, arg);
        Autotest.Commands["story_adapt"] = arg => Autotest.RequireRun().State.Storyteller.AdaptMinutes = float.Parse(arg, CultureInfo.InvariantCulture);
        Autotest.Fields["story_adapt"] = () => Autotest.RequireRun().State.Storyteller.AdaptMinutes;
        Autotest.Fields["story_pressure"] = () => Math.Round(Pressure(Autotest.RequireRun()) * 1000);
        Autotest.Fields["story_last_mix"] = () => Math.Round(Pursuit.Director.LastMix * 1000);
        Autotest.Fields["story_ships_lost"] = () => Autotest.RequireRun().State.Storyteller.VisitShipsLost;
        // story_lose_ship: the newest human ship that is not the first one leaves the sim, as if destroyed (a test of spec 1723).
        Autotest.Commands["story_lose_ship"] = _ =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            var ships = sim.Ships.HumanPlayerSignificantShips.OrderBy(x => x.UniqueID).ToList();
            if (ships.Count < 2) throw new InvalidOperationException("story_lose_ship needs a second human ship (aiplayership)");
            var ship = ships[^1];
            Log.Info($"AUTOTEST story_lose_ship: {ship.Metadata.PlainShipName} leaves the sim");
            sim.Ships.Remove(ship, dispose: true, handleLostShip: false);
        };
    }
}

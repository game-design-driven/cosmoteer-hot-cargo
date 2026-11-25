using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Missions.Mechanics;
using HotCargo.Pursuit;

namespace HotCargo.Missions;

/// <summary>Spec 2430: the mayday that is due. Saved with the run, so a load and a co-op resync keep it.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class MaydayState
{
    /// <summary>The system where a mayday is due; null when none is.</summary>
    [Serialize(Optional = true)] public ObjectID? Node;
    /// <summary>Run game seconds when it starts.</summary>
    [Serialize(Optional = true)] public double At;
}

/// <summary>
/// Spec 2430 to 2434: a live distress fight. On arrival a mayday may be due. When it starts, a civilian ship and its attackers spawn in
/// sight of the players, and the mission enters the log by itself (<see cref="MaydayObjective"/>).
/// </summary>
internal static class Mayday
{
    internal static readonly ID<MissionMetatype> Metatype = new("HotCargoMayday");
    /// <summary>The victim has the convoy freighter tag and the attackers the convoy raider tag, so the attackers want the victim (spec 36, 1885).</summary>
    static readonly ID<SimObjectSpawner> FreighterTag = new("hc_escort_ship"), RaiderTag = new("hc_escort_raider"), VictimTag = new("hc_mayday_victim");

    internal static void Register()
    {
        Run.Hooks.SystemEntered += (run, _) => Errors.Guard("mayday arrival", () => OnArrival(run));
        Run.Hooks.Tick += (run, sim, _) => Errors.Guard("mayday", () => Tick(run, sim));
    }

    /// <summary>
    /// Spec 2430, 2434: on arrival, a mayday is due with MaydayChance, MaydayDelaySeconds later (sim random stream). Not in an autotest: a
    /// roll there would move the random stream of every test and drop fights into them; a test rolls with <see cref="Roll"/>.
    /// </summary>
    static void OnArrival(Run run)
    {
        if (Autotest.IsActive || run.Game.Sim is not { } sim) return;
        Roll(run, sim, run.T.Missions.MaydayChance);
    }

    /// <summary>The arrival roll with the given chance. Internal for the autotest.</summary>
    internal static void Roll(Run run, SimRoot sim, float chance)
    {
        var t = run.T.Missions;
        var state = run.State.Mayday;
        state.Node = null;
        if (sim.Rand.Single() >= chance) return;
        state.Node = sim.NodeId();
        state.At = run.State.GameSeconds + t.MaydayDelaySeconds;
        Log.Info($"mayday due in {t.MaydayDelaySeconds:0} s");
    }

    static void Tick(Run run, SimRoot sim)
    {
        var state = run.State.Mayday;
        if (state.Node is not { } node || node != sim.NodeId() || run.State.GameSeconds < state.At) return;
        state.Node = null;
        if (!Pacer.MayStart(run, PacedKinds.Mayday))
        {
            Log.Info("mayday skipped: the big event pacer says no");
            return;
        }
        Start(run, sim);
    }

    /// <summary>
    /// Spec 2431: spawns the victim and its attackers in sight of the players and puts the mission into the log. Null when the system has
    /// no station of a faction that is not hostile to the players, or no player ship. Sim tick only (sim.Rand). Internal for the autotest.
    /// </summary>
    internal static Mission? Start(Run run, SimRoot sim)
    {
        var t = run.T.Missions;
        var game = run.Game;
        var mode = (CareerSimModeManager)sim.Mode;
        var player = sim.Ships.HumanPlayerSignificantShips.OrderBy(s => s.UniqueID).FirstOrDefault();
        if (player is null) return Skip("no player ship");
        var humans = game.HumanPlayersIndexes;
        // The nearest station of a faction that is not hostile to the players: its faction owns the victim, and it pays. It must give
        // missions: the pay step (vanilla ReceiveRewardsObjective) asks its AI for pending rewards, and a station without a mission
        // module (a military station) throws there, which crashed both co-op machines when the mayday was won (#313).
        var home = SpaceStation.GetStations(sim, onlyOperating: true)
            .Where(s => s.GetProvidedMissions() is not null)
            .Select(s => (Station: s, Faction: FactionRules.GetShipFaction(s.MetaShip)))
            .Where(x => x.Faction is { } f && !humans.Any(h => sim.Mode.AreEnemies(f.CivilianPlayerIndex, h)))
            .OrderBy(x => x.Station.DetWorldCenter.DistanceTo(player.DetWorldCenter)).ThenBy(x => x.Station.StationID)
            .FirstOrDefault();
        if (home.Faction is not { } faction) return Skip("no station that gives missions, of a faction that is not hostile");
        int tier = Math.Max(1, mode.TierFields.GetTier(player.DetWorldCenter));

        Vector2 at = WaveSpawns.Location(sim, player.DetWorldCenter, sim.Rand.Direction(), t.MaydayDistanceMin, t.MaydayDistanceMax);
        var hull = WaveSpawns.PickShipNearTier(sim, [.. t.MaydayVictimTags.Select(s => new ID<BuiltinShipTag>(s))], tier);
        var spawner = new StasisShipSpawner(sim, hull.ID, at, SpawnedShipFeatures.None, sim.Rand.UInt64(), [FreighterTag, VictimTag])
        {
            AI = new ID<ShipAIRules>("Normal"),
            // A small unacquire range keeps it flying to its origin, the nearest player ship (MaydayObjective moves the origin).
            AIParameters = new AIParameters
            {
                PatrolOrigin = player.DetWorldCenter,
                PatrolRadius = 100f,
                TargetAcquireRangeFromOrigin = 150f,
                TargetUnacquireRangeFromOrigin = 150f,
                ChanceToConsiderSurrenderingToPlayer = 0f,
            },
            Direction = at.DirectionTo(player.DetWorldCenter),
            ObjectAllegiance = faction.CivilianPlayerIndex,
        };
        spawner.Spawn(sim);
        var victim = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"mayday victim {hull.ID} did not spawn");

        int attackers = mode.AllocUniquePlayerIndex();
        int count = Scaling.EnemyScale.FightCount(t.MaydayRaiders, sim);
        int attackerTier = Math.Max(1, tier + t.MaydayRaiderTierOffset);
        WaveSpawns.Spawn(sim, victim.DetWorldCenter, sim.Rand.Direction(), count, attackerTier, t.MaydayRaiderTags, attackers, RaiderTag,
            t.MaydayRaiderDistanceMin, t.MaydayRaiderDistanceMax);

        var mission = new Mission(game, Metatype, faction.ID);
        var objective = new MaydayObjective(new ObjectiveTargetShip(game, victim), attackers, tier);
        objective.ShowRing(sim);
        mission.AddObjective(new SequentialObjective([objective, new ReceiveRewardsObjective(new ObjectiveTargetShip(sim, home.Station.MetaShip))]));
        mission.AddReward(new MoneyReward(mission, (int)Math.Round(GameData.StandardMissionMoney(tier) * t.MaydayMoneyFactor)));
        mission.AddReward(new FameReward((int)Math.Round(GameData.StandardMissionFame(tier) * t.MaydayFameFactor)));
        run.Mode.Missions.AddMission(mission, track: true);
        Pacer.Started(run, PacedKinds.Mayday);

        Log.Info($"mayday: {victim.Metadata.PlainShipName} ({hull.ID}, {faction.ID}) at {at}, {count} tier-{attackerTier} attackers, pay at {home.Station.StationID}");
        Ux.UxKit.ChatLine(game, Strings.FormatText("HotCargoMissions/MaydayStartFmt", victim.Metadata.XmlShipName));
        Ux.ShipAlerts.Banner(game, Strings.FormatText("HotCargoMissions/MaydayStartFmt", victim.Metadata.PlainShipName), Cosmoteer.Gui.WidgetRules.Instance.BadTextColor);
        return mission;

        Mission? Skip(string why)
        {
            Log.Info($"mayday skipped: {why}");
            return null;
        }
    }
}

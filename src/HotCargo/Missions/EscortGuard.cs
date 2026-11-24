using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Missions.Mechanics;

namespace HotCargo.Missions;

/// <summary>Sim state of the guard rule of one escort (spec 1881 to 1884). Saved with its objective, so a load and a co-op resync keep it.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class EscortGuardState
{
    /// <summary>Game seconds that the escort under way has been without a player ship near.</summary>
    [Serialize(Optional = true)] public double ExposedSeconds;
    /// <summary>This time alone already drew its raid.</summary>
    [Serialize(Optional = true)] public bool Raided;
    /// <summary>Player index of this escort's raiders. 0 until the first raid.</summary>
    [Serialize(Optional = true)] public int RaidAllegiance;
    /// <summary>Where the escort holds while no player ship is near. Null while it is guarded.</summary>
    [Serialize(Optional = true)] public Vector2? HoldAt;
    /// <summary>The last tick found a player ship near. Sim state, so the HUD line is the same on every machine.</summary>
    [Serialize(Optional = true)] public bool Guarded = true;
}

/// <summary>
/// Spec 1881 to 1885: the guard rule of every escort under way (Convoy Duty, Quiet Freight, Navy Tender, the Mining Escort).
/// The escort moves only while a player ship is near. Alone, it draws raiders. Alone and in stasis, it is lost.
/// Reason: the game freezes every ship far from all player ships (stasis), and raiders that spawn there too. The escort is not a player
/// ship, so it cannot wake them. Before this rule an escort flew alone past frozen raiders and arrived.
/// </summary>
internal static class EscortGuard
{
    /// <summary>Spec 1881: the hull of a player ship (significant ships) is within EscortGuardRadius of the escort's hull. In stasis the escort has no hull, so its center counts.</summary>
    internal static bool IsGuarded(SimRoot sim, ObjectiveTargetShip escort)
    {
        if (escort.TargetMetaShip is not { } meta) return false;
        Circle e = meta.DetWorldBoundingCircle;
        float radius = MissionArea.TunablesFor(sim.RequireGame()).EscortGuardRadius;
        return sim.Ships.HumanPlayerSignificantShips.Any(s => MechanicsModel.Gap(s.DetWorldCenter.DistanceTo(e.Center), s.DetWorldBoundingCircle.Radius, e.Radius) <= radius);
    }

    /// <summary>
    /// One sim tick of the guard rule for an escort under way. Sets the hold point when the escort is alone, spawns a raid or removes the
    /// escort when the clock asks for it, and returns true when the escort may move. <paramref name="changed"/> runs when the HUD line changes.
    /// </summary>
    internal static bool Tick(SimRoot sim, ObjectiveTargetShip escort, EscortGuardState state, int tier, ID<SimObjectSpawner> raidTag, Action changed)
    {
        var t = MissionArea.TunablesFor(sim.RequireGame());
        bool guarded = IsGuarded(sim, escort);
        Ship? ship = escort.TargetShip;
        if (guarded != state.Guarded)
        {
            state.Guarded = guarded;
            Log.Info($"escort {escort.GetPlainTargetName(sim)} {(guarded ? "guarded again" : "alone: it waits")}");
            changed();
        }
        state.HoldAt = guarded ? null : state.HoldAt ?? escort.DetLocation();
        switch (MissionModel.GuardStep(ref state.ExposedSeconds, ref state.Raided, sim.FixedUpdater.Interval.Seconds, guarded, ship is not null, t.EscortRaidSeconds, t.EscortLostSeconds))
        {
            case EscortGuardEvent.Raid:
                Errors.Guard("escort raid", () => Raid(sim, ship!, state, tier, raidTag, t));
                changed();
                break;
            case EscortGuardEvent.Lost:
                Lose(sim, escort);
                break;
        }
        return guarded;
    }

    /// <summary>Spec 1882: one raid around the lone escort, on this escort's raider allegiance. Sim tick only (sim.Rand).</summary>
    static void Raid(SimRoot sim, Ship escort, EscortGuardState state, int tier, ID<SimObjectSpawner> tag, MissionTunables t)
    {
        if (state.RaidAllegiance == 0) state.RaidAllegiance = ((CareerSimModeManager)sim.Mode).AllocUniquePlayerIndex();
        int live = SimQueries.TaggedShips(sim, tag).Count(s => s.PlayerIndex == state.RaidAllegiance);
        // Spec 334: co-op scales the raid and the live cap.
        int count = Math.Clamp(Scaling.EnemyScale.FightCount(t.EscortRaidShips, sim), 0, Math.Max(0, Scaling.EnemyScale.FightCap(t.EscortRaidMaxLive, sim) - live));
        int shipTier = Math.Max(1, tier + t.EscortRaidTierOffset);
        WaveSpawns.Spawn(sim, escort.DetWorldCenter, sim.Rand.Direction(), count, shipTier, t.EscortRaidShipTags, state.RaidAllegiance, tag, t.EscortRaidDistanceMin, t.EscortRaidDistanceMax);
        Log.Info($"escort {escort.Metadata.PlainShipName} raided: {count} tier-{shipTier} ships ({live} still live) after {state.ExposedSeconds:0} s alone");
        Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/EscortRaidFmt", escort.Metadata.XmlShipName));
    }

    /// <summary>
    /// Spec 1883: the escort in stasis is lost to raiders. Removing its stasis spawner makes the game report the target ship killed
    /// (ObjectiveTargetShip.Killed), so the objective and its ProtectShips fail the normal way.
    /// </summary>
    static void Lose(SimRoot sim, ObjectiveTargetShip escort)
    {
        var spawner = escort.TargetSpawner ?? throw new InvalidOperationException("a lost escort must be in stasis");
        string name = escort.GetPlainTargetName(sim);
        Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/EscortLostFmt", escort.GetXmlTargetName(sim)));
        Log.Info($"escort {name} lost to raiders while alone in stasis");
        sim.Stasis.RemoveStasisSpawner(spawner);
    }

    /// <summary>Spec 1881: the HUD line under the escort's line while it is alone, or null while it is guarded.</summary>
    internal static Objective.DisplayLine? AloneLine(EscortGuardState state, string escortName) => state.Guarded ? null : new Objective.DisplayLine
    {
        Text = Strings.KeyStringFmt(state.Raided ? "HotCargoObjectives/EscortRaidedFmt" : "HotCargoObjectives/EscortStayCloseFmt", escortName),
        State = CompletionState.InProgress,
    };
}

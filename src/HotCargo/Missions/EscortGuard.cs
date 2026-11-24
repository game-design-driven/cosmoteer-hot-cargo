using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling.Geometry;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Missions.Mechanics;

namespace HotCargo.Missions;

/// <summary>Sim state of the guard rule of one escort (spec 1881 to 1886). Saved with its objective, so a load and a co-op resync keep it.</summary>
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
    /// <summary>Spec 1886: this escort's time alone before it is lost, from its rules (the spawner's LostSeconds). 0: Missions.EscortLostSeconds.</summary>
    [Serialize(Optional = true)] public float LostSeconds;
    /// <summary>Spec 1886: the warning ring that follows the escort while it is alone. Null while it is guarded.</summary>
    [Serialize(Optional = true)] public ObjectID? MarkerId;

    /// <summary>The time alone before the escort is lost: its own LostSeconds, else the tunable.</summary>
    public float LostAfter(MissionTunables t) => LostSeconds > 0f ? LostSeconds : t.EscortLostSeconds;
}

/// <summary>
/// Spec 1881 to 1886: the guard rule of every escort under way (Convoy Duty, Quiet Freight, Navy Tender, the Mining Escort).
/// The escort moves only while a player ship is near. Alone, a clock runs: it draws raiders, and when the clock runs out, raiders take it.
/// Reason: the game freezes every ship far from all player ships (stasis), and raiders that spawn there too. The escort is not a player
/// ship, so it cannot wake them. Before this rule an escort flew alone past frozen raiders and arrived.
/// </summary>
internal static class EscortGuard
{
    /// <summary>Spec 1886: the warning ring, a red copy of the vanilla target area ring (mod/missions/escort_alone.rules).</summary>
    static readonly ID<DoodadRules> AloneRing = new("yarden_zamir.poi_escort_alone");

    /// <summary>Spec 1881: the hull of a player ship (significant ships) is within EscortGuardRadius of the escort's hull. In stasis the escort has no hull, so its center counts.</summary>
    internal static bool IsGuarded(SimRoot sim, ObjectiveTargetShip escort)
    {
        if (escort.TargetMetaShip is not { } meta) return false;
        Circle e = meta.DetWorldBoundingCircle;
        float radius = MissionArea.TunablesFor(sim.RequireGame()).EscortGuardRadius;
        return sim.Ships.HumanPlayerSignificantShips.Any(s => MechanicsModel.Gap(s.DetWorldCenter.DistanceTo(e.Center), s.DetWorldBoundingCircle.Radius, e.Radius) <= radius);
    }

    /// <summary>
    /// Spec 1886: a rules override of the time alone must leave room for the raid and the last warning. Throws on a bad value, so a rules
    /// error shows when the offer is made.
    /// </summary>
    internal static float CheckLostSeconds(SimRoot sim, float lostSeconds)
    {
        var t = MissionArea.TunablesFor(sim.RequireGame());
        if (lostSeconds != 0f && (lostSeconds <= t.EscortRaidSeconds || lostSeconds <= t.EscortLostWarnSeconds))
            throw new InvalidDataException($"escort LostSeconds {lostSeconds} must be 0 (the default) or above EscortRaidSeconds ({t.EscortRaidSeconds}) and EscortLostWarnSeconds ({t.EscortLostWarnSeconds})");
        return lostSeconds;
    }

    /// <summary>
    /// One sim tick of the guard rule for an escort under way. Sets the hold point while the escort is alone, moves its warning ring, warns,
    /// spawns a raid, and lets raiders take the escort when the clock runs out. Returns true when the escort may move.
    /// <paramref name="changed"/> runs when the HUD line changes; <paramref name="fail"/> runs when raiders take the escort.
    /// </summary>
    internal static bool Tick(SimRoot sim, ObjectiveTargetShip escort, EscortGuardState state, int tier, ID<SimObjectSpawner> raidTag, Action changed, Action fail)
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
        float lost = state.LostAfter(t);
        var events = MissionModel.GuardStep(ref state.ExposedSeconds, ref state.Raided, sim.FixedUpdater.Interval.Seconds, guarded, ship is not null,
            t.EscortRaidSeconds, lost, t.EscortLostWarnSeconds);
        Errors.Guard("escort alone ring", () => Ring(sim, escort, state, guarded));
        if (events.HasFlag(EscortGuardEvent.Alone)) Warn(sim, escort, "HotCargoObjectives/EscortAloneStartFmt", lost);
        if (events.HasFlag(EscortGuardEvent.Warning)) Warn(sim, escort, "HotCargoObjectives/EscortAloneWarnFmt", t.EscortLostWarnSeconds);
        if (events.HasFlag(EscortGuardEvent.Raid))
        {
            Errors.Guard("escort raid", () => Raid(sim, ship!, state, tier, raidTag, t));
            changed();
        }
        if (events.HasFlag(EscortGuardEvent.Lost))
        {
            Lose(sim, escort, state);
            fail();
            return false;
        }
        return guarded;
    }

    /// <summary>Spec 1886: a banner and a chat line with the time left. The banner is local GUI; every machine shows its own.</summary>
    static void Warn(SimRoot sim, ObjectiveTargetShip escort, string key, float seconds)
    {
        string plain = Strings.FormatText(key, escort.GetPlainTargetName(sim), Clock(seconds));
        Log.Info($"escort warning: {plain}");
        Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText(key, escort.GetXmlTargetName(sim), Clock(seconds)));
        Ux.ShipAlerts.Banner(sim.RequireGame(), plain, Cosmoteer.Gui.WidgetRules.Instance.BadTextColor);
    }

    static string Clock(double seconds)
    {
        int s = (int)Math.Max(0, Math.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>
    /// Spec 1886: while the escort is alone, a red ring of EscortMarkerRadius follows it, on the map and in space. It goes when a player
    /// ship is back, and with the objective (IMissionMarkers, spec 996). Sim state, spawned in the sim tick on every machine.
    /// </summary>
    static void Ring(SimRoot sim, ObjectiveTargetShip escort, EscortGuardState state, bool guarded) =>
        state.MarkerId = WarningRing(sim, state.MarkerId, guarded ? null : escort.DetLocation());

    /// <summary>
    /// The red warning ring of EscortMarkerRadius at the point: spawned when <paramref name="id"/> names none, moved when it does, removed
    /// when the point is null. Returns the ring's ID, or null. Sim tick only (sim.Rand for a new ID). Also the Mayday victim's ring (spec 2433).
    /// </summary>
    internal static ObjectID? WarningRing(SimRoot sim, ObjectID? id, Vector2? at)
    {
        var ring = id is { } existing ? sim.Doodads[existing] as POIDoodad : null;
        if (at is not { } point)
        {
            if (ring is not null) sim.Doodads.Remove(ring, dispose: true);
            return null;
        }
        if (ring is null)
        {
            var rules = GameApp.Rules.GetDoodadRules(AloneRing) as RingedPOIDoodadRules ?? throw new InvalidOperationException($"{AloneRing} is not a ringed POI doodad");
            ObjectID newId;
            do newId = ObjectID.NewRandom(sim.Rand); while (!sim.IsObjectIDAvailable(newId));
            ring = (POIDoodad)rules.SpawnAt(sim, point, Direction.Zero, sim.Rand, null, null, null, loadInstantly: true, id: newId,
                scaleFactor: MissionArea.TunablesFor(sim.RequireGame()).EscortMarkerRadius);
        }
        ring.Location = point;
        return ring.UniqueID;
    }

    /// <summary>Spec 1882: one raid around the lone escort, on this escort's raider allegiance. Sim tick only (sim.Rand).</summary>
    static void Raid(SimRoot sim, Ship escort, EscortGuardState state, int tier, ID<SimObjectSpawner> tag, MissionTunables t)
    {
        int count = SpawnRaid(sim, escort.DetWorldCenter, sim.Rand.Direction(), t.EscortRaidDistanceMin, t.EscortRaidDistanceMax, state, tier, tag);
        Log.Info($"escort {escort.Metadata.PlainShipName} raided: {count} ships after {state.ExposedSeconds:0} s alone");
        Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/EscortRaidFmt", escort.Metadata.XmlShipName));
    }

    /// <summary>
    /// Spec 1882: EscortRaidShips ships (scaled for co-op) of the tier + EscortRaidTierOffset jump in <paramref name="min"/> to
    /// <paramref name="max"/> from the center near the direction, on the escort's raider allegiance, capped at EscortRaidMaxLive live.
    /// Returns how many. Sim tick only (sim.Rand).
    /// </summary>
    internal static int SpawnRaid(SimRoot sim, Vector2 center, float direction, float min, float max, EscortGuardState state, int tier, ID<SimObjectSpawner> tag)
    {
        var t = MissionArea.TunablesFor(sim.RequireGame());
        int allegiance = RaidersOf(sim, state);
        int live = SimQueries.TaggedShips(sim, tag).Count(s => s.PlayerIndex == allegiance);
        // Spec 334: co-op scales the raid and the live cap.
        int count = Math.Clamp(Scaling.EnemyScale.FightCount(t.EscortRaidShips, sim), 0, Math.Max(0, Scaling.EnemyScale.FightCap(t.EscortRaidMaxLive, sim) - live));
        int shipTier = Math.Max(1, tier + t.EscortRaidTierOffset);
        WaveSpawns.Spawn(sim, center, direction, count, shipTier, t.EscortRaidShipTags, allegiance, tag, min, max);
        Log.Info($"escort raid: {count} tier-{shipTier} ships ({live} still live) at {center}");
        return count;
    }

    static int RaidersOf(SimRoot sim, EscortGuardState state)
    {
        if (state.RaidAllegiance == 0) state.RaidAllegiance = ((CareerSimModeManager)sim.Mode).AllocUniquePlayerIndex();
        return state.RaidAllegiance;
    }

    /// <summary>
    /// Spec 1883: the clock ran out, so raiders take the escort: live or in stasis, it changes to this escort's raider allegiance (hostile
    /// to the players). The caller fails the objective.
    /// </summary>
    static void Lose(SimRoot sim, ObjectiveTargetShip escort, EscortGuardState state)
    {
        int raiders = RaidersOf(sim, state);
        if (escort.TargetShip is { } ship) ship.Metadata.PlayerIndex = raiders;
        else if (escort.TargetSpawner is Cosmoteer.Simulation.Stasis.StasisShipSpawner spawner) spawner.ObjectAllegiance = raiders;
        else throw new InvalidOperationException("a lost escort must be live or in stasis");
        Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/EscortLostFmt", escort.GetXmlTargetName(sim)));
        Log.Info($"escort {escort.GetPlainTargetName(sim)} lost: raiders took it after {state.ExposedSeconds:0} s alone (live {escort.TargetShip is not null})");
    }

    /// <summary>Spec 1886: the HUD line under the escort's line while it is alone, with the time left, or null while it is guarded.</summary>
    internal static Objective.DisplayLine? AloneLine(EscortGuardState state, string escortName, GameRoot game)
    {
        if (state.Guarded) return null;
        var t = MissionArea.TunablesFor(game);
        string left = Clock(MissionModel.SecondsLeft(state.LostAfter(t), state.ExposedSeconds));
        return new Objective.DisplayLine
        {
            Text = Strings.KeyStringFmt(state.Raided ? "HotCargoObjectives/EscortRaidedFmt" : "HotCargoObjectives/EscortAloneFmt", escortName, left),
            State = CompletionState.InProgress,
        };
    }

    /// <summary>Spec 1886: the whole seconds that the HUD line shows, or -1 while guarded. A change means the line must be rebuilt.</summary>
    internal static int ShownSeconds(EscortGuardState state, GameRoot game) =>
        state.Guarded ? -1 : MissionModel.SecondsLeft(state.LostAfter(MissionArea.TunablesFor(game)), state.ExposedSeconds);
}

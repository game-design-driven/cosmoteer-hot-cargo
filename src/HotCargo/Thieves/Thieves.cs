using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Localization;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using HotCargo.Ai;
using HotCargo.Autonomy;
using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.Events;
using HotCargo.Hunters;
using HotCargo.Pursuit;
using HotCargo.Scaling;
using HotCargo.Tech;
using HotCargo.Telemetry;

namespace HotCargo.Thieves;

/// <summary>
/// Spec 1770 to 1799: pirate thieves. They fight with the pirates but aim at the players' cargo. When enough player loot floats near them,
/// the group stops fighting, collects it with crew and funnel, flees and jumps out. All of it runs in the sim tick and the AI tick and
/// reads and writes the run state (RunState.Thieves), in sim order, so every co-op machine does the same.
/// </summary>
internal static class Thieves
{
    static readonly ID<ShipAIRules> NormalAi = new("Normal"), WandererAi = new("Wanderer");
    /// <summary>Game seconds between two checks.</summary>
    const double CheckSeconds = 1;
    /// <summary>A thief that joins a group arrives this far from the group's first ship.</summary>
    static readonly Range<float> JoinDistance = new(300f, 600f);

    static ThievesState S(Run run) => run.State.Thieves;
    static string Text(string key, params object?[] args) => Strings.FormatText("HotCargoThieves/" + key, args);

    /// <summary>Spec 1771, 1780: the AIs that carry the thief modules (mod/thieves/actions.rules). Only groups that fly them get a thief.</summary>
    static bool HasThiefModules(ID<ShipAIRules> ai) => ai == NormalAi || ai == WandererAi;

    internal static ThiefState? Of(Ship ship) => Run.Current?.State.Thieves.Thieves.GetValueOrDefault(ship.UniqueID);

    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = S(run);
        s.Thieves.Clear();
        s.Rolled.Clear();
        s.EscapingCargo.Clear();
        s.EncounterDue = firstVisit ? run.State.GameSeconds + run.T.Thieves.EncounterDelaySeconds : -1;
    }

    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("thieves tick", () =>
    {
        if (sim != run.Game.Sim) return;
        double now = run.State.GameSeconds;
        if (Math.Floor(now / CheckSeconds) == Math.Floor((now - dt) / CheckSeconds)) return;
        var t = run.T.Thieves;
        Forget(run, sim);
        if (t.Enabled && run.State.Result == RunResult.Running)
        {
            Joins(run, sim, t);
            Encounter(run, sim, t, now);
        }
        Triggers(run, sim, t, now);
    });

    // ---- Spawns (spec 1780, 1781)

    /// <summary>
    /// Spec 1780: a pirate group (a hostile side of no faction and not hunters, with its own player index) rolls JoinChance the first time a
    /// ship of it is live in the system. On a success, thieves (1 × the co-op scale) of the system tier arrive beside its first ship, with its
    /// player index (so they are its allies), its AI and its AI parameters.
    /// </summary>
    static void Joins(Run run, SimRoot sim, ThievesTunables t)
    {
        var s = S(run);
        foreach (var ship in sim.Ships.ToList())
        {
            int index = ship.Metadata.PlayerIndex;
            if (!IsPirate(ship) || s.Thieves.ContainsKey(ship.UniqueID) || !s.Rolled.Add(index)) continue;
            float roll = sim.Rand.Single();
            Log.Info($"thieves: group {index} ({ship.Metadata.PlainShipName}) join roll {roll:0.000} vs {t.JoinChance:0.000}");
            if (roll >= t.JoinChance) continue;
            var ai = ship.AI!.Rules.ID;
            var parameters = ship.AI?.Parameters ?? Parameters(ship.DetWorldCenter);
            int count = EnemyScale.Count(1, sim);
            var face = SimQueries.FirstHumanShip(sim)?.DetWorldCenter ?? ship.DetWorldCenter;
            for (int i = 0; i < count; i++)
            {
                var at = HunterSpawner.SpawnLocation(sim, ship.DetWorldCenter, sim.Rand.Single(new Range<float>(0f, MathF.Tau)), MathF.PI, JoinDistance);
                Add(run, ThiefShip.Spawn(sim, run.CurrentTier, index, ai, parameters, at, at.DirectionTo(face)), index, run.CurrentTier);
            }
        }
    }

    /// <summary>
    /// A live pirate ship that flies: significant, not junk, hostile to the players, of the personality group "pirates", with a pirate AI
    /// (Normal or Wanderer, not a defense platform or station AI), and not a hunter.
    /// </summary>
    static bool IsPirate(Ship ship) =>
        ship.IsSignificant && !ship.IsJunk && !ship.IsHumanPlayerShip && ship.Metadata.PlayerIndex >= 100_000 && !ship.HasTag(HunterSpawner.HunterTag)
        && ship.AI?.Rules.ID is { } ai && HasThiefModules(ai)
        && PersonalityStrategy.Group(ship) == PersonalityStrategy.Pirates && AiGate.HostileToPlayers(ship);

    /// <summary>
    /// Spec 1781: once per first system visit, EncounterDelaySeconds after the arrival, from MinSector on and while the Pursuit pacer allows
    /// the kind Thieves: a roll of EncounterChance. On a success, EncounterMin to EncounterMax thieves (× the co-op scale) of the system tier
    /// arrive together EncounterDistanceMin to EncounterDistanceMax from the players' first ship, with a new pirate player index and the
    /// Normal AI.
    /// </summary>
    static void Encounter(Run run, SimRoot sim, ThievesTunables t, double now)
    {
        var s = S(run);
        if (s.EncounterDue < 0 || now < s.EncounterDue) return;
        s.EncounterDue = -1;
        if (run.State.SectorIndex < t.MinSector || !Pacer.MayStart(run, PacedKinds.Thieves)) return;
        float roll = sim.Rand.Single();
        Log.Info($"thieves: encounter roll {roll:0.000} vs {t.EncounterChance:0.000}");
        if (roll < t.EncounterChance) Arrive(run, sim, t);
    }

    /// <summary>Spec 1781: the thief-only encounter. Also the test command th_encounter.</summary>
    internal static List<Ship> Arrive(Run run, SimRoot sim, ThievesTunables t)
    {
        var first = sim.Ships.HumanPlayerShips.Where(x => !x.IsJunk).MinBy(x => x.UniqueID) ?? throw new InvalidOperationException("thieves: no player ship");
        int count = EnemyScale.Count(sim.Rand.Int32(t.EncounterMin, Math.Max(t.EncounterMin, t.EncounterMax)), sim);
        int index = Situation.Career(sim).AllocUniquePlayerIndex();
        float angle = sim.Rand.Single(new Range<float>(0f, MathF.Tau));
        var center = HunterSpawner.SpawnLocation(sim, first.DetWorldCenter, angle, MathF.PI, new Range<float>(t.EncounterDistanceMin, t.EncounterDistanceMax));
        var ships = new List<Ship>();
        for (int i = 0; i < count; i++)
        {
            var at = center + Vector2.FromAngle(i * MathF.Tau / count) * (i == 0 ? 0f : 400f);
            var ship = ThiefShip.Spawn(sim, run.CurrentTier, index, NormalAi, Parameters(center), at, at.DirectionTo(first.DetWorldCenter));
            Add(run, ship, index, run.CurrentTier);
            ships.Add(ship);
        }
        Pacer.Started(run, PacedKinds.Thieves);
        Log.Info($"thieves: encounter of {count} thieves (group {index})");
        return ships;
    }

    /// <summary>
    /// Spec 1781: the AI parameters of an encounter: like the vanilla ambush spawns, a small patrol around the arrival spot that acquires
    /// targets within 6,000 of it and drops them beyond 9,000, and never surrenders.
    /// </summary>
    internal static AIParameters Parameters(Vector2 origin) => new()
    {
        PatrolOrigin = origin,
        PatrolRadius = 800f,
        TargetAcquireRangeFromOrigin = 6000f,
        TargetUnacquireRangeFromOrigin = 9000f,
        ChanceToConsiderSurrenderingToPlayer = 0f,
    };

    internal static void Add(Run run, Ship ship, int group, int tier) =>
        S(run).Thieves[ship.UniqueID] = new ThiefState { Group = group, Tier = tier, Phase = ThiefPhase.Fight, Since = run.State.GameSeconds };

    // ---- Trigger (spec 1778)

    /// <summary>
    /// Spec 1778: for each group that still fights, the value of the loose resources from player parts within LootRadius of any of its live
    /// thieves. At TriggerShare × the value maximum of its tier, the whole group stops fighting and collects.
    /// </summary>
    static void Triggers(Run run, SimRoot sim, ThievesTunables t, double now)
    {
        var s = S(run);
        foreach (var group in s.Thieves.Where(kv => kv.Value.Phase == ThiefPhase.Fight).GroupBy(kv => kv.Value.Group).OrderBy(g => g.Key))
        {
            var ships = group.Select(kv => sim.Ships[kv.Key]).OfType<Ship>().Where(x => !x.IsJunk).OrderBy(x => x.UniqueID).ToList();
            if (ships.Count == 0) continue;
            long value = PlayerLoot(sim, ships, t.LootRadius);
            int tier = group.Max(kv => kv.Value.Tier);
            long threshold = ThievesModel.Threshold(t.TriggerShare, GameData.TierValueMaximum(tier));
            if (value < threshold) continue;
            foreach (var (id, th) in group) { th.Phase = ThiefPhase.Collect; th.Since = now; }
            foreach (var ship in ships) ship.Commands.RemoveAll(c => c is IAttackCommand);
            Log.Info($"thieves: group {group.Key} triggers: player loot {value} >= {threshold} (tier {tier}); {ships.Count} thieves collect");
            Ux.UxKit.ChatLine(run.Game, Text("TriggerFmt", ships[0].Metadata.XmlShipName));
            Tele.Emit(Cat.Combat, "thief.trigger", (group.Key, tier, value, threshold, ships.Count), static (w, x) =>
            {
                w.WriteNumber("group", x.Key);
                w.WriteNumber("tier", x.tier);
                w.WriteNumber("value", x.value);
                w.WriteNumber("threshold", x.threshold);
                w.WriteNumber("thieves", x.Count);
            });
        }
    }

    /// <summary>Spec 1778: loose resources (not tech crates) with the vanilla player-drop flag, within the radius of a ship's hull, in nugget order.</summary>
    internal static long PlayerLoot(SimRoot sim, List<Ship> ships, float radius)
    {
        var loose = new List<Loose>();
        for (int i = 0; i < sim.Nuggets.Count; i++)
        {
            var n = sim.Nuggets[i];
            if (n.ResourceType == Vaults.TechId) continue;
            float d = ships.Min(x => AutonomyWorld.EdgeDistance(x, n.DetWorldLocation));
            loose.Add(new Loose((n.Flags & NuggetFlags.BelongsToPlayer) != 0, d, n.Quantity, Funnel.UnitValue(sim, n.ResourceType)));
        }
        return ThievesModel.LootValue(loose, radius);
    }

    // ---- Collect and flee (spec 1779), in the AI tick

    /// <summary>
    /// Spec 1779: one AI step of a thief that collects or flees. Collect: like the scavenger's collect step (spec 1335) with no cooldown: fly
    /// to the nearest loose resource within LootRadius (not tech crates or mission items) and order the crew and funnel to fetch all within
    /// reach. It flees when nothing is left, its bays are full, or CollectSeconds are over. Flee: fly straight away from the nearest player
    /// ship; after FleeSeconds the ship jumps out (the Diplomacy leave list, vanilla FTL effect). In both its guns are off. False: the pirate AI fights.
    /// </summary>
    internal static bool Step(Ship ship)
    {
        if (Run.Current is not { } run || ship.Sim is not { } sim || run.Game.Sim != sim || Of(ship) is not { } th) return false;
        var t = run.T.Thieves;
        double now = run.State.GameSeconds;
        if (th.Phase != ThiefPhase.Fight) GunsOff(ship);
        switch (th.Phase)
        {
            case ThiefPhase.Fight:
                return false;
            case ThiefPhase.Collect:
            {
                var loot = Loot(run, sim, ship, t.LootRadius);
                bool full = AutonomyWorld.CargoFill(ship) is not { } fill || fill >= 0.98f;
                if (loot.Count == 0 || full || now - th.Since >= t.CollectSeconds)
                {
                    th.Phase = ThiefPhase.Flee;
                    th.Since = now;
                    Log.Info($"thieves: {ship.Metadata.PlainShipName} flees with cargo worth {CargoValue(sim, ship)} ({(loot.Count == 0 ? "nothing left" : full ? "bays full" : "time")})");
                    return true;
                }
                float reach = GameApp.Rules.Crew.LoseHomeShipWarningDistance;
                var nearest = loot[0];
                if (AutonomyWorld.EdgeDistance(ship, nearest.DetWorldLocation) > reach * 0.5f) HunterOrders.MoveTo(ship, nearest.DetWorldLocation, reach * 0.25f);
                AutonomyWorld.OrderCollect(ship, loot);
                return true;
            }
            case ThiefPhase.Flee:
            {
                var threat = sim.Ships.HumanPlayerSignificantShips.MinBy(p => p.DetWorldCenter.DistanceSquaredTo(ship.DetWorldCenter));
                if (threat is not null)
                {
                    var away = Vector2.FromAngle(threat.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
                    HunterOrders.MoveTo(ship, ship.DetWorldCenter + away * 3000f, 1000f);
                }
                if (now - th.Since >= t.FleeSeconds && !run.State.Diplomacy.Leaving.Any(l => l.Ship == ship.UniqueID))
                    run.State.Diplomacy.Leaving.Add(new Leaving { Ship = ship.UniqueID, Due = now });
                return true;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(th.Phase), th.Phase, null);
        }
    }

    /// <summary>
    /// Spec 1779: a thief that collects or flees stops shooting: its weapon parts (not the funnel) switch off, as a player switches a part off
    /// (the vanilla on/off toggle). Done each AI tick, so a load keeps it.
    /// </summary>
    static void GunsOff(Ship ship)
    {
        foreach (var weapon in ship.Weapons)
            if (weapon.Part.Rules.TypeCategories.Contains(new("weapon")))
                foreach (var toggle in weapon.Part.Components.OfType<Cosmoteer.Ships.Parts.UI.PartUIToggle>())
                    if (toggle.Rules.ToggleID.ToString() == "on_off" && toggle.Mode != 0) toggle.Mode = 0;
    }

    /// <summary>Loose resources a thief collects: within the radius of its hull, not tech crates or mission items, with room in its bays; nearest first.</summary>
    static List<Nugget> Loot(Run run, SimRoot sim, Ship ship, float radius)
    {
        var items = run.Mode.Missions.SelectMany(MissionCargo.ItemTypes).ToHashSet();
        var found = new List<(Nugget N, float D)>();
        for (int i = 0; i < sim.Nuggets.Count; i++)
        {
            var n = sim.Nuggets[i];
            if (n.ResourceType == Vaults.TechId || items.Contains(n.ResourceType)) continue;
            float d = AutonomyWorld.EdgeDistance(ship, n.DetWorldLocation);
            if (d <= radius && AutonomyWorld.CargoRoom(ship, n.ResourceType) > 0) found.Add((n, d));
        }
        return found.OrderBy(x => x.D).ThenBy(x => x.N.UniqueID).Select(x => x.N).ToList();
    }

    /// <summary>The value of the cargo in a ship's storage bays.</summary>
    internal static long CargoValue(SimRoot sim, Ship ship) =>
        AutonomyWorld.CargoByType(ship).Sum(kv => (long)kv.Value * Funnel.UnitValue(sim, new ID<ResourceRules>(kv.Key)));

    // ---- Ends (spec 1782)

    /// <summary>
    /// Spec 1782: forgets thieves that are gone. A fleeing thief that jumped out or flew out of the live range escaped with its cargo (one in
    /// stasis is removed); one that is a wreck or gone otherwise died, and its storage drops where it broke. Each end goes to telemetry. Other
    /// thieves in stasis stay.
    /// </summary>
    static void Forget(Run run, SimRoot sim)
    {
        var s = S(run);
        foreach (var (id, th) in s.Thieves.OrderBy(kv => kv.Key).ToList())
        {
            var ship = sim.Ships[id];
            var frozen = ship is null ? sim.Stasis.FirstOrDefault(x => x.ReservedObjectID == id) : null;
            // A fleeing thief that flew out of the live range has escaped: it leaves the stasis too, so it never comes back.
            if (frozen is not null && th.Phase == ThiefPhase.Flee) sim.Stasis.RemoveStasisSpawner(frozen);
            else if (ship is { IsJunk: false } || frozen is not null) continue;
            bool escaped = ship is null && th.Phase == ThiefPhase.Flee;
            long stolen = escaped ? s.EscapingCargo.GetValueOrDefault(id) : 0;
            s.Thieves.Remove(id);
            s.EscapingCargo.Remove(id);
            if (escaped) s.Escaped++; else s.Died++;
            Log.Info($"thieves: {id} {(escaped ? $"escaped with cargo worth {stolen}" : "died")} (phase {th.Phase})");
            if (escaped) Ux.UxKit.ChatLine(run.Game, Text("EscapedFmt", stolen.ToString("N0")));
            Tele.Emit(Cat.Combat, "thief.end", (th, escaped, stolen), static (w, x) =>
            {
                w.WriteNumber("group", x.th.Group);
                w.WriteNumber("tier", x.th.Tier);
                w.WriteString("outcome", x.escaped ? "escaped" : "died");
                w.WriteString("phase", x.th.Phase.ToString());
                w.WriteNumber("stolen", x.stolen);
            });
        }
        // The cargo of a fleeing thief, noted every check, so the end event knows what left with it.
        foreach (var (id, th) in s.Thieves)
            if (th.Phase == ThiefPhase.Flee && sim.Ships[id] is { } ship) s.EscapingCargo[id] = CargoValue(sim, ship);
    }
}

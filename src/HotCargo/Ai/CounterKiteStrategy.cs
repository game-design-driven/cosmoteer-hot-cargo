using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Nebulas;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tactics;

namespace HotCargo.Ai;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcCounterKite")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class CounterKiteStrategyRules : AIStrategyModuleRules
{
    [Serialize(Optional = true), PortableHash] public bool ForHunters;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new CounterKiteStrategy(this);
}

/// <summary>
/// Spec 1900-1905. A ship that a player ship outranges and hits for a while stops feeding it: it closes in hard, calls for help,
/// regroups with its allies out of reach, or breaks off and holds just outside the kiter's range. Tell: the ship stops trading
/// shots at range and either rushes the kiter or pulls back out of its reach. No label (spec 368).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class CounterKiteStrategy(CounterKiteStrategyRules rules) : AIStrategyModule
{
    /// <summary>The read window: its start and the health at the start. A negative health: no window yet.</summary>
    [Serialize(Optional = true)] Time _windowStart;
    [Serialize(Optional = true)] float _windowHealth = -1f;
    [Serialize(Optional = true)] int _answer;
    [Serialize(Optional = true)] ObjectID _kiter;
    /// <summary>Start of the answer (or of the break off that followed a failed close in), and the gap at that start.</summary>
    [Serialize(Optional = true)] Time _since;
    [Serialize(Optional = true)] float _startGap;
    /// <summary>The last read, kept after the answer ends: the anvil of the ship's allies uses it (spec 1906).</summary>
    [Serialize(Optional = true)] Time _readAt;
    [Serialize(Optional = true)] ObjectID _readKiter;
    /// <summary>Spec 1920: what the ship saw of the players: a kite read, a player ship that fights from a nebula, and its part
    /// health lost by kind (FocusKind order) since the first sample, with the last sums. A negative last sum: no sample yet.</summary>
    [Serialize(Optional = true)] bool _sawKite, _sawCloud;
    [Serialize(Optional = true)] float _lossWeapons, _lossThrusters, _lossCore, _fullHealth;
    [Serialize(Optional = true)] float _lastWeapons = -1f, _lastThrusters, _lastCore;
    [Serialize(Optional = true)] Time _lastSample;
    // Log only, not sim state.
    bool _holdLogged;

    public KiteAnswer Answer => (KiteAnswer)_answer;
    public ObjectID Kiter => _kiter;

    /// <summary>Spec 1902: true while the ship answers a kite with a call (CallStrategy starts the call).</summary>
    public static bool WantsCall(Ship ship) => ship.AI?.GetStrategyModule<CounterKiteStrategy>() is { Answer: KiteAnswer.Call };

    /// <summary>Spec 1906: the ship read this target as a kiter within the last memory seconds.</summary>
    public bool ReadRecently(ObjectID target, Time now, float memorySeconds) =>
        _readKiter != default && _readKiter == target && (now - _readAt).Seconds <= memorySeconds;

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _windowStart.GetIntegrityHash(iHash);
        _windowHealth.GetIntegrityHash(iHash);
        _answer.GetIntegrityHash(iHash);
        _kiter.GetIntegrityHash(iHash);
        _since.GetIntegrityHash(iHash);
        _startGap.GetIntegrityHash(iHash);
        _readAt.GetIntegrityHash(iHash);
        _readKiter.GetIntegrityHash(iHash);
        _sawKite.GetIntegrityHash(iHash);
        _sawCloud.GetIntegrityHash(iHash);
        _lossWeapons.GetIntegrityHash(iHash);
        _lossThrusters.GetIntegrityHash(iHash);
        _lossCore.GetIntegrityHash(iHash);
        _fullHealth.GetIntegrityHash(iHash);
        _lastWeapons.GetIntegrityHash(iHash);
        _lastThrusters.GetIntegrityHash(iHash);
        _lastCore.GetIntegrityHash(iHash);
        _lastSample.GetIntegrityHash(iHash);
    }

    /// <summary>Spec 1920: the habits that the ship saw so far.</summary>
    public Habits Observed(TacticsTunables t) =>
        new(_sawKite, _sawCloud, LearnModel.Focus([_lossWeapons, _lossThrusters, _lossCore], _fullHealth, t.FocusShare, t.FocusMinLoss));

    public override void OnEnteringStasis(Ship ship)
    {
        _answer = (int)KiteAnswer.None;
        _windowHealth = -1f;
    }

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (Run.Current is not { } run || AiGate.Surrendered(ship) || !AiGate.HostileToPlayers(ship))
        {
            _windowHealth = -1f;
            return End(ship, null);
        }
        Time now = HunterOrders.Sim(ship).LogicalTime;
        // Spec 1920, 1925: watching the players does not need the counter kiting switch.
        if (run.T.Tactics.LearnEnabled) Observe(ship, now, run.T.Tactics);
        if (AiGate.Active(Behavior.CounterKite, rules.ForHunters) is not { } t)
        {
            _windowHealth = -1f;
            return End(ship, null);
        }
        float own = HunterOrders.MaxWeaponRange(ship, onlyActive: true);
        if (Answer == KiteAnswer.None && AiLearn.Prepared(ship).Cloud && CloudHold(ship, t)) return true;
        _holdLogged = false;
        if (Answer == KiteAnswer.None)
        {
            Read(ship, now, own, t);
            if (Answer == KiteAnswer.None) return false;
        }
        return Act(ship, now, own, t);
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new CounterKiteStrategy(rules)
    {
        _windowStart = _windowStart, _windowHealth = _windowHealth, _answer = _answer, _kiter = _kiter, _since = _since, _startGap = _startGap,
        _readAt = _readAt, _readKiter = _readKiter, _sawKite = _sawKite, _sawCloud = _sawCloud, _lossWeapons = _lossWeapons,
        _lossThrusters = _lossThrusters, _lossCore = _lossCore, _fullHealth = _fullHealth, _lastWeapons = _lastWeapons,
        _lastThrusters = _lastThrusters, _lastCore = _lastCore, _lastSample = _lastSample,
    };

    /// <summary>
    /// Spec 1920, 1921, 1925: every HabitSampleSeconds while a player ship is on the team radar, sums the part health by kind and adds
    /// what was lost since the last sample, and notes a player ship that fights from a nebula (inside one at AnvilNebulaStrength or
    /// more, with this ship in its reach). Repairs do not take a loss back.
    /// </summary>
    void Observe(Ship ship, Time now, TacticsTunables t)
    {
        if (AiGate.NearestHumanEnemy(ship) is null) return;
        AiLearn.Notice(ship);
        if (_lastWeapons >= 0f && (now - _lastSample).Seconds < t.HabitSampleSeconds) return;
        var categories = t.VulnerableParts.Select(c => new ID<Cosmoteer.Ships.Parts.PartCategory>(c)).ToList();
        float weapons = 0f, thrusters = 0f, core = 0f, full = 0f;
        foreach (var part in ship.Parts)
        {
            full += part.Rules.MaxHealth;
            var kinds = ShipAi.ShipAiController.KindsOf(part.Rules);
            if (kinds.Contains(ShipAi.PartKind.Weapon)) weapons += part.Health;
            if (kinds.Contains(ShipAi.PartKind.Thruster)) thrusters += part.Health;
            if (categories.Any(part.Rules.TypeCategories.Contains)) core += part.Health;
        }
        if (_lastWeapons >= 0f)
        {
            _lossWeapons += Math.Max(0f, _lastWeapons - weapons);
            _lossThrusters += Math.Max(0f, _lastThrusters - thrusters);
            _lossCore += Math.Max(0f, _lastCore - core);
        }
        (_lastWeapons, _lastThrusters, _lastCore, _fullHealth, _lastSample) = (weapons, thrusters, core, Math.Max(_fullHealth, full), now);
        if (_sawCloud) return;
        foreach (var human in HunterOrders.Sim(ship).Ships.HumanPlayerSignificantShips)
            if (ship.IsEnemiesWith(human) && InNebula(human, t) && HunterOrders.EdgeDistance(ship, human) <= Reach(human))
                _sawCloud = true;
    }

    /// <summary>Spec 1921: the ship is inside a nebula of any type at AnvilNebulaStrength or more.</summary>
    static bool InNebula(Ship ship, TacticsTunables t)
    {
        for (int i = 0; i < ship.Nebulas.Count; i++)
            if (ship.Nebulas[i].Strength >= t.AnvilNebulaStrength) return true;
        return false;
    }

    /// <summary>
    /// Spec 1926: a ship of a group with a cloud report does not follow a player ship into a nebula. While its target is inside one
    /// and the ship is not, it holds CloudEdgeMargin outside the nebula edge, on the line from the target. True: this module drives the ship.
    /// </summary>
    bool CloudHold(Ship ship, TacticsTunables t)
    {
        var target = (ship.Commands.CurConfirmedCommand as AttackCommand)?.Target ?? AiGate.NearestHumanEnemy(ship);
        if (target is null || !target.IsHumanPlayerShip || !InNebula(target, t) || InNebula(ship, t)) return false;
        var sim = HunterOrders.Sim(ship);
        var types = new List<ID<NebulaTypeRules>>();
        for (int i = 0; i < target.Nebulas.Count; i++)
            if (target.Nebulas[i].Strength >= t.AnvilNebulaStrength) types.Add(target.Nebulas[i].Type.ID);
        Vector2 from = target.DetWorldCenter;
        float distance = from.DistanceTo(ship.DetWorldCenter);
        var dir = Vector2.FromAngle(from.DirectionTo(ship.DetWorldCenter));
        if (LearnModel.EdgeDistance(d => types.Any(type => sim.Nebulas.GetInsideStrength(from + dir * d, type) >= t.AnvilNebulaStrength), distance, t.CloudEdgeStep)
            is not { } edge) return false;
        var spot = from + dir * (edge + t.CloudEdgeMargin + ship.WorldBoundingRadius);
        PersonalityStrategy.MoveGuarded(ship, spot, Math.Max(t.CloudEdgeMargin * 0.5f, 50f), target, t);
        if (!_holdLogged) TacticsLog.Event(ship, AiArea.CloudHold, $"holds at the nebula edge {edge:0} from {target.Metadata.PlainShipName}");
        _holdLogged = true;
        return true;
    }

    /// <summary>Spec 1900, 1901: keeps the read window while a player ship outranges the ship, and starts an answer when it is kited.</summary>
    void Read(Ship ship, Time now, float own, TacticsTunables t)
    {
        float health = HunterOrders.HealthFraction(ship);
        var kiter = Outranging(ship, own);
        if (kiter is null || _windowHealth < 0f)
        {
            (_windowStart, _windowHealth) = (now, health);
            return;
        }
        double seconds = (now - _windowStart).Seconds;
        // Spec 1924: a group with a kite report reads a kite sooner.
        float readSeconds = AiLearn.Prepared(ship).Kite ? t.PreparedKiteReadSeconds : t.KiteReadSeconds;
        if (seconds < readSeconds) return;
        if (CounterKiteModel.Kited(seconds, _windowHealth - health, readSeconds, t.KiteReadLoss)) Start(ship, kiter, now, t);
        (_windowStart, _windowHealth) = (now, health);
    }

    void Start(Ship ship, Ship kiter, Time now, TacticsTunables t)
    {
        var answer = CounterKiteModel.Pick(PersonalityStrategy.Traits(ship, t), t.PersonalitiesEnabled, PersonalityStrategy.Scores(ship, t).Agility, t.CloseInAgility);
        // A call needs the call behavior; without it the ship breaks off.
        if (answer == KiteAnswer.Call && AiGate.Active(Behavior.Call, AiGate.IsHunterAi(ship)) is null) answer = KiteAnswer.BreakOff;
        (_answer, _kiter, _since, _startGap, _readAt, _readKiter) = ((int)answer, kiter.UniqueID, now, HunterOrders.EdgeDistance(ship, kiter), now, kiter.UniqueID);
        _sawKite = true;
        TacticsLog.Event(ship, AiArea.KiteRead, $"kited by {kiter.Metadata.PlainShipName}: {answer} (gap {_startGap:0}, range {HunterOrders.MaxWeaponRange(ship, true):0} vs reach {Reach(kiter):0})");
    }

    /// <summary>Spec 1902-1905. True: this module drives the ship.</summary>
    bool Act(Ship ship, Time now, float own, TacticsTunables t)
    {
        if (HunterOrders.Sim(ship).Ships[_kiter] is not { } kiter || kiter.IsJunk || !ship.IsEnemiesWith(kiter) || !kiter.IsWithinRadarOfTeam(ship.Metadata.Team))
            return End(ship, "kiter gone");
        float gap = HunterOrders.EdgeDistance(ship, kiter);
        if (gap <= own) return End(ship, "kiter in reach");
        if (Answer == KiteAnswer.CloseIn)
        {
            if (!CounterKiteModel.CloseInFailed((now - _since).Seconds, _startGap, gap, t.CloseInSeconds, t.CloseInGain))
            {
                // CounterKiteTactic closes the attack radius to BrawlRangeFraction of the ship's range.
                HunterOrders.Attack(ship, kiter);
                return true;
            }
            (_answer, _since) = ((int)KiteAnswer.BreakOff, now);
            TacticsLog.Event(ship, AiArea.KiteRead, $"close in on {kiter.Metadata.PlainShipName} failed (gap {_startGap:0} -> {gap:0}): BreakOff");
        }
        if (AllyEngages(ship, kiter)) return End(ship, "an ally engages");
        var allies = AiGate.Allies(ship).Where(a => a.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= t.RegroupSearchRange).ToList();
        if (Answer == KiteAnswer.Team && allies.Count(a => a.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= t.RegroupRadius) + 1 >= Math.Min(t.RegroupMinGroup, allies.Count + 1)
            && allies.Count > 0)
            return End(ship, "team gathered");
        Hold(ship, kiter, Answer == KiteAnswer.Team ? allies : [], t);
        return true;
    }

    /// <summary>
    /// Spec 1904: holds BreakOffMargin outside the kiter's longest weapon range, edge to edge, with the guarded move (spec 1033). A team
    /// ship holds on the side of its allies' center, so the team gathers out of reach.
    /// </summary>
    static void Hold(Ship ship, Ship kiter, List<Ship> allies, TacticsTunables t)
    {
        Vector2 from = kiter.DetWorldCenter;
        Vector2 toward = ship.DetWorldCenter;
        if (allies.Count > 0)
        {
            var center = new Vector2(allies.Average(a => a.DetWorldCenter.X), allies.Average(a => a.DetWorldCenter.Y));
            if (center.DistanceTo(from) > 1f) toward = center;
        }
        float hold = kiter.WorldBoundingRadius + ship.WorldBoundingRadius + Reach(kiter) + t.BreakOffMargin;
        var spot = from + Vector2.FromAngle(from.DirectionTo(toward)) * hold;
        // A NaN in a move command crashes the vanilla thruster code far from here, so a bad spot fails with its inputs.
        if (!float.IsFinite(spot.X) || !float.IsFinite(spot.Y))
            throw new InvalidOperationException($"counter kite hold: no spot for {ship.Metadata.PlainShipName} from {from} toward {toward}, hold {hold}");
        PersonalityStrategy.MoveGuarded(ship, spot, Math.Max(t.BreakOffMargin * 0.5f, 50f), kiter, t);
    }

    /// <summary>Spec 1900: the kiter's reach. Weapons that are off count: their player can turn them on at any time.</summary>
    internal static float Reach(Ship kiter) => CounterKiteModel.Reach(HunterOrders.MaxWeaponRange(kiter, onlyActive: false), kiter.Sensors.RadarRadius);

    /// <summary>Spec 1905: an ally of the ship has the kiter within its own weapon range.</summary>
    static bool AllyEngages(Ship ship, Ship kiter)
    {
        foreach (var ally in AiGate.Allies(ship))
            if (HunterOrders.EdgeDistance(ally, kiter) <= HunterOrders.MaxWeaponRange(ally, onlyActive: true)) return true;
        return false;
    }

    /// <summary>Spec 1900: the nearest human enemy on the team radar that reaches the ship while the ship does not reach it, or null.</summary>
    static Ship? Outranging(Ship ship, float own)
    {
        Ship? best = null;
        float bestGap = float.MaxValue;
        foreach (var human in HunterOrders.Sim(ship).Ships.HumanPlayerSignificantShips)
        {
            if (!ship.IsEnemiesWith(human) || !human.IsWithinRadarOfTeam(ship.Metadata.Team)) continue;
            float gap = HunterOrders.EdgeDistance(ship, human);
            if (gap < bestGap && CounterKiteModel.Outranged(gap, own, Reach(human))) (best, bestGap) = (human, gap);
        }
        return best;
    }

    bool End(Ship ship, string? why)
    {
        if (Answer != KiteAnswer.None && why is not null) TacticsLog.Event(ship, AiArea.KiteEnd, $"{Answer} ends: {why}");
        _answer = (int)KiteAnswer.None;
        _windowHealth = -1f;
        return false;
    }
}

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcAnvil")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class CounterKiteTacticRules : AITacticsModuleRules
{
    [Serialize(Optional = true), PortableHash] public int AttackRotationsToCheck = 8;
    [Serialize(Optional = true), PortableHash] public float AttackRotationSwitchThresholdFactor = 1.25f;

    public override AITacticsModule CreateModule(AIParameters? parameters) => new CounterKiteTactic(this);
}

/// <summary>
/// Spec 1903, 1906. The attack radius of a ship that closes in on its kiter, and the anvil: attackers of a kiter come from the far
/// side of a nearby electron or cloudy nebula, or from the far side of the other attackers, so a kiter that backs off backs into the
/// nebula or into the other enemies. Tell: the enemies swing around before they come in.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class CounterKiteTactic(CounterKiteTacticRules rules) : AITacticsModule
{
    // Log only, not sim state.
    int _lastSlot = -1;

    public override bool UpdateAttackAngle(AttackCommand ac, ShipAI ai)
    {
        if (Gate(ac.Ship) is not { } t) return false;
        var sim = HunterOrders.Sim(ac.Ship);
        var group = sim.Ships.GetShipsForPlayer(ac.Ship.Metadata.PlayerIndex)
            .Where(s => s.Commands.CurConfirmedCommand is AttackCommand other && other.Target == ac.Target && other.IsValid())
            .ToList();
        group.Sort((a, b) => a.UniqueID.CompareTo(b.UniqueID));
        int slot = group.IndexOf(ac.Ship);
        // Spec 1924: a group with a kite report takes the anvil on any target that outranges it, from first contact.
        bool prepared = AiLearn.Prepared(ac.Ship).Kite && CounterKiteStrategy.Reach(ac.Target) > HunterOrders.MaxWeaponRange(ac.Ship, onlyActive: true);
        if (group.Count < 2 || slot < 0
            || !prepared && !group.Any(s => s.AI?.GetStrategyModule<CounterKiteStrategy>()?.ReadRecently(ac.Target.UniqueID, sim.LogicalTime, t.AnvilMemorySeconds) == true))
        {
            _lastSlot = -1;
            return false;
        }
        Vector2 kiter = ac.Target.DetWorldCenter;
        var others = group.Where(s => s != ac.Ship).ToList();
        var center = new Vector2(others.Average(s => s.DetWorldCenter.X), others.Average(s => s.DetWorldCenter.Y));
        var nebula = AnvilNebula(sim, ac.Target, t);
        float? angle = CounterKiteModel.AnvilAngle((kiter.X, kiter.Y), nebula is { } n ? (n.X, n.Y) : null, (center.X, center.Y), slot, group.Count, t.AnvilSpreadDegrees);
        if (angle is not { } a) return false;
        ac.UseWorldRelativeFollowAngle = true;
        ac.FollowAngle = new Direction(a);
        if (slot != _lastSlot)
            TacticsLog.Event(ac.Ship, AiArea.Anvil, $"slot {slot} of {group.Count} on {ac.Target.Metadata.PlainShipName}, {(nebula is null ? "far side of the others" : $"far side of a nebula at {nebula}")}");
        _lastSlot = slot;
        return true;
    }

    public override bool UpdateAttackRadiusAndRotation(AttackCommand ac, ShipAI ai)
    {
        if (Gate(ac.Ship) is not { } t || ac.Ship.AI?.GetStrategyModule<CounterKiteStrategy>() is not { Answer: KiteAnswer.CloseIn } m || m.Kiter != ac.Target.UniqueID)
            return false;
        ac.EdgeToEdgeFollowRadius = HunterOrders.MaxWeaponRange(ac.Ship, onlyActive: true) * t.BrawlRangeFraction;
        ac.SetBestAttackRotation(useIdealRange: false, onlyWithResources: true, rules.AttackRotationsToCheck, rules.AttackRotationSwitchThresholdFactor);
        return true;
    }

    public override AITacticsModule Clone(AIParameters? parameters) => new CounterKiteTactic(rules);

    static TacticsTunables? Gate(Ship ship) =>
        AiGate.Active(Behavior.CounterKite, AiGate.IsHunterAi(ship)) is { } t && !AiGate.Surrendered(ship) && AiGate.HostileToPlayers(ship) ? t : null;

    /// <summary>Rings of sample points around the kiter: this many rings out to AnvilNebulaRange, this many points on each.</summary>
    const int Rings = 3, PointsPerRing = 12;

    static string[]? _typesSource;
    static List<ID<NebulaTypeRules>> _types = [];
    // Per sim tick cache of the nebula point by kiter, not sim state: every attacker of one kiter asks in the same tick.
    static readonly Dictionary<ObjectID, (SimRoot Sim, int Tick, Vector2? Point)> Cache = [];

    /// <summary>
    /// Spec 1906: the nearest sample point around the kiter inside an AnvilNebulas nebula at AnvilNebulaStrength or more, nearest ring
    /// first, then by angle from +X. Null when none. An unknown nebula type throws, so a tunables typo shows.
    /// Limit: the samples can miss a nebula smaller than the gap between points (about 2,000 on the outer ring). Revisit if attackers
    /// often ignore a nearby nebula.
    /// </summary>
    static Vector2? AnvilNebula(SimRoot sim, Ship kiter, TacticsTunables t)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(kiter.UniqueID, out var hit) && hit.Sim == sim && hit.Tick == sim.Tick) return hit.Point;
            if (!ReferenceEquals(_typesSource, t.AnvilNebulas))
            {
                _types = t.AnvilNebulas.Select(name => GameApp.Rules.TryGetNebulaTypeRules(new ID<NebulaTypeRules>(name), out _)
                    ? new ID<NebulaTypeRules>(name)
                    : throw new InvalidOperationException($"Tactics.AnvilNebulas: unknown nebula type '{name}'")).ToList();
                _typesSource = t.AnvilNebulas;
            }
            Vector2? found = null;
            for (int ring = 1; ring <= Rings && found is null; ring++)
                for (int i = 0; i < PointsPerRing && found is null; i++)
                {
                    var point = kiter.DetWorldCenter + Vector2.FromAngle(i * MathF.Tau / PointsPerRing) * (t.AnvilNebulaRange * ring / Rings);
                    if (_types.Any(type => sim.Nebulas.GetInsideStrength(point, type) >= t.AnvilNebulaStrength)) found = point;
                }
            Cache[kiter.UniqueID] = (sim, sim.Tick, found);
            return found;
        }
    }
}

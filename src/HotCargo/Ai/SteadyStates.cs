using System.Globalization;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.StrategyModules;
using Halfling.Geometry;
using Halfling.Timing;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;

namespace HotCargo.Ai;

/// <summary>
/// Spec 1531. The vanilla flee module of trade ships starts and stops at the radar radius, so an enemy at the radar edge made the
/// ship flee, lose the contact, turn back and flee again. When vanilla stops a flee, this keeps it while an enemy is within the
/// stop radius × TraderFleeStopFactor: the ship holds its last flee move. The flee flag and FTL time are the module's saved state.
/// </summary>
[HarmonyPatch(typeof(AIFleeFromEnemiesModule), nameof(AIFleeFromEnemiesModule.OnAITick))]
static class TraderFleeHoldPatch
{
    static void Prefix(AIFleeFromEnemiesModule __instance, out (bool Fleeing, Time? FtlAt) __state) =>
        __state = (__instance._isFleeing, __instance._ftlAt);

    // First: the telemetry postfix on every OnAITick must see the final result.
    [HarmonyPriority(Priority.First)]
    static void Postfix(AIFleeFromEnemiesModule __instance, Ship ship, (bool Fleeing, Time? FtlAt) __state, ref bool __result)
    {
        if (__result || !__state.Fleeing || Run.Current is not { } run) return;
        try
        {
            // Vanilla stop radius: FleeStopRange, capped at the radar radius (the trade ship rules leave FleeStopRange unset).
            float radius = Math.Min(__instance.Rules.FleeStopRange, ship.Sensors.RadarRadius) * run.T.Tactics.TraderFleeStopFactor;
            if (!AnyEnemyWithin(ship, radius)) return;
            (__instance._isFleeing, __instance._ftlAt, __result) = (true, __state.FtlAt, true);
        }
        catch (Exception ex) { Errors.Report("trader flee hold", ex.Message, ex); }
    }

    /// <summary>The same enemy test as the vanilla module, in a larger circle.</summary>
    internal static bool AnyEnemyWithin(Ship ship, float radius)
    {
        var found = new List<Ship>();
        HunterOrders.Sim(ship).Ships.DetFindIntersectingWith(new Circle(ship.DetWorldCenter, radius), found, approximate: false, false);
        return found.Any(s => s != ship && s.IsSignificant && s.IsEnemiesWith(ship));
    }

    static ObjectID? _trader;

    /// <summary>Autotest commands and fields of spec 1531. Single player only: they move ships from a GUI frame.</summary>
    internal static void RegisterAutotest()
    {
        // ai_trader_want: picks the trade ship nearest to the player and makes its faction want the player, so it flees from them.
        Autotest.Commands["ai_trader_want"] = _ =>
        {
            var me = Player();
            var trader = me.Sim!.Ships.Where(s => s.AI?.GetStrategyModule<AIFleeFromEnemiesModule>() is not null)
                .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(me.DetWorldCenter)).ThenBy(s => s.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no trade ship in this system");
            _trader = trader.UniqueID;
            Autotest.RequireRun().Mode.SimMode.Wanted.WarTriggeredByPlayer(trader);
            Autotest.Check(me.IsEnemiesWith(trader), $"{trader.Metadata.PlainShipName} is an enemy of the player");
        };
        // ai_trader_place <factor>: lines up the player's ships on one ray from the trade ship. The hull of the nearest one is the
        // trade ship's radar radius × factor from the trade ship; each next one sits 50 behind the one before.
        // ai_trader_place <factor>: lines up the player's ships on one ray from the trade ship. The hull of the nearest one is the
        // trade ship's radar radius × factor from the trade ship; each next one sits 50 behind the one before.
        Autotest.Commands["zz_trader_enemies"] = _ =>
        {
            var t = Trader();
            foreach (var e in t.Sim!.Ships.Where(x => x != t && x.IsSignificant && x.IsEnemiesWith(t)).OrderBy(x => x.DetWorldCenter.DistanceTo(t.DetWorldCenter)).Take(5))
                Log.Info($"ZZ enemy {e.Metadata.PlainShipName} human={e.IsHumanPlayerShip} d={e.DetWorldCenter.DistanceTo(t.DetWorldCenter):0} r={e.WorldBoundingRadius:0}");
            var m = t.AI!.GetStrategyModule<AIFleeFromEnemiesModule>()!;
            Log.Info($"ZZ trader cmd={t.Commands.CurConfirmedCommand?.GetType().Name} fleeing={m._isFleeing} radar={t.Sensors.RadarRadius:0} ai={t.AI.Rules.ID}");
        };
        Autotest.Commands["ai_trader_place"] = arg =>
        {
            var trader = Trader();
            var sim = trader.Sim!;
            var ships = sim.Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).ToList();
            if (ships.Count == 0) throw new InvalidOperationException("the player has no ship");
            var dir = Vector2.FromAngle(trader.DetWorldCenter.DirectionTo(ships[0].DetWorldCenter));
            float edge = trader.Sensors.RadarRadius * float.Parse(arg.Trim(), CultureInfo.InvariantCulture);
            foreach (var ship in ships)
            {
                Autotest.Teleport(ship, trader.DetWorldCenter + dir * (edge + ship.WorldBoundingRadius));
                edge += 2 * ship.WorldBoundingRadius + 50f;
            }
            Log.Info($"AUTOTEST ai_trader_place: {ships.Count} player ships, nearest hull at {trader.Sensors.RadarRadius * float.Parse(arg.Trim(), CultureInfo.InvariantCulture):0} from {trader.Metadata.PlainShipName} (radar {trader.Sensors.RadarRadius:0})");
        };
        // 1 while the picked trade ship flees, else 0. A trade ship without a trade route (a test spawn) leaves the system as soon
        // as it stops fleeing, so a ship that left counts as 0.
        // ai_trader_clear: every enemy of the picked trade ship that is not a human ship, live or in stasis, leaves the system, so only
        // the player decides whether it flees. A pirate of the test world near the trade ship held the flee (CI full suites, #184).
        Autotest.Commands["ai_trader_clear"] = _ =>
        {
            var trader = Trader();
            var sim = trader.Sim!;
            var live = sim.Ships.Where(s => s != trader && !s.IsHumanPlayerShip && s.IsEnemiesWith(trader)).OrderBy(s => s.UniqueID).ToList();
            foreach (var s in live) sim.Ships.Remove(s, dispose: true, handleLostShip: false);
            var stasis = sim.Stasis.OfType<Cosmoteer.Simulation.Stasis.StasisShipSpawner>()
                .Where(s => sim.Mode.AreEnemies(s.ObjectAllegiance, trader.Metadata.PlayerIndex) && !Autotest.RequireRun().Game.HumanPlayersIndexes.Contains(s.ObjectAllegiance))
                .OrderBy(s => s.ReservedObjectID).ToList();
            foreach (var s in stasis) sim.Stasis.RemoveStasisSpawner(s);
            Log.Info($"AUTOTEST ai_trader_clear: {live.Count} live and {stasis.Count} stasis enemies of {trader.Metadata.PlainShipName} left the system");
        };
        Autotest.Fields["ai_trader_fleeing"] = () => _trader is { } id && Autotest.RequireRun().Game.Sim.Ships[id] is null && !InStasis(id) ? 0
            : Trader().AI!.GetStrategyModule<AIFleeFromEnemiesModule>()!._isFleeing ? 1 : 0;
    }

    static Ship Trader()
    {
        var sim = Autotest.RequireRun().Game.Sim;
        if (_trader is not { } id) throw new InvalidOperationException("no trade ship picked (run ai_trader_want first)");
        return sim.Ships[id] ?? throw new InvalidOperationException(InStasis(id) ? "the trade ship went into stasis" : "the trade ship left the system");
    }

    static bool InStasis(ObjectID id) =>
        Autotest.RequireRun().Game.Sim.Stasis.Any(s => s is Cosmoteer.Simulation.Stasis.StasisShipSpawner sp && sp.ReservedObjectID == id);

    static Ship Player() => Autotest.RequireRun().Game.Sim.Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).FirstOrDefault()
        ?? throw new InvalidOperationException("the player has no ship");
}

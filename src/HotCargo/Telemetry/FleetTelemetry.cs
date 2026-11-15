using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tech;
using static HotCargo.Telemetry.TelemetryModel;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 1580 to 1582, 1586: ship sides and wreck lineage, captures, ships that leave the fleet, the ships of a jump, tech unlock money.
/// Read-only patches: prefixes return nothing, postfixes change no result.
/// </summary>
static class FleetTelemetry
{
    static readonly Lock Gate = new();
    static readonly ShipLineage<ObjectID, ShipInfo> Lineage = new();
    /// <summary>The last state of each ship that became a wreck this session, so a capture of a wreck names its old side.</summary>
    static readonly Dictionary<ObjectID, ShipInfo> Wrecked = [];
    /// <summary>The fleet at the last second of the previous session in this process, for ships that a co-op resync drops.</summary>
    static (string Run, ObjectID Node, Dictionary<ObjectID, (string Name, int Player)> Ships)? _lastFleet;

    internal static void ResetSession()
    {
        lock (Gate) { Lineage.Clear(); Wrecked.Clear(); }
    }

    static bool IsHuman(Session s, int player) => s.Game.HumanPlayersIndexes.Contains(player);

    /// <summary>
    /// Spec 1580: the one side of a ship, for every event. A piece of a broken ship keeps the side of the ship it came from
    /// (pieces lose the hunter tag). playerIndex is the allegiance to judge, for example the one before the ship became a wreck.
    /// </summary>
    internal static string SideOf(Session s, Ship ship, int playerIndex)
    {
        if (IsHuman(s, playerIndex)) return "player";
        if (playerIndex == JunkPlayer) return "derelict";
        ShipInfo? root;
        lock (Gate) root = Lineage.InfoOf(ship.UniqueID);
        if (root is { Side: not "player" }) return root.Side;
        if (ship.HasTag(HunterSpawner.HunterTag)) return "hunter";
        return TelemetryPatches.IsHostile(s, playerIndex) ? "hostile" : "neutral";
    }

    internal static ShipInfo Info(Session s, Ship ship, int playerIndex) => new(
        ship.UniqueID.ToString(), ship.Metadata.PlainShipName, playerIndex, SideOf(s, ship, playerIndex),
        FactionRules.GetFaction(playerIndex)?.ID.ToString(), ship.HasTag(HunterSpawner.HunterTag), ship.BaseBlueprintCost,
        ship.Tags is { } tags ? [.. tags.Select(t => t.ToString())] : []);

    /// <summary>Spec 1580: a ship became a wreck. Returns the info of the destroyed ship once its last live piece is a wreck, else null.</summary>
    internal static ShipInfo? Junked(Session s, Ship ship, int oldAllegiance)
    {
        var own = Info(s, ship, oldAllegiance);
        lock (Gate)
        {
            var (ends, root) = Lineage.Junked(ship.UniqueID);
            Wrecked[ship.UniqueID] = root ?? own;
            return ends ? root ?? own : null;
        }
    }

    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.OnShipSplit))]
    static class SplitPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Ship oldShip, IReadOnlyList<Ship> newShips)
        {
            if (oldShip.IsJunk || Session.Current is not { } s || oldShip.Sim != s.Game.Sim || !Tele.On(Cat.Combat)) return;
            long t0 = Stats.Enter();
            try
            {
                var info = Info(s, oldShip, oldShip.Metadata.PlayerIndex);
                lock (Gate) Lineage.Split(oldShip.UniqueID, newShips.Select(n => n.UniqueID), () => info);
            }
            catch (Exception ex) { Errors.Report("telemetry split", ex.Message, ex); }
            finally { Stats.Exit(t0); }
        }
    }

    /// <summary>Spec 1581: a ship that becomes a player ship (a claimed wreck, a ship that joins) or stops being one without a wreck.</summary>
    [HarmonyPatch(typeof(ShipMetadata), nameof(ShipMetadata.PlayerIndex), MethodType.Setter)]
    static class AllegiancePatch
    {
        static void Prefix(ShipMetadata __instance, out int __state) => __state = __instance.PlayerIndex;

        static void Postfix(ShipMetadata __instance, int __state)
        {
            int now = __instance.PlayerIndex;
            if (now == __state || Session.Current is not { } s || !Tele.On(Cat.Combat) || __instance.Ship is not { } ship || ship.Sim != s.Game.Sim) return;
            bool was = IsHuman(s, __state), isNow = IsHuman(s, now);
            if (was == isNow || now == JunkPlayer) return;
            long t0 = Stats.Enter();
            try
            {
                string caller = TelemetryPatches.Caller();
                if (isNow)
                {
                    ShipInfo old;
                    lock (Gate) old = __state == JunkPlayer && Wrecked.TryGetValue(ship.UniqueID, out var w) ? w : Info(s, ship, __state);
                    Tele.Emit(Cat.Combat, "combat.captured", w =>
                    {
                        WriteCapture(w, old, now, CaptureHow(__state, caller), ship.BaseBlueprintCost, ship.Ftl.HasFtlDrives, Vaults.ShipTech(ship));
                        w.WriteString("caller", caller);
                    });
                }
                else
                    Tele.Emit(Cat.Combat, "combat.fleet_left", w =>
                    {
                        WriteLeft(w, ship, __state, "control_lost");
                        w.WriteNumber("to_player", now);
                        w.WriteString("caller", caller);
                    });
            }
            catch (Exception ex) { Errors.Report("telemetry allegiance", ex.Message, ex); }
            finally { Stats.Exit(t0); }
        }
    }

    static void WriteLeft(System.Text.Json.Utf8JsonWriter w, Ship ship, int player, string reason)
    {
        w.WriteString("ship", ship.UniqueID.ToString());
        w.WriteString("name", ship.Metadata.PlainShipName);
        w.WriteNumber("player", player);
        w.WriteString("reason", reason);
        w.WriteNumber("value", ship.BaseBlueprintCost);
        w.WriteNumber("tech", Vaults.ShipTech(ship));
    }

    // ---- Jumps

    /// <summary>Spec 1582: one ship of a jump. Cost is the vanilla fuel cost of the jump path, null without a destination.</summary>
    internal static JumpShip JumpShipOf(Ship ship, IList<Cosmoteer.GalaxyMap.MapNode>? path) => new(
        ship.UniqueID.ToString(), ship.Metadata.PlainShipName, ship.Metadata.PlayerIndex, ship.Ftl.HasFtlDrives, ship.Ftl.GetTotalFuel(),
        ship.Ftl.Sum(d => d.MaxFuel), path is { Count: >= 2 } ? ship.Ftl.GetTotalFtlMapJumpFuelCost(path) : null, Vaults.ShipTech(ship), ship.Parts.Count);

    /// <summary>The vanilla jump path from the current system to the confirmed destination (GameRoot.DoHumanPlayersFtlJump), or null.</summary>
    internal static List<Cosmoteer.GalaxyMap.MapNode>? JumpPath(GameRoot game)
    {
        if (game.DestinationMapNode.Confirmed is not { } dest || game.Map?.Nodes is not { } nodes) return null;
        var path = new List<Cosmoteer.GalaxyMap.MapNode>();
        return float.IsPositiveInfinity(nodes.FindPathTo(dest, path, requireJumpable: true)) ? null : path;
    }

    /// <summary>Spec 1582: the ships that stay in the old system at a jump leave the fleet.</summary>
    internal static void EmitLeftBehind(IEnumerable<Ship> ships)
    {
        foreach (var ship in ships)
            Tele.Emit(Cat.Combat, "combat.fleet_left", ship, static (w, sh) => WriteLeft(w, sh, sh.Metadata.PlayerIndex, "left_behind"));
    }

    // ---- Co-op resync

    /// <summary>Keeps the fleet of the current system, read every second, so the next session in this process can compare.</summary>
    internal static void RememberFleet(Session s, SimRoot sim)
    {
        var ships = sim.Ships.HumanPlayerSignificantShips.ToDictionary(x => x.UniqueID, x => (x.Metadata.PlainShipName, x.Metadata.PlayerIndex));
        _lastFleet = (s.RunId, sim.NodeId(), ships);
    }

    /// <summary>
    /// Spec 1581: after a co-op resync or a join, the ships of the previous session in this process that are gone from the same system.
    /// Limit: it compares only ships of the system that both sessions show. Revisit if a resync also changes the system.
    /// </summary>
    internal static void AfterResync(Session s)
    {
        if (_lastFleet is not { } last || last.Run != s.RunId || s.Game.Sim is not { } sim || sim.NodeId() != last.Node) return;
        var now = sim.Ships.HumanPlayerSignificantShips.Select(x => x.UniqueID).ToHashSet();
        foreach (var (id, (name, player)) in last.Ships.OrderBy(kv => kv.Key))
        {
            if (now.Contains(id)) continue;
            Tele.Emit(Cat.Combat, "combat.fleet_left", w =>
            {
                w.WriteString("ship", id.ToString());
                w.WriteString("name", name);
                w.WriteNumber("player", player);
                w.WriteString("reason", "resync");
                w.WriteBoolean("in_sim", sim.Ships[id] is not null);
            });
        }
    }

    // ---- Money

    /// <summary>Spec 1586: vanilla charges a blueprint unlock with a direct set of PlayerState.Money, so no money event fires.</summary>
    [HarmonyPatch(typeof(CommTechsTab.MPBuyTechInput), nameof(CommTechsTab.MPBuyTechInput.Execute))]
    static class TechUnlockPatch
    {
        static void Prefix(GameRoot game, int playerIndex, out int __state) => __state = game.GetPlayerState(playerIndex, deterministic: true).Money;

        static void Postfix(CommTechsTab.MPBuyTechInput __instance, GameRoot game, int playerIndex, int __state)
        {
            if (Session.Current is not { } s || s.Game != game || !Tele.On(Cat.Economy)) return;
            int after = game.GetPlayerState(playerIndex, deterministic: true).Money;
            if (after == __state) return;
            Errors.Guard("telemetry tech unlock", () => s.MoneyChanged(playerIndex, after - __state, __state, after, "tech_unlock", $"CommTechsTab.MPBuyTechInput {__instance.TechID}"));
        }
    }
}

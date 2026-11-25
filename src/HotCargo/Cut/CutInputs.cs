using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Halfling.Network;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Cut;

/// <summary>Spec 2463, 2469: a player's cut line becomes an order. Runs in lockstep on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoCutOrder")]
internal sealed class CutOrderInput : MPInput
{
    [Serialize] public ObjectID Ship { get; private set; }
    /// <summary>The line edges as X, Y, down (0 or 1) triples, in line order.</summary>
    [Serialize] public int[] Edges { get; private set; } = [];

    public CutOrderInput(Ship ship, IReadOnlyList<Edge> edges)
    {
        Ship = ship.UniqueID;
        Edges = [.. edges.SelectMany(e => new[] { e.X, e.Y, e.Down ? 1 : 0 })];
    }

    CutOrderInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Errors.Guard("cut order", () =>
    {
        if (Run.Current is not { } run || run.Game != game || CutInputs.OwnShip(game, Ship, playerIndex) is not { } ship) return;
        if (Edges.Length % 3 != 0) throw new InvalidOperationException($"cut order: {Edges.Length} numbers are no edge triples");
        var edges = Enumerable.Range(0, Edges.Length / 3).Select(i => new Edge(Edges[3 * i], Edges[3 * i + 1], Edges[3 * i + 2] == 1)).ToList();
        if (!CutArea.Order(run, ship, edges)) Log.Info($"cut: order of player {playerIndex} refused in the sim");
    });
}

/// <summary>Spec 2463: a player cancels the order on a ship. Runs in lockstep on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoCutCancel")]
internal sealed class CutCancelInput : MPInput
{
    [Serialize] public ObjectID Ship { get; private set; }

    public CutCancelInput(Ship ship) => Ship = ship.UniqueID;

    CutCancelInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Errors.Guard("cut cancel", () =>
    {
        if (Run.Current is { } run && run.Game == game && CutInputs.OwnShip(game, Ship, playerIndex) is { } ship) CutArea.Cancel(run, ship);
    });
}

static class CutInputs
{
    /// <summary>Spec 2470: the ship, when it is a human player's ship in this system that the player commands (as MPUpdateShipInput checks).</summary>
    internal static Ship? OwnShip(GameRoot game, ObjectID id, int playerIndex) =>
        game.Sim?.Ships[id] is { } ship && ship.IsHumanPlayerShip && ship.Commands.IsCommandableBy(playerIndex) ? ship : null;
}

[HarmonyLib.HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowCutInputsPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is CutOrderInput or CutCancelInput) __result = true;
    }
}

/// <summary>Spec 2469: the cut orders and their work in the mode's integrity hash, so a co-op divergence shows at once.</summary>
[HarmonyLib.HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetIntegrityHash))]
static class CutHash
{
    static void Postfix(CareerGameModeManager __instance, IntegrityHash iHash)
    {
        if (Run.Current is not { } run || run.Mode != __instance) return;
        var cut = run.State.Cut;
        iHash.AddHash(cut.Splits);
        iHash.AddHash(cut.Orders.Count);
        foreach (var order in cut.Orders)
            foreach (var e in order.Edges) iHash.AddHash((int)(e.Work * 1000) + (e.Done ? 1 : 0));
    }
}

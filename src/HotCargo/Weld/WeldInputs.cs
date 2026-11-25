using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Halfling.Network;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Cut;

namespace HotCargo.Weld;

/// <summary>Spec 2484, 2491: a player's ghost becomes a weld order. Runs in lockstep on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoWeldOrder")]
internal sealed class WeldOrderInput : MPInput
{
    [Serialize] public ObjectID Ship { get; private set; }
    [Serialize] public ObjectID Target { get; private set; }
    [Serialize] public int Turns { get; private set; }
    [Serialize] public int X { get; private set; }
    [Serialize] public int Y { get; private set; }

    public WeldOrderInput(Ship ship, Ship target, Placement p)
    {
        Ship = ship.UniqueID;
        Target = target.UniqueID;
        (Turns, X, Y) = (p.Turns, p.X, p.Y);
    }

    WeldOrderInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Errors.Guard("weld order", () =>
    {
        if (Run.Current is not { } run || run.Game != game || CutInputs.OwnShip(game, Ship, playerIndex) is not { } ship) return;
        if (game.Sim?.Ships[Target] is not { } target) return;
        if (!WeldArea.Order(run, ship, target, new Placement(Turns & 3, X, Y), playerIndex)) Log.Info($"weld: order of player {playerIndex} refused in the sim");
    });
}

/// <summary>Spec 2484: a player cancels the weld order of a ship. Runs in lockstep on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoWeldCancel")]
internal sealed class WeldCancelInput : MPInput
{
    [Serialize] public ObjectID Ship { get; private set; }

    public WeldCancelInput(Ship ship) => Ship = ship.UniqueID;

    WeldCancelInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Errors.Guard("weld cancel", () =>
    {
        if (Run.Current is { } run && run.Game == game && CutInputs.OwnShip(game, Ship, playerIndex) is { } ship) WeldArea.Cancel(run, ship);
    });
}

[HarmonyLib.HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowWeldInputsPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is WeldOrderInput or WeldCancelInput) __result = true;
    }
}

/// <summary>Spec 2491: the weld orders, their phase and work in the mode's integrity hash, so a co-op divergence shows at once.</summary>
[HarmonyLib.HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetIntegrityHash))]
static class WeldHash
{
    static void Postfix(CareerGameModeManager __instance, IntegrityHash iHash)
    {
        if (Run.Current is not { } run || run.Mode != __instance) return;
        var weld = run.State.Weld;
        iHash.AddHash(weld.Merges);
        iHash.AddHash(weld.Orders.Count);
        foreach (var order in weld.Orders)
        {
            iHash.AddHash((int)order.Phase);
            foreach (var e in order.Joints) iHash.AddHash((int)(e.Work * 1000) + (e.Done ? 1 : 0));
        }
    }
}

using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Halfling.Network;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Diplomacy;

namespace HotCargo.Tactics;

/// <summary>
/// Spec 1307, 1318. One rule (PoolModel.Pools) decides which ships pool construction resources and help each other build, like
/// the ships of one player. Vanilla asks Ship.IsSamePlayerAs for the resource pool within 400
/// (ResourceManager.GetUsableConstructionResourceShips), for crews that carry resources and for construction help
/// (SimModeManager.CanShipHelpWithConstruction, Owned mode). Added to vanilla: AI ships of one faction, and the ships of all
/// human players while the co-op switch is on. With the switch off, player ships keep the vanilla answer.
/// Reads sim state, run state and run tunables only, so it answers the same on every co-op machine.
/// </summary>
[HarmonyPatch(typeof(Ship), nameof(Ship.IsSamePlayerAs), typeof(Ship))]
static class ResourcePool
{
    static void Postfix(Ship __instance, Ship ship, ref bool __result)
    {
        if (__result) return;
        try { __result = Pools(__instance, ship); }
        catch (Exception ex) { Errors.Report("resource pool", ex.Message, ex); }
    }

    public static bool Pools(Ship a, Ship b)
    {
        if (a == b) return true;
        var run = Run.Current;
        // The switch is read only for two player ships, because this runs in vanilla loops over many ships.
        bool shared = run is not null && a.IsHumanPlayerShip && b.IsHumanPlayerShip && SharedOn(run);
        return PoolModel.Pools(Side(a), Side(b), factionPool: run?.T.Tactics.FactionPoolEnabled == true, sharedPlayers: shared);
    }

    /// <summary>Spec 1318: the co-op switch is on and the game has two or more human players.</summary>
    public static bool SharedOn(Run run) => run.State.SharedResources && run.Game.HumanPlayersIndexes.Count > 1;

    static PoolSide Side(Ship s) => new(s.Metadata.PlayerIndex, s.Sim?.Mode.GetTeamFromPlayerIndex(s.Metadata.PlayerIndex) ?? s.Metadata.PlayerIndex,
        s.IsHumanPlayerShip, s.AI is not null, Situation.IsStation(s), s.IsJunk);
}

/// <summary>Spec 1319: the switch is run state, so a divergence between co-op machines shows in the integrity hash.</summary>
[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetIntegrityHash))]
static class SharedResourcesHash
{
    static void Postfix(CareerGameModeManager __instance, IntegrityHash iHash)
    {
        if (Run.Current is { } run && run.Mode == __instance) iHash.AddHash(run.State.SharedResources ? 1 : 0);
    }
}

/// <summary>Spec 1318: a human player turns resource sharing on or off. Runs in lockstep on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoShareResources")]
internal sealed class ShareResourcesInput : MPInput
{
    [Serialize] public bool On { get; private set; }

    public ShareResourcesInput(bool on) => On = on;

    ShareResourcesInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Errors.Guard("share resources", () =>
    {
        if (Run.Current is not { } run || run.Game != game || run.State.SharedResources == On) return;
        run.State.SharedResources = On;
        string who = SharedChoice.SharedChoices.PlayerName(run, playerID);
        Log.Info($"share resources: {who} (player {playerIndex}) turns it {(On ? "on" : "off")}");
        Ux.UxKit.ChatLine(game, $"<limited_color>{Halfling.StringTools.EscapeForXml(Ai.AiText.Format(On ? "ShareOnFmt" : "ShareOffFmt", who))}</limited_color>");
    });
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowShareResourcesPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is ShareResourcesInput) __result = true;
    }
}

/// <summary>
/// Spec 1307, 1318. Construction takes stock from another ship with a transfer job (ResourceManager.TransferConstructionResourcesTo,
/// ManualResourceTransfer), and that job is valid only while the source ship is commandable by the job ship's player
/// (ResourceTransferJob.IsSourceValid). While that one check runs, a source ship that pools with the job's ship counts as
/// commandable. Nothing else changes: no player gets command over another player's ship.
/// </summary>
[HarmonyPatch(typeof(Cosmoteer.Ships.Crew.Jobs.ResourceTransferJob), "IsSourceValid")]
static class PooledTransferScope
{
    /// <summary>The ship of the transfer job whose source is being checked, or null outside the check.</summary>
    [ThreadStatic] internal static Ship? JobShip;

    static void Prefix(Cosmoteer.Ships.Crew.Jobs.ResourceTransferJob __instance) =>
        JobShip = __instance.TradeType == Cosmoteer.Ships.Crew.Jobs.TradeType.None ? __instance.Ship : null;

    static Exception? Finalizer(Exception? __exception)
    {
        JobShip = null;
        return __exception;
    }
}

[HarmonyPatch(typeof(Cosmoteer.Ships.Commands.CommandManager), nameof(Cosmoteer.Ships.Commands.CommandManager.IsCommandableBy))]
static class PooledTransferCommandable
{
    static void Postfix(Cosmoteer.Ships.Commands.CommandManager __instance, int playerIndex, ref bool __result)
    {
        if (__result || PooledTransferScope.JobShip is not { } jobShip || jobShip.Metadata.PlayerIndex != playerIndex) return;
        try { __result = ResourcePool.Pools(__instance.Ship, jobShip); }
        catch (Exception ex) { Errors.Report("pooled transfer", ex.Message, ex); }
    }
}

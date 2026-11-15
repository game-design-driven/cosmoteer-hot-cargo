using Cosmoteer.Game;
using HarmonyLib;

namespace HotCargo.Core;

/// <summary>
/// Spec 1510. Vanilla adds money from the parallel ship update: ResourceManager.FixedUpdate runs on worker threads
/// (SimRoot.ParallelFixedUpdate) and refunds expired pre-purchases with PlayerState.AddMoney. Two ships that refund one player
/// in the same tick race on Money, and a lost add differs between machines: a co-op desync (playtest 2026-11-15).
/// One lock makes each add atomic. Adds commute, so every machine ends the tick with the same total.
/// The event fires outside the lock, as in vanilla, so a handler never waits on another thread while it holds the lock.
/// Limit: a vanilla check of Money followed by an add in another thread can still see a different value on each machine.
/// Revisit if a desync shows such a check.
/// </summary>
[HarmonyPatch(typeof(PlayerState), nameof(PlayerState.AddMoney))]
static class MoneyLock
{
    static readonly Lock Gate = new();

    /// <summary>Same body as PlayerState.AddMoney, with the read and the write of Money under the lock.</summary>
    static bool Prefix(PlayerState __instance, int amount, ref int __result)
    {
        lock (Gate) __result = __instance.Money = (int)Math.Clamp((long)__instance.Money + amount, int.MinValue, int.MaxValue);
        __instance.Game.EventHub.OnPlayerMoneyChanged(__instance);
        return false;
    }
}

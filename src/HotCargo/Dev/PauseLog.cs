using System.Diagnostics;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Dev;

/// <summary>
/// Logs every pause and every change of the game speed, so a game that stops can be explained (a co-op test step stopped with no line in
/// either log, queue run 2026-11-24). Two lines: where the input was made, with the calling code, on the machine that made it; and when it
/// applies, with the player who sent it, on every machine. The game's own inputs carry both (MPSetPausedInput, MPSetClockScaleInput).
/// Logs only: it changes nothing.
/// </summary>
static class PauseLog
{
    /// <summary>The calling code: the first frames outside this class and Harmony, outer type and method, for the log.</summary>
    static string Caller()
    {
        var frames = new StackTrace(2, fNeedFileInfo: false).GetFrames()
            .Select(f => f.GetMethod())
            .Where(m => m?.DeclaringType is { } t && t != typeof(PauseLog) && !t.FullName!.StartsWith("HarmonyLib", StringComparison.Ordinal)
                        && !(t.DeclaringType == typeof(PauseLog)))
            .Take(4)
            .Select(m => $"{m!.DeclaringType!.Name}.{m.Name}");
        return string.Join(" < ", frames);
    }

    [HarmonyPatch(typeof(MPSetPausedInput), MethodType.Constructor, typeof(bool))]
    static class PauseMade
    {
        static void Postfix(bool paused) => Log.Info($"pause input: paused={paused} from {Caller()}");
    }

    [HarmonyPatch(typeof(MPSetPausedInput), nameof(MPSetPausedInput.Execute))]
    static class PauseApplied
    {
        static void Prefix(MPSetPausedInput __instance, GameRoot game, int playerIndex)
        {
            if (game.Sim?.IsPaused.Confirmed != __instance.Paused) Log.Info($"pause: game {(__instance.Paused ? "paused" : "unpaused")} by player {playerIndex}");
        }
    }

    [HarmonyPatch(typeof(MPSetClockScaleInput), MethodType.Constructor, typeof(float))]
    static class SpeedMade
    {
        static void Postfix(float clockScale) => Log.Info($"speed input: x{clockScale} from {Caller()}");
    }

    [HarmonyPatch(typeof(MPSetClockScaleInput), nameof(MPSetClockScaleInput.Execute))]
    static class SpeedApplied
    {
        static void Prefix(MPSetClockScaleInput __instance, GameRoot game, int playerIndex)
        {
            if (game.Sim?.ClockScale.Confirmed != __instance.ClockScale) Log.Info($"speed: game x{__instance.ClockScale} by player {playerIndex}");
        }
    }
}

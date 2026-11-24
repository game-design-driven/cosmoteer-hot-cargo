using Cosmoteer;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Telemetry;

/// <summary>
/// Spec 1997: a game crash (an exception that nothing caught) becomes a "crash" event, and the session ends with reason "crash"
/// before the vanilla crash dialog. The writer then writes its last lines at once, and a co-op client sends the rest of its files
/// to the host, like at a normal game close (spec 1593). Read only: the vanilla crash handling runs unchanged after it.
/// </summary>
static class CrashRecord
{
    /// <summary>Stack text past this length is cut. The vanilla crash log keeps the whole text.</summary>
    const int MaxStackChars = 6000;

    [HarmonyPatch(typeof(GameApp), "OnUnhandledException", [typeof(Exception)])]
    static class CrashPatch
    {
        // A failure here must never hide the crash itself: catch everything, then the vanilla handler runs.
        static void Prefix(Exception ex)
        {
            try { Record(ex); }
            catch (Exception inner) { Log.Error("telemetry crash record failed", inner); }
        }
    }

    internal static void Record(Exception ex)
    {
        if (Session.Current is null) return;
        Tele.Emit(Cat.Run, "crash", ex, static (w, e) =>
        {
            w.WriteString("type", e.GetType().FullName);
            // Spec 1585: no user folder or Steam ID in the telemetry files.
            w.WriteString("msg", TelemetryModel.Scrub(e.Message));
            string stack = TelemetryModel.Scrub(e.ToString());
            w.WriteString("stack", stack.Length <= MaxStackChars ? stack : stack[..MaxStackChars]);
        });
        Session.End("crash");
    }
}

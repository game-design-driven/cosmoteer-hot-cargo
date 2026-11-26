namespace HotCargo.Core;

/// <summary>
/// Reports failures of mod code that runs inside game callbacks (patches, sim ticks, UI frames).
/// A failure must not crash the game: it logs once for each place, shows in the overlay, and the caller skips its work.
/// In the sim tick every multiplayer client fails the same way, so the sim stays in sync.
/// </summary>
public static class Errors
{
    static readonly HashSet<string> Reported = [];

    /// <summary>Logs the first report for each place and shows the newest one in the overlay. Errors stay until the game restarts.</summary>
    public static void Report(string what, string message, Exception? ex = null)
    {
        lock (Reported)
        {
            if (!Reported.Add(what)) return;
        }
        Log.Error($"{what}: {message}", ex);
        Ui.Overlay.FatalError = $"{what}: {message}";
    }

    /// <summary>Test only: a failure of this place is on purpose ("err_step_fail"). It logs no ERROR and does not show in the overlay.</summary>
    internal static string? Expected;

    /// <summary>
    /// Runs the body and reports a failure in place of a crash. Spec 17, Rules: a step that throws can leave a half-done change,
    /// and a sim step runs again in the next tick. So a place whose body failed inside a sim phase (SimPhase) rests: it does not run
    /// again for Debug.FailedStepRestSeconds game seconds. The rest is run state (RunState.RestingSteps): every co-op machine skips
    /// the same ticks, also after a join or a load. Frame code (UI) never rests: a click that failed once can work the next time.
    /// Limit: the failed call itself is not undone. Code that changes several things checks first, then changes (plan, then apply).
    /// </summary>
    public static void Guard(string what, Action body)
    {
        // Sim steps run on the main thread; a parallel sim bucket on a worker thread never rests, so the order of the entries is the same on every machine.
        var run = SimPhase.InPhaseOnMain ? Run.Current : null;
        if (run is not null && run.State.RestingSteps.TryGetValue(what, out double until))
        {
            if (run.State.GameSeconds < until) return;
            run.State.RestingSteps.Remove(what);
        }
        try { body(); }
        catch (Exception ex)
        {
            float rest = run?.T.Debug.FailedStepRestSeconds ?? 0f;
            if (run is not null && rest > 0f) run.State.RestingSteps[what] = run.State.GameSeconds + rest;
            string message = run is not null && rest > 0f ? $"{ex.Message} (this step rests for {rest:0} game seconds)" : ex.Message;
            if (Expected == what) Log.Info($"{what} failed as the test expects: {message}");
            else Report(what, message, ex);
        }
    }
}

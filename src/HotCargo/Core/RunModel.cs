namespace HotCargo.Core;

/// <summary>Pure run math (no game types); the unit tests cover it.</summary>
public static class RunModel
{
    /// <summary>Spec 2555: run progress from 0 at step 1 to 1 at the last step.</summary>
    public static float Progress(int step, int steps) => steps <= 1 ? 1f : Math.Clamp((step - 1f) / (steps - 1f), 0f, 1f);
}

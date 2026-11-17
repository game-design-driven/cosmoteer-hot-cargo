namespace HotCargo.Storyteller;

/// <summary>Spec 1724: the three points of the pressure curve: adapt minutes at the low end, at 0 and at the top, and the pressure there.</summary>
public readonly record struct PressureCurve(float AdaptMin, float AdaptMax, float PressureMin, float PressureMax);

/// <summary>Pure math of the storyteller (spec 1720 to 1731). No game types, so the unit tests run on the Mac.</summary>
public static class StorytellerModel
{
    /// <summary>
    /// Spec 1721 to 1723: adapt minutes after some game minutes and some losses. Time adds perMinute each minute, but not above 0 in
    /// sector 1 (a level above 0 that was reached before stays). Losses take shipLoss per lost ship and crewLoss per killed crew member.
    /// The result stays in the curve's range.
    /// </summary>
    public static float Next(float adapt, double minutes, int shipsLost, int crewKilled, bool sectorOne, float perMinute, float shipLoss, float crewLoss,
        PressureCurve c)
    {
        if (minutes < 0 || shipsLost < 0 || crewKilled < 0) throw new ArgumentOutOfRangeException(nameof(minutes), $"minutes {minutes}, ships {shipsLost}, crew {crewKilled}");
        float gained = adapt + (float)(minutes * perMinute);
        if (sectorOne) gained = Math.Max(adapt, Math.Min(gained, 0f));
        return Math.Clamp(gained - shipsLost * shipLoss - crewKilled * crewLoss, c.AdaptMin, c.AdaptMax);
    }

    /// <summary>
    /// Spec 1724, 1725: the pressure for these adapt minutes: a straight line from the low end to 1 at 0, and from 1 to the top.
    /// impact (0 to 1) moves it toward 1.
    /// </summary>
    public static float Pressure(float adapt, float impact, PressureCurve c)
    {
        if (!(c.AdaptMin < 0 && c.AdaptMax > 0)) throw new ArgumentOutOfRangeException(nameof(c), c, "the curve needs AdaptMin below 0 and AdaptMax above 0");
        float a = Math.Clamp(adapt, c.AdaptMin, c.AdaptMax);
        float curve = a < 0 ? 1f + (c.PressureMin - 1f) * a / c.AdaptMin : 1f + (c.PressureMax - 1f) * a / c.AdaptMax;
        return 1f + Math.Clamp(impact, 0f, 1f) * (curve - 1f);
    }

    /// <summary>Spec 1726: the fleet factor × the pressure, clamped to min..max.</summary>
    public static float Combined(float fleet, float pressure, float min, float max) => Math.Clamp(fleet * pressure, min, max);
}

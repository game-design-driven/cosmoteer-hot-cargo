using System.Globalization;
using HotCargo.Core;

namespace HotCargo.Journey;

/// <summary>Spec 7, 2590: the score of a run as a breakdown. Pure, no game types.</summary>
public static class ScoreModel
{
    /// <summary>
    /// Spec 7: money + units still held × the Buyer price of one unit. Delivered units are already in the money (at the Buyer price),
    /// so each unit counts once, and an unsold unit is worth what the Buyer would pay for it. Other areas add their steps after these
    /// (Run.Hooks.ScoreTerms: the debt rule of requirement 1411).
    /// </summary>
    public static Breakdown Terms(long money, int heldUnits, long unitPrice)
    {
        if (heldUnits < 0 || unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(heldUnits), $"held {heldUnits}, unit price {unitPrice}");
        return new Breakdown(BreakdownUnit.Credits).Base("ScoreMoney", money)
            .Plus("ScoreHeld", heldUnits * unitPrice, string.Format(CultureInfo.InvariantCulture, "{0} x {1:N0}", heldUnits, unitPrice));
    }
}

using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// An objective whose ship meets the players and waits for their "Ready" before it starts (spec 1850 to 1855).
/// <see cref="EscortHail"/> finds it by its ship and shows the question in that ship's comms.
/// </summary>
internal interface IMeetingEscort
{
    ObjectiveTargetShip Escort { get; }

    /// <summary>The ship waits for the players' answer.</summary>
    bool Waiting { get; }

    /// <summary>A player answered "Ready". Runs in the shared choice input, on every machine.</summary>
    void Depart(SimRoot sim, int playerIndex);
}

/// <summary>The meet step that every <see cref="IMeetingEscort"/> runs in its sim tick while it waits (spec 1851 to 1853).</summary>
internal static class EscortMeeting
{
    /// <summary>
    /// Counts down the hail timer, returns the meeting spot (null when the players have no ship here), and hails when a player hull is
    /// within EscortHailGap and the timer allows it. Returns true in <paramref name="firstHail"/> on the first hail: the caller refreshes its line.
    /// </summary>
    internal static Vector2? Step(SimRoot sim, Ship ship, Vector2 start, ref double hailIn, ref bool hailed, out bool firstHail)
    {
        firstHail = false;
        var t = MissionArea.TunablesFor(sim.RequireGame());
        hailIn = Math.Max(0, hailIn - sim.FixedUpdater.Interval.Seconds);
        Vector2 at = ship.DetWorldCenter;
        var player = sim.Ships.HumanPlayerSignificantShips
            .OrderBy(h => h.DetWorldCenter.DistanceSquaredTo(at)).ThenBy(h => h.UniqueID).FirstOrDefault();
        if (player is null) return null;
        float escortRadius = ship.DetWorldBoundingCircle.Radius, playerRadius = player.DetWorldBoundingCircle.Radius;
        var spot = MissionModel.MeetSpot(N(player.DetWorldCenter), playerRadius, player.Sensors.DetWorldCommsCircle.Radius, N(at), escortRadius,
            t.EscortMeetGap, t.EscortCommsMargin, N(start), t.EscortMeetRange);
        float gap = at.DistanceTo(player.DetWorldCenter) - escortRadius - playerRadius;
        if (gap <= t.EscortHailGap && hailIn <= 0 && Events.EventHail.TryHail(sim, ship.UniqueID))
        {
            hailIn = t.EscortRehailSeconds;
            Log.Info($"escort {ship.Metadata.PlainShipName} hails the players (first {!hailed})");
            if (!hailed)
            {
                Ux.UxKit.ChatLine(sim.RequireGame(), Strings.FormatText("HotCargoObjectives/EscortHailedFmt", ship.Metadata.XmlShipName));
                hailed = true;
                firstHail = true;
            }
        }
        return new Vector2(spot.X, spot.Y);
    }

    static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);
}

namespace HotCargo.Ux;

/// <summary>Why a ship does not come along on a system jump (spec 1560). <see cref="None"/>: it jumps.</summary>
public enum StayReason { None, NoDrive, NoControl, TooBig, NeedFuel, NotOrdered, SpinningUp }

/// <summary>
/// What the vanilla jump reads from one ship (GameRoot.DoHumanPlayersFtlJump takes the ships with FtlManager.IsReadyToJump).
/// Cost: hyperium for the jump. Capacity: the hyperium that its working drives can hold. Fuel: the hyperium in them now.
/// Ordered: the ship has the gate jump command. Ready: vanilla IsReadyToJump.
/// </summary>
public readonly record struct JumpFacts(bool HasDrive, bool CanCommand, int Cost, int Capacity, int Fuel, bool Ordered, bool Ready);

/// <summary>Spec 2522: the tech that a move to another system leaves behind: none, some, or all of it.</summary>
public enum TechLeft { None, Some, All }

public static class JumpWarningsModel
{
    /// <summary>Spec 1560: the reason a ship stays behind on the planned jump, in the order a player fixes them.</summary>
    public static StayReason Reason(JumpFacts f) => f switch
    {
        { Ready: true } => StayReason.None,
        { HasDrive: false } => StayReason.NoDrive,
        { CanCommand: false } => StayReason.NoControl,
        _ when f.Cost > f.Capacity => StayReason.TooBig,
        _ when f.Cost > f.Fuel => StayReason.NeedFuel,
        { Ordered: false } => StayReason.NotOrdered,
        _ => StayReason.SpinningUp,
    };

    /// <summary>
    /// Spec 1561, 1562: the lasting reason a ship stays behind on any jump from this system, before a jump is planned.
    /// cheapestCost: the hyperium of the cheapest jump from here, null when no system can be reached.
    /// A ship without a drive counts only when a ship of the fleet has one: before that, no ship can jump and the warning is noise.
    /// Fuel and spin-up are left out: the player fixes them on the way, and vanilla shows them.
    /// </summary>
    public static StayReason Standing(bool hasDrive, bool canCommand, int? cheapestCost, int capacity, bool fleetHasDrive)
    {
        if (!hasDrive) return fleetHasDrive ? StayReason.NoDrive : StayReason.None;
        if (!canCommand) return StayReason.NoControl;
        return cheapestCost is { } cost && cost > capacity ? StayReason.TooBig : StayReason.None;
    }

    /// <summary>
    /// Spec 1566: the system that the players go back to when no human ship is in the current system: the one with the most waiting
    /// ships (a tie keeps the order of the list). Null while a human ship is here, while a jump is under way, or when no ship waits.
    /// The waiting ships hold no tech (spec 2520), so the loss timer runs meanwhile (spec 12); the vanilla map switch brings the players back.
    /// </summary>
    public static TNode? RejoinTarget<TNode>(int shipsHere, bool jumpUnderWay, IEnumerable<(TNode Node, int Ships)> elsewhere) where TNode : class =>
        shipsHere > 0 || jumpUnderWay ? null : elsewhere.Where(e => e.Ships > 0).OrderByDescending(e => e.Ships).Select(e => e.Node).FirstOrDefault();

    /// <summary>
    /// Spec 1601: seconds until the next automatic recall: recallSeconds after the players became stranded, and again recallSeconds after
    /// each recall that did not bring them back (a vote that failed). 0 or less: send it now.
    /// </summary>
    public static double RecallLeft(double now, double strandedSince, double? lastRecall, float recallSeconds) =>
        (lastRecall ?? strandedSince) + recallSeconds - now;

    /// <summary>
    /// Spec 2522: what a move to another system leaves behind. stays: tech that stays in this system and is lost; goes: tech that comes
    /// along. All: no tech comes along. Playtest 2026-11-15: the mothership with the last unit stayed.
    /// </summary>
    public static TechLeft LeftKind(int stays, int goes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stays);
        ArgumentOutOfRangeException.ThrowIfNegative(goes);
        return stays == 0 ? TechLeft.None : goes == 0 ? TechLeft.All : TechLeft.Some;
    }
}

/// <summary>
/// Spec 1563: the ships that joined the fleet since the last poll, each told once per sim. The first poll of a sim (a load or a jump
/// arrival) only takes the baseline, so ships that were already there never count as joined.
/// </summary>
public sealed class JoinWatch<T> where T : notnull
{
    readonly HashSet<T> _known = [];
    object? _sim;

    public List<T> Joined(object sim, IEnumerable<T> ships)
    {
        ArgumentNullException.ThrowIfNull(sim);
        bool fresh = !ReferenceEquals(sim, _sim);
        if (fresh) { _known.Clear(); _sim = sim; }
        var joined = new List<T>();
        foreach (var ship in ships)
            if (_known.Add(ship) && !fresh) joined.Add(ship);
        return joined;
    }
}

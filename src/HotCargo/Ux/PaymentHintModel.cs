namespace HotCargo.Ux;

/// <summary>
/// Spec PAYHINT: when the players get the payment hint. Vanilla Visit missions (and other missions paid by "any station in a system")
/// end in two steps: the arrival, then the receive payment button in the comms of a station of that system. Players miss the second step.
/// </summary>
public static class PaymentHintModel
{
    /// <summary>The payment waits in the current system: it is in progress, every earlier step of its mission is done, and its system is here.</summary>
    public static bool DueHere(bool paymentInProgress, IEnumerable<bool> earlierStepsDone, bool systemIsHere) =>
        paymentInProgress && systemIsHere && earlierStepsDone.All(done => done);

    /// <summary>Once per mission: true only the first time its payment is due here.</summary>
    public static bool Tell<TId>(bool dueHere, ISet<TId> told, TId mission) => dueHere && told.Add(mission);
}

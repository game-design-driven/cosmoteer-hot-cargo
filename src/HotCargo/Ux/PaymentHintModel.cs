namespace HotCargo.Ux;

/// <summary>
/// Spec PAYHINT: when the players get the payment hint. Vanilla Visit missions (and other missions paid by "any station in a system")
/// end in two steps: the arrival, then the receive payment button in the comms of a station of that system. Players miss the second step.
/// </summary>
public static class PaymentHintModel
{
    /// <summary>
    /// The payment waits in the current system: it is in progress, it is the active step of its mission (vanilla
    /// Objective.GetObjectives with activeOnly: a sequence reports only its current step), and its system is here.
    /// </summary>
    public static bool DueHere(bool paymentInProgress, bool paymentActive, bool systemIsHere) =>
        paymentInProgress && paymentActive && systemIsHere;

    /// <summary>Once per mission: true only the first time its payment is due here.</summary>
    public static bool Tell<TId>(bool dueHere, ISet<TId> told, TId mission) => dueHere && told.Add(mission);
}

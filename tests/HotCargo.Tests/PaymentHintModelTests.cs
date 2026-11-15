using HotCargo.Ux;
using Xunit;

namespace HotCargo.Tests;

public class PaymentHintModelTests
{
    [Fact]
    public void The_hint_is_due_only_in_the_payment_system_after_the_arrival()
    {
        // Playtest: "Visit Titraness" arrived (step 1 done) and never called a station there (step 2).
        Assert.True(PaymentHintModel.DueHere(paymentInProgress: true, [true], systemIsHere: true));
        // On the way: the arrival is not done yet.
        Assert.False(PaymentHintModel.DueHere(true, [false], true));
        // Arrived, then left for another system.
        Assert.False(PaymentHintModel.DueHere(true, [true], false));
        // Paid already.
        Assert.False(PaymentHintModel.DueHere(false, [true], true));
        // A payment with no earlier step is due as soon as the players are there.
        Assert.True(PaymentHintModel.DueHere(true, [], true));
    }

    [Fact]
    public void Each_mission_is_told_once()
    {
        var told = new HashSet<int>();
        Assert.False(PaymentHintModel.Tell(false, told, 1));
        Assert.True(PaymentHintModel.Tell(true, told, 1));
        Assert.False(PaymentHintModel.Tell(true, told, 1));
        Assert.True(PaymentHintModel.Tell(true, told, 2));
    }
}

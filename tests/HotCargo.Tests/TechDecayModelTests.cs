using HotCargo.Tech;
using Xunit;

public class TechDecayModelTests
{
    const double Limit = 360;
    static readonly Func<TechSpot, bool> TilesOnly = s => !s.Key.StartsWith('c');

    static (List<TechWear> Next, List<string> Destroy) Step(List<TechWear> prev, double dt, DecayInVault mode, params TechSpot[] seen) =>
        TechDecayModel.Step(prev, seen, dt, Limit, mode, TilesOnly);

    static double Wear(List<TechWear> state, string key) => state.SingleOrDefault(w => w.Key == key).Wear;

    [Fact]
    public void VaultsNeededRoundsUp()
    {
        Assert.Equal(2, TechModel.VaultsNeeded(8, 4));
        Assert.Equal(3, TechModel.VaultsNeeded(9, 4));
        Assert.Equal(0, TechModel.VaultsNeeded(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechModel.VaultsNeeded(8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechModel.VaultsNeeded(-1, 4));
    }

    [Fact]
    public void StorageDecaysAndIsDestroyedAtTheLimit()
    {
        var state = new List<TechWear>();
        for (int i = 0; i < 359; i++) state = Step(state, 1, DecayInVault.Pause, new TechSpot("s1", true)).Next;
        Assert.Equal(359, Wear(state, "s1"), 6);
        var (next, destroy) = Step(state, 1, DecayInVault.Pause, new TechSpot("s1", true));
        Assert.Equal(["s1"], destroy);
        Assert.Empty(next);
    }

    [Fact]
    public void TechInAVaultHasNoWear()
    {
        var (next, destroy) = Step([], 100, DecayInVault.Pause, new TechSpot("v1", false));
        Assert.Empty(next);
        Assert.Empty(destroy);
    }

    [Fact]
    public void CrewCarryWearFromStorageToVault()
    {
        var state = new List<TechWear> { new("s1", 200, Group: "ship") };
        // Pickup: the tile is empty, the crew member's job names it as source.
        state = Step(state, 1, DecayInVault.Pause, new TechSpot("c7", true, From: "s1", To: "v3")).Next;
        Assert.Equal(201, Wear(state, "c7"), 6);
        // Delivery: the crew member is gone, the vault tile that its job named holds the stack.
        state = Step(state, 1, DecayInVault.Pause, new TechSpot("v3", false, Group: "ship")).Next;
        Assert.Equal(201, Wear(state, "v3"), 6);
        // Pause: the wear stays in the vault.
        state = Step(state, 500, DecayInVault.Pause, new TechSpot("v3", false, Group: "ship")).Next;
        Assert.Equal(201, Wear(state, "v3"), 6);
    }

    [Fact]
    public void PickupFromAStackCopiesItsWear()
    {
        // A stack of 3 in storage: crew take 1, the tile keeps 2 and its wear.
        var state = new List<TechWear> { new("s1", 100, Group: "a") };
        var (next, _) = Step(state, 1, DecayInVault.Pause, new TechSpot("s1", true, Group: "a"), new TechSpot("c7", true, From: "s1", To: "s2"));
        Assert.Equal(101, Wear(next, "s1"), 6);
        Assert.Equal(101, Wear(next, "c7"), 6);
    }

    [Fact]
    public void DeliveryOntoAStackKeepsTheHigherWear()
    {
        var state = new List<TechWear> { new("v3", 50, Group: "a"), new("c7", 200, To: "v3") };
        var (next, _) = Step(state, 1, DecayInVault.Pause, new TechSpot("v3", false, Group: "a"));
        Assert.Equal(200, Wear(next, "v3"), 6);
    }

    [Fact]
    public void ResetRecoversInTheVault()
    {
        var state = new List<TechWear> { new("c7", 300, To: "v3") };
        var (next, _) = Step(state, 1, DecayInVault.Reset, new TechSpot("v3", false));
        Assert.Empty(next);
    }

    [Fact]
    public void CarriedTechPastTheLimitBreaksWhenSetDown()
    {
        var state = new List<TechWear> { new("c7", 359.5, To: "s2") };
        var carried = Step(state, 1, DecayInVault.Pause, new TechSpot("c7", true, To: "s2"));
        Assert.Empty(carried.Destroy);
        var down = Step(carried.Next, 1, DecayInVault.Pause, new TechSpot("s2", true));
        Assert.Equal(["s2"], down.Destroy);
    }

    [Fact]
    public void InstantMoveOnOneShipKeepsTheHighestWear()
    {
        var state = new List<TechWear> { new("s1", 100, Group: "a"), new("s2", 250, Group: "a"), new("s9", 300, Group: "b") };
        var (next, _) = Step(state, 0, DecayInVault.Pause, new TechSpot("s1", true, Group: "a"), new TechSpot("s5", true, Group: "a"));
        Assert.Equal(250, Wear(next, "s5"), 6);
        Assert.DoesNotContain(next, w => w.Key == "s9");
    }

    [Fact]
    public void NewTechStartsFresh()
    {
        var state = new List<TechWear> { new("s1", 300, Group: "a") };
        // s1 was sold (gone); a crate pickup lands on another ship: no match, no wear.
        var (next, _) = Step(state, 1, DecayInVault.Pause, new TechSpot("s4", true, Group: "b"));
        Assert.Equal(1, Wear(next, "s4"), 6);
    }

    [Fact]
    public void ADuplicatePlaceCrashes() =>
        Assert.Throws<ArgumentException>(() => Step([], 1, DecayInVault.Pause, new TechSpot("s1", true), new TechSpot("s1", true)));
}

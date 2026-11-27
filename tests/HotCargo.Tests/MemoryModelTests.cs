using HotCargo.Events;
using HotCargo.Pursuit;
using Xunit;

namespace HotCargo.Tests;

public class MemoryModelTests
{
    static MemoryFacts M(MemoryKind kind, int sector, bool recalled = false) => new(kind, sector, recalled);

    [Fact]
    public void Pick_takes_the_oldest_unrecalled_memory_of_the_kind()
    {
        var list = new[] { M(MemoryKind.Spared, 1, recalled: true), M(MemoryKind.PaidOff, 1), M(MemoryKind.Spared, 2), M(MemoryKind.Spared, 3) };
        Assert.Equal(2, MemoryModel.Pick(list, MemoryKind.Spared, sector: 5, minSectors: 1));
    }

    [Fact]
    public void Pick_waits_until_the_memory_is_old_enough()
    {
        var list = new[] { M(MemoryKind.Spared, 3) };
        Assert.Equal(-1, MemoryModel.Pick(list, MemoryKind.Spared, sector: 3, minSectors: 1));
        Assert.Equal(0, MemoryModel.Pick(list, MemoryKind.Spared, sector: 4, minSectors: 1));
    }

    [Fact]
    public void Pick_finds_nothing_for_another_kind_or_a_recalled_memory()
    {
        var list = new[] { M(MemoryKind.Bluffed, 1), M(MemoryKind.Spared, 1, recalled: true) };
        Assert.Equal(-1, MemoryModel.Pick(list, MemoryKind.Spared, sector: 5, minSectors: 1));
    }

    [Fact]
    public void Pick_of_no_kind_is_a_bug() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryModel.Pick([], MemoryKind.None, 1, 1));

    [Fact]
    public void A_surrender_is_remembered_by_faction_and_never_for_a_hunter()
    {
        Assert.Equal(MemoryKind.Spared, MemoryModel.SparedKind(hunter: false, hasFaction: true));
        Assert.Equal(MemoryKind.SparedPirate, MemoryModel.SparedKind(hunter: false, hasFaction: false));
        Assert.Equal(MemoryKind.None, MemoryModel.SparedKind(hunter: true, hasFaction: true));
    }

    [Fact]
    public void Each_kind_comes_back_once_per_run_and_the_pirate_pair_counts_as_one_kind()
    {
        // User rule 2026-11-26: "never of same type". Pirate's Honor and Unfinished Business both recall SparedPirate.
        var opened = new[] { (MemoryKind.SparedPirate, 0), (MemoryKind.SparedPirate, 1), (MemoryKind.Spared, 0) };
        Assert.True(MemoryModel.KindUsed(opened, MemoryKind.SparedPirate));
        Assert.False(MemoryModel.KindUsed(opened, MemoryKind.Spared));
        Assert.False(MemoryModel.KindUsed(opened, MemoryKind.Bluffed));
    }

    [Fact]
    public void Four_callbacks_fit_in_a_run_and_a_fifth_does_not()
    {
        // User rule 2026-11-26: "can do 4". The cap is the pacer's run limit of the kind Callback (spec 905).
        var four = Enumerable.Range(0, 4).Select(i => new PaceStart("Callback", i * 1000.0)).ToList();
        Assert.Equal(PaceVerdict.Ok, PursuitModel.MayStart(four.Take(3).ToList(), "Callback", 5000, MemoryModel.CallbackMaxPerRun, 900, 0));
        Assert.Equal(PaceVerdict.RunLimit, PursuitModel.MayStart(four, "Callback", 5000, MemoryModel.CallbackMaxPerRun, 900, 0));
    }

    [Fact]
    public void A_spared_crew_joins_free_more_often_the_better_the_standing_with_its_faction()
    {
        // User decision 2026-11-27: free or discounted, by a roll whose odds depend on the faction (spec 2506).
        Assert.Equal(0.5f, MemoryModel.JoinFreeChance(reputation: 0f, freeBase: 0.5f, freePerReputation: 0.4f), 3);
        Assert.Equal(0.9f, MemoryModel.JoinFreeChance(reputation: 1f, freeBase: 0.5f, freePerReputation: 0.4f), 3);
        Assert.Equal(0.1f, MemoryModel.JoinFreeChance(reputation: -1f, freeBase: 0.5f, freePerReputation: 0.4f), 3);
        Assert.Equal(1f, MemoryModel.JoinFreeChance(reputation: 1f, freeBase: 0.9f, freePerReputation: 0.5f), 3);
        Assert.Equal(0f, MemoryModel.JoinFreeChance(reputation: -1f, freeBase: 0.1f, freePerReputation: 0.5f), 3);
    }

    [Fact]
    public void A_join_that_is_not_free_costs_a_share_of_the_join_request_price()
    {
        Assert.Equal(0f, MemoryModel.JoinUnits(free: true, joinRequestUnits: 1f, discountedShare: 0.5f));
        Assert.Equal(0.5f, MemoryModel.JoinUnits(free: false, joinRequestUnits: 1f, discountedShare: 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryModel.JoinUnits(free: false, joinRequestUnits: 1f, discountedShare: 1f));
    }
}

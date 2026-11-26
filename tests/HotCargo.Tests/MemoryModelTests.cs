using HotCargo.Events;
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
}

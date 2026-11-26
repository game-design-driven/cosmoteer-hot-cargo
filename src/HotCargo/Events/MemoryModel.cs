namespace HotCargo.Events;

/// <summary>Spec 2500: a past act of the players that the run remembers, so a callback page can bring it back later.</summary>
public enum MemoryKind
{
    None,
    /// <summary>The players accepted the surrender of a faction ship that is not a hunter.</summary>
    Spared,
    /// <summary>The players accepted the surrender of a pirate: a ship with no faction that is not a hunter.</summary>
    SparedPirate,
    /// <summary>A hunter took the players' pay-off, and its wave left.</summary>
    PaidOff,
    /// <summary>A hunter believed the players' bluff, and its wave left.</summary>
    Bluffed,
    /// <summary>The players saved the victim of a mayday.</summary>
    Rescued,
}

/// <summary>What the pure rules need of a memory.</summary>
public readonly record struct MemoryFacts(MemoryKind Kind, int Sector, bool Recalled);

/// <summary>Pure rules of memories and callbacks (spec 2500 to 2519). No game types, so the unit tests run on the Mac.</summary>
public static class MemoryModel
{
    /// <summary>
    /// Spec 2503: the memory that a callback of this kind recalls: the oldest one of the kind that no callback recalled yet and that is at
    /// least minSectors old (sector now minus the sector of the act). -1 when there is none.
    /// </summary>
    public static int Pick(IReadOnlyList<MemoryFacts> memories, MemoryKind kind, int sector, int minSectors)
    {
        if (kind == MemoryKind.None) throw new ArgumentOutOfRangeException(nameof(kind), kind, "a callback recalls a real kind");
        for (int i = 0; i < memories.Count; i++)
        {
            var m = memories[i];
            if (m.Kind == kind && !m.Recalled && sector - m.Sector >= minSectors) return i;
        }
        return -1;
    }

    /// <summary>Spec 2501: the kind of memory that an accepted surrender leaves. A hunter's surrender leaves none.</summary>
    public static MemoryKind SparedKind(bool hunter, bool hasFaction) =>
        hunter ? MemoryKind.None : hasFaction ? MemoryKind.Spared : MemoryKind.SparedPirate;
}

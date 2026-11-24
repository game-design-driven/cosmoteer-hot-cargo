using HotCargo.Ux;
using Xunit;

namespace HotCargo.Tests;

public class TechWatchModelTests
{
    [Fact]
    public void Chosen_places_do_not_alarm()
    {
        Assert.False(TechWatchModel.Alarms(WatchKind.Friend));
        Assert.False(TechWatchModel.Alarms(WatchKind.Waiting));
        Assert.True(TechWatchModel.Alarms(WatchKind.Storage));
        Assert.True(TechWatchModel.Alarms(WatchKind.Crates));
        Assert.True(TechWatchModel.Alarms(WatchKind.Design));
    }

    [Fact]
    public void Crates_near_the_lowest_id_form_one_group()
    {
        var groups = TechWatchModel.Groups([new(5, 50, 0, 1), new(2, 0, 0, 1), new(9, 1000, 0, 2), new(7, 80, 0, 1)], 100);
        Assert.Equal([new CratePoint(2, 0, 0, 3), new CratePoint(9, 1000, 0, 2)], groups);
    }

    [Fact]
    public void The_design_warning_needs_a_removed_vault_and_too_little_room()
    {
        // The playtest: a rebuild with no vault in the new design, 8 tech in 2 vaults.
        Assert.Equal(8, TechWatchModel.DesignShort(shipTech: 8, builtVaults: 2, blueprintVaults: 0, unitsPerVault: 4));
        Assert.Equal(4, TechWatchModel.DesignShort(8, 2, 1, 4));
        // A move with the grab tool keeps the vault count: the tech waits for the new vault.
        Assert.Equal(0, TechWatchModel.DesignShort(8, 2, 2, 4));
        // Room left after the removal.
        Assert.Equal(0, TechWatchModel.DesignShort(3, 2, 1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechWatchModel.DesignShort(8, 2, 0, 0));
    }

    [Fact]
    public void The_alarm_waits_then_shows_once_until_all_is_safe()
    {
        var w = new AlarmWatch();
        Assert.False(w.Update(0, true, 3));
        Assert.False(w.Update(2.9, true, 3));
        Assert.True(w.Update(3, true, 3));
        Assert.False(w.Update(10, true, 3));
        Assert.False(w.Update(11, false, 3));
        Assert.False(w.Update(12, true, 3));
        Assert.True(w.Update(15, true, 3));
    }

    [Fact]
    public void A_short_move_between_vaults_stays_quiet()
    {
        var w = new AlarmWatch();
        Assert.False(w.Update(0, true, 3));
        Assert.False(w.Update(2, false, 3));
        Assert.False(w.Update(4, true, 3));
        Assert.False(w.Update(6, true, 3));
    }
}

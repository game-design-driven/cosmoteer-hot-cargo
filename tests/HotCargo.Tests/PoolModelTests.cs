using HotCargo.Tactics;
using Xunit;

namespace HotCargo.Tests;

public class PoolModelTests
{
    static PoolSide Ai(int player, int team) => new(player, team, Human: false, Ai: true, Station: false, Junk: false);
    static PoolSide Human(int player) => new(player, player, Human: true, Ai: false, Station: false, Junk: false);

    [Fact]
    public void Same_player_pools_as_in_vanilla_but_never_on_a_special_index()
    {
        Assert.True(PoolModel.Pools(Human(0), Human(0), factionPool: false, sharedPlayers: false));
        Assert.True(PoolModel.Pools(Ai(100, 1), Ai(100, 1), factionPool: false, sharedPlayers: false));
        Assert.False(PoolModel.Pools(Ai(-2, -2), Ai(-2, -2), factionPool: true, sharedPlayers: true));
    }

    [Fact]
    public void Ai_ships_of_one_faction_pool_while_the_faction_pool_is_on()
    {
        Assert.True(PoolModel.Pools(Ai(100, 1), Ai(101, 1), factionPool: true, sharedPlayers: false));
        Assert.False(PoolModel.Pools(Ai(100, 1), Ai(101, 1), factionPool: false, sharedPlayers: false));
        Assert.False(PoolModel.Pools(Ai(100, 1), Ai(200, 2), factionPool: true, sharedPlayers: false));
        Assert.False(PoolModel.Pools(Ai(100, 1), Ai(101, 1) with { Station = true }, factionPool: true, sharedPlayers: false));
        Assert.False(PoolModel.Pools(Ai(100, 1), Ai(101, 1) with { Junk = true }, factionPool: true, sharedPlayers: false));
    }

    [Fact]
    public void Players_pool_only_with_each_other_and_only_while_the_switch_is_on()
    {
        Assert.False(PoolModel.Pools(Human(0), Human(1), factionPool: true, sharedPlayers: false));
        Assert.True(PoolModel.Pools(Human(0), Human(1), factionPool: false, sharedPlayers: true));
        // A player never pools with an AI ship, even of the same team.
        Assert.False(PoolModel.Pools(Human(0), Ai(100, 0), factionPool: true, sharedPlayers: true));
    }
}

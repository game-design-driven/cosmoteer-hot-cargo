using HotCargo.Journey;
using Xunit;

namespace HotCargo.Tests;

public class RunMapModelTests
{
    static Func<float> Rand(uint seed)
    {
        uint s = seed * 2654435761u + 1u;
        return () => { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s >> 8) / (float)(1 << 24); };
    }

    static IEnumerable<(int Steps, RunMapLayout Map)> Maps() =>
        from steps in new[] { 2, 3, 4, 6, 8, 10 }
        from seed in Enumerable.Range(1, 200)
        select (steps, RunMapModel.Generate(steps, Rand((uint)(seed * 31 + steps))));

    [Fact]
    public void One_start_and_one_Buyer_and_a_lens_in_between()
    {
        Assert.Equal([1, 4, 5, 5, 4, 1], Enumerable.Range(0, 6).Select(b => RunMapModel.Width(b, 6)));
        foreach (var (steps, map) in Maps())
        {
            int last = RunMapModel.LastBand(steps);
            Assert.Single(map.Nodes, n => n.Band == 0);
            Assert.Single(map.Nodes, n => n.Band == last);
            // The band follows from the X location (no save state).
            Assert.All(map.Nodes, n => Assert.Equal(n.Band, RunMapModel.Band(n.X)));
        }
    }

    [Fact]
    public void Every_system_has_two_ways_ahead_except_the_last_band_which_has_the_Buyer()
    {
        foreach (var (steps, map) in Maps())
        {
            int last = RunMapModel.LastBand(steps);
            for (int i = 0; i < map.Nodes.Count; i++)
            {
                int band = map.Nodes[i].Band;
                if (band == last) continue;
                int ahead = RunMapModel.Neighbors(map, i).Count(n => map.Nodes[n].Band == band + 1);
                Assert.True(ahead >= (band == last - 1 ? 1 : 2), $"steps {steps}: system {i} in band {band} has {ahead} ways ahead");
                if (band > 0) Assert.Contains(RunMapModel.Neighbors(map, i), n => map.Nodes[n].Band == band - 1);
            }
        }
    }

    [Fact]
    public void The_direct_route_takes_steps_minus_one_jumps_and_old_lanes_skip_bands_but_never_reach_the_Buyer()
    {
        foreach (var (steps, map) in Maps())
        {
            int last = RunMapModel.LastBand(steps);
            var lanes = map.Links.Where(l => RunMapModel.Kind(map.Nodes[l.A].Band, map.Nodes[l.B].Band) == LinkKind.OldLane).ToList();
            Assert.True(lanes.Count <= RunMapModel.OldLanes(steps));
            Assert.All(lanes, l => Assert.True(Math.Max(map.Nodes[l.A].Band, map.Nodes[l.B].Band) < last));
            if (steps == 6) Assert.Equal(2, lanes.Count);
        }
    }

    [Fact]
    public void Tier_rises_from_one_to_the_final_tier()
    {
        Assert.Equal([1, 3, 5, 8, 10, 12], Enumerable.Range(0, 6).Select(b => RunMapModel.Tier(b, 6, 12)));
        Assert.Equal(12, RunMapModel.Tier(9, 10, 12));
    }
}

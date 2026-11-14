using System.Numerics;
using HotCargo.Ux;
using Xunit;

namespace HotCargo.Tests;

public class PingModelTests
{
    static readonly Vector2 Here = new(10, 20), There = new(-5, 7);

    [Fact]
    public void A_new_ping_replaces_the_players_old_one_and_keeps_the_others()
    {
        var book = new PingBook<string, int>();
        book.Place("a", Here, null, 0);
        book.Place("b", Here, null, 1);
        book.Place("a", There, 3, 2);
        Assert.Equal(2, book.Marks.Count);
        Assert.True(book.TryGet("a", out var a));
        Assert.Equal((There, 3, 2.0), (a.At, a.Target, a.Since));
    }

    [Fact]
    public void A_ping_ends_after_its_lifetime()
    {
        var book = new PingBook<string, int>();
        book.Place("a", Here, null, 0);
        book.Place("b", Here, null, 20);
        Assert.Empty(book.Prune(29.9, 30, _ => true));
        Assert.Equal(["a"], book.Prune(30, 30, _ => true));
        Assert.Equal(["b"], book.Marks.Select(m => m.Player));
        Assert.Equal(["b"], book.Prune(20, 0, _ => true));
    }

    [Fact]
    public void A_ping_on_an_object_follows_it_and_ends_when_it_is_gone()
    {
        var book = new PingBook<string, int>();
        var mark = book.Place("a", Here, 7, 0);
        Assert.Equal(There, PingBook<string, int>.Where(mark, id => id == 7 ? There : null));
        // While the object is out of sight it shows where it was placed.
        Assert.Equal(Here, PingBook<string, int>.Where(mark, _ => null));
        Assert.Equal(["a"], book.Prune(1, 30, id => id != 7));
        Assert.Empty(book.Marks);
    }
}

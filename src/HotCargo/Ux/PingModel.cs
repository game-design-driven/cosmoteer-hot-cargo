using System.Numerics;

namespace HotCargo.Ux;

/// <summary>
/// The pings of the players (spec 1500 to 1509): at most one per player, each with the point that the player clicked, the object
/// that it follows (or none) and the time it was placed. Pure, so the unit tests check it; the game side (Ping) keeps one book.
/// </summary>
public sealed class PingBook<TPlayer, TTarget> where TPlayer : notnull where TTarget : struct
{
    public readonly record struct Mark(TPlayer Player, Vector2 At, TTarget? Target, double Since);

    readonly Dictionary<TPlayer, Mark> _marks = [];

    public IReadOnlyCollection<Mark> Marks => _marks.Values;

    public bool TryGet(TPlayer player, out Mark mark) => _marks.TryGetValue(player, out mark);

    /// <summary>Places a ping. A new ping of the same player replaces that player's earlier ping.</summary>
    public Mark Place(TPlayer player, Vector2 at, TTarget? target, double now) => _marks[player] = new(player, at, target, now);

    /// <summary>
    /// Removes the pings that ended: placed lifetime seconds ago or earlier, or whose object no longer exists. Returns their players.
    /// A lifetime of 0 or less ends every ping.
    /// </summary>
    public List<TPlayer> Prune(double now, float lifetime, Func<TTarget, bool> exists)
    {
        var ended = _marks.Values.Where(m => now - m.Since >= lifetime || m.Target is { } t && !exists(t)).Select(m => m.Player).ToList();
        foreach (var p in ended) _marks.Remove(p);
        return ended;
    }

    public void Clear() => _marks.Clear();

    /// <summary>Where a ping shows: at its object while it follows one, else where it was placed.</summary>
    public static Vector2 Where(Mark mark, Func<TTarget, Vector2?> locate) => mark.Target is { } t && locate(t) is { } at ? at : mark.At;
}

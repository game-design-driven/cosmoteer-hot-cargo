namespace HotCargo.Ux;

/// <summary>An axis-aligned box in GUI units, Y down. No game types.</summary>
public readonly record struct HudBox(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    /// <summary>True when the two boxes share area. Boxes that only touch do not overlap.</summary>
    public bool Overlaps(HudBox o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    /// <summary>True when this box lies inside the other, with half a GUI unit of slack for rounding (as spec 1481).</summary>
    public bool Inside(HudBox o) => X >= o.X - 0.5f && Y >= o.Y - 0.5f && Right <= o.Right + 0.5f && Bottom <= o.Bottom + 0.5f;
}

/// <summary>Spec 1132, 1318: where the bottom-left HUD widgets of Hot Cargo go, and whether the players can see them. No game types.</summary>
public static class HudStackModel
{
    /// <summary>
    /// The top of a widget in a column: as low as possible (gap above the screen bottom), but above every blocker that it overlaps.
    /// Each pass moves it above the highest blocker that it overlaps, so it never overlaps that blocker again: at most one pass per blocker.
    /// </summary>
    public static float StackTop(HudBox screen, float x, float width, float height, float gap, IReadOnlyList<HudBox> blockers)
    {
        if (!(width > 0 && height > 0 && gap >= 0)) throw new ArgumentOutOfRangeException(nameof(width), $"size {width}x{height}, gap {gap}");
        float top = screen.Bottom - gap - height;
        for (int pass = 0; pass <= blockers.Count; pass++)
        {
            var me = new HudBox(x, top, width, height);
            float? highest = null;
            foreach (var b in blockers)
                if (me.Overlaps(b)) highest = Math.Min(highest ?? b.Y, b.Y);
            if (highest is not { } y) return top;
            top = y - gap - height;
        }
        throw new InvalidOperationException("the HUD stack did not settle");
    }

    /// <summary>True when the widget lies inside the screen and no blocker (a panel or an open window) covers any part of it.</summary>
    public static bool Visible(HudBox widget, HudBox screen, IEnumerable<HudBox> blockers) =>
        widget.Inside(screen) && !blockers.Any(widget.Overlaps);
}

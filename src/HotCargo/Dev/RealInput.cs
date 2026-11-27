using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Null;
using Halfling.Gui;
using Halfling.Input;

namespace HotCargo.Dev;

/// <summary>
/// Test only: mouse and key input through the game's own input path. Every host reads its buttons and the cursor through
/// <see cref="InputManager.InputFilter"/> (the Windows raw input, the Mac host, the null input of the headless sim), and this filter
/// sets them. A click is three frames: the cursor on the widget, the press, the release. So the widget's own click controller decides
/// whether the click fires, as it does for a player. A widget method call skips that, and a test that calls one can pass while the
/// player's click does nothing (issue #393: the button turned off and on each frame, and Halfling drops a press when a widget turns off).
/// <para>
/// A click fails when its widget is hidden or disabled at the first frame. In a game that draws, it fails when the widget's click event
/// does not fire (a menu: when it takes no focus). Without a renderer it fails when the widget turns off or loses input before the frame
/// after the release. Without a renderer (the headless sim, the headless PC slots) the GUI has no real screen layout, so the cursor cannot
/// hit the widget: there, a click that passes these checks does what the press and release do (a menu opens, a button fires its
/// click event), without the mouse button. Revisit if the headless
/// hosts get a real GUI layout; then the mouse alone can decide everywhere.
/// </para>
/// </summary>
internal sealed class RealInput : IInputFilter
{
    enum Phase { Hover, Press, Release, Key }

    /// <summary>One frame of input.</summary>
    sealed record Step(Phase Phase, Func<Widget?>? Target, Func<Vector2?>? Pixel, InputButtonID[] Down);

    static readonly RealInput Instance = new();
    static readonly Queue<Step> Steps = new();
    static IntVector2? _cursor;
    static HashSet<InputButtonID> _down = [], _wasDown = [];
    static bool _hooked;
    /// <summary>The widget of the click that runs, and whether it turned off or lost input since its first frame.</summary>
    static Widget? _watched;
    static bool _turnedOff, _released, _clicked;

    /// <summary>True while steps wait, a button is still down or a click waits for its check.</summary>
    internal static bool Busy => Steps.Count > 0 || _down.Count > 0 || _wasDown.Count > 0 || _watched is not null;

    /// <summary>False when a click of the last sequence failed: its widget was hidden or disabled, or turned off during the click.</summary>
    internal static bool AllHit { get; private set; } = true;

    static bool Headless => App.Graphics is NullGraphicsManager;

    /// <summary>A left click on the widget that <paramref name="target"/> returns at the frame of the click.</summary>
    internal static void Click(Func<Widget?> target)
    {
        Start();
        Steps.Enqueue(new(Phase.Hover, target, null, []));
        Steps.Enqueue(new(Phase.Press, target, null, [InputButtonID.MouseLeft]));
        Steps.Enqueue(new(Phase.Release, target, null, []));
    }

    /// <summary>A right click at a pixel of the window, for example over the world (the tools read it, not the GUI).</summary>
    internal static void RightClickAt(Func<Vector2?> pixel)
    {
        Start();
        Steps.Enqueue(new(Phase.Key, null, pixel, []));
        Steps.Enqueue(new(Phase.Key, null, pixel, [InputButtonID.MouseRight]));
        Steps.Enqueue(new(Phase.Key, null, pixel, []));
    }

    /// <summary>The keys down together for one frame, then up. Modifiers come first: they are down one frame before the others.</summary>
    internal static void Keys(InputButtonID[] modifiers, InputButtonID[] keys)
    {
        Start();
        if (modifiers.Length > 0) Steps.Enqueue(new(Phase.Key, null, null, modifiers));
        Steps.Enqueue(new(Phase.Key, null, null, [.. modifiers, .. keys]));
        Steps.Enqueue(new(Phase.Key, null, null, []));
    }

    /// <summary>Several calls in one frame make one sequence; a call while an older sequence runs is an error.</summary>
    static void Start()
    {
        if (_down.Count > 0 || _wasDown.Count > 0 || _watched is not null) throw new InvalidOperationException("real input: the last input has not ended");
        if (Steps.Count == 0) AllHit = true;
        if (_hooked) return;
        App.Director.FrameEnded += (_, _) => Advance();
        _hooked = true;
    }

    /// <summary>After each frame: the next step's input is what the next frame reads.</summary>
    static void Advance()
    {
        if (_watched is { } w && _released) EndClick(w);
        if (!Busy)
        {
            if (App.Input.InputFilter == Instance) App.Input.InputFilter = null;
            _cursor = null;
            return;
        }
        if (App.Input.InputFilter is { } other && other != Instance)
            throw new InvalidOperationException($"real input: the game uses its own input filter ({other.GetType().Name})");
        App.Input.InputFilter = Instance;
        _wasDown = _down;
        if (!Steps.TryDequeue(out var step))
        {
            _down = [];
            return;
        }
        // Without a renderer no widget is under the cursor, and a press would count as a click outside (an open menu closes).
        _down = Headless && step.Phase == Phase.Press ? [] : [.. step.Down];
        if (step.Pixel?.Invoke() is { } p) _cursor = new IntVector2((int)p.X, (int)p.Y);
        switch (step.Phase)
        {
            case Phase.Hover: BeginClick(step); break;
            case Phase.Release: _released = true; break;
        }
    }

    static void BeginClick(Step step)
    {
        if (step.Target!() is { IsActive: true, IsInputActive: true, Root: { } root } w)
        {
            var c = root.TransformToPixels(w.ScreenRect.Center);
            _cursor = new IntVector2((int)c.X, (int)c.Y);
            _watched = w;
            _turnedOff = _released = _clicked = false;
            w.Deactivated += OnTurnedOff;
            w.InputDeactivated += OnTurnedOff;
            if (w is Button b) b.ClickController.Clicked += OnClicked;
            return;
        }
        var miss = step.Target();
        Log.Info($"AUTOTEST real input: nothing to click: {(miss is null ? "no widget shows" : $"{miss.GetType().Name} active {miss.IsActive}, input {miss.IsInputActive}")}");
        Fail();
    }

    static void EndClick(Widget w)
    {
        w.Deactivated -= OnTurnedOff;
        w.InputDeactivated -= OnTurnedOff;
        if (w is Button b) b.ClickController.Clicked -= OnClicked;
        _watched = null;
        _released = false;
        if (!Headless)
        {
            // The mouse decided: the click fired, or a menu took focus on the press.
            if (_clicked || w is MenuCategory { FocusController.IsFocused: true }) return;
            Log.Info($"AUTOTEST real input: the click on {w.GetType().Name} did not fire{(_turnedOff ? "; it turned off or lost input during the click" : "")}");
            Fail();
            return;
        }
        if (_turnedOff || !w.IsActive || !w.IsInputActive)
        {
            Log.Info($"AUTOTEST real input: {w.GetType().Name} turned off or lost input during the click; a player's click does not fire");
            Fail();
            return;
        }
        // What the press and the release do: a menu opens on the press (WidgetMouseFocusController), a button fires its click.
        if (w is MenuCategory menu) menu.FocusController.RequestFocus();
        else (w as Button ?? throw new InvalidOperationException($"real input: cannot click a {w.GetType().Name} without a renderer")).ClickController.InvokeClicked();
    }

    /// <summary>A turn-off after the click fired is the click's own effect (a menu item closes its menu).</summary>
    static void OnTurnedOff(object? sender, EventArgs e) => _turnedOff |= !_clicked;
    static void OnClicked(object? sender, EventArgs e) => _clicked = true;

    /// <summary>The click fails: nothing more of this click is pressed.</summary>
    static void Fail()
    {
        AllHit = false;
        _down = [];
        while (Steps.TryPeek(out var next) && next.Phase is Phase.Press or Phase.Release) Steps.Dequeue();
    }

    public void FilterIsDown(InputButtonID id, InputButton btn, ref bool isDown) => isDown |= _down.Contains(id);
    public void FilterWasDown(InputButtonID id, InputButton btn, ref bool wasDown) => wasDown |= _wasDown.Contains(id);
    public void FilterValue(InputAxisID id, InputAxis axis, ref float value) { }
    public void FilterPrevValue(InputAxisID id, InputAxis axis, ref float prevValue) { }

    public void FilterValue(InputIntAxisID id, InputIntAxis axis, ref int value)
    {
        if (_cursor is { } c) value = id == InputIntAxisID.CursorXLocation ? c.X : c.Y;
    }

    public void FilterPrevValue(InputIntAxisID id, InputIntAxis axis, ref int prevValue)
    {
        if (_cursor is { } c) prevValue = id == InputIntAxisID.CursorXLocation ? c.X : c.Y;
    }
}

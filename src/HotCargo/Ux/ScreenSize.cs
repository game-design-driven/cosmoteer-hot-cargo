using Cosmoteer;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui;
using Halfling.Gui.Components.Hierarchy;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// Test aids for the readability pass (spec 1480, 1481): set the window size and the game's UI scale, and check that windows
/// and panels fit the screen. Headless slots use the null window. A windowed test slot runs minimized (UX.md, "Screenshots"),
/// so a resize of the hidden window does not reach the back buffer; this resizes the D3D11 swap chain directly.
/// </summary>
static class ScreenSize
{
    /// <summary>The size that the last "window" command set, so that onscreen sees a size that the game set back.</summary>
    static IntVector2? _wanted;

    /// <summary>Sets the render size. The game then applies its UI scale setting for that size (GameApp.OnWindowResized).</summary>
    internal static void SetWindow(IntVector2 size)
    {
        // No lower limit: the headless window starts at 800x600, and "matrix" sets the start size again at its end.
        if (size.X <= 0 || size.Y <= 0) throw new ArgumentException($"window {size}: needs a positive size");
        var window = App.Window;
        if (window is Halfling.Windows.Null.NullWindow)
        {
            // The headless window runs borderless, where Size cannot be set but reads WindowedSize (NullWindow.Size).
            window.WindowedSize = size;
            window.InvokeResizedOrModeChanged();
        }
        else if (window.GetType().FullName == "Halfling.Windows.D3D11.D3D11Window")
        {
            if (!window.IsMinimized) throw new InvalidOperationException("window: only for a minimized test slot window (scripts/screens.sh)");
            ResizeSwapChain(window, size);
            window.InvokeResizedOrModeChanged();
        }
        else throw new InvalidOperationException($"window: unknown window type {window.GetType().FullName}");
        if (window.Size != size) throw new InvalidOperationException($"window: size is {window.Size}, not {size}");
        _wanted = size;
        Log.Info($"ux: window {size}, UI scale {GuiRoot.GlobalUIScale}");
    }

    /// <summary>
    /// The minimized window has no client area, so D3D11Window keeps the back buffer size (its Size getter returns it) and its frame
    /// loop does not resize it. This mirrors that loop's resize (D3D11Window, "Resized swap chain") with an explicit size.
    /// The platform assembly is not referenced at build time, so every member is found by name. A missing member throws, so a game update
    /// that changes the window code fails the script instead of taking screenshots at the wrong size (SetWindow checks the size).
    /// </summary>
    static void ResizeSwapChain(Halfling.Windows.Window window, IntVector2 size)
    {
        var type = window.GetType();
        var swapChainProperty = AccessTools.Property(type, "SwapChain") ?? throw new MissingMemberException(type.FullName, "SwapChain");
        var backBufferProperty = AccessTools.Property(type, "BackBuffer") ?? throw new MissingMemberException(type.FullName, "BackBuffer");
        var sizeField = AccessTools.Field(type, "_curBackBufferSize") ?? throw new MissingMemberException(type.FullName, "_curBackBufferSize");
        object swapChain = swapChainProperty.GetValue(window) ?? throw new InvalidOperationException("window: no swap chain");
        var resize = swapChain.GetType().GetMethods().Single(m => m.Name == "ResizeBuffers" && m.GetParameters().Length == 5);
        var getBackBuffer = swapChain.GetType().GetMethods().Single(m => m.Name == "GetBackBuffer" && m.IsGenericMethodDefinition)
            .MakeGenericMethod(backBufferProperty.PropertyType);
        var p = resize.GetParameters();

        Invoke(window, "SwapChainResizing");
        ((IDisposable)(backBufferProperty.GetValue(window) ?? throw new InvalidOperationException("window: no back buffer"))).Dispose();
        // Buffer count 1 and flags 2 (AllowModeSwitch): the values of the vanilla resize in a window mode.
        resize.Invoke(swapChain, [1, size.X, size.Y, Enum.ToObject(p[3].ParameterType, 0), Enum.ToObject(p[4].ParameterType, 2)]);
        backBufferProperty.SetValue(window, getBackBuffer.Invoke(swapChain, [0]));
        sizeField.SetValue(window, size);
        Invoke(window, "SwapChainResized");
    }

    static void Invoke(object window, string eventName)
    {
        var field = AccessTools.Field(window.GetType(), eventName) ?? throw new MissingMemberException(window.GetType().FullName, eventName);
        ((Action?)field.GetValue(window))?.Invoke();
    }

    /// <summary>
    /// Sets the UI scale like the game's setting (Settings.ApplyDpiScaling), only to a step that the game allows for this size.
    /// It is not saved. The next "window" sets the game's own scale again.
    /// </summary>
    internal static void SetUiScale(float scale)
    {
        var mode = Steps.FirstOrDefault(m => m.Scale == scale).Mode
            ?? throw new ArgumentException($"uiscale {scale}: the game has {string.Join(", ", Steps.Select(m => m.Scale))}");
        if (!Settings.IsDpiScaleAllowedForResolution(mode, App.Window.Size))
            throw new ArgumentException($"uiscale {scale}: the game does not allow it at {App.Window.Size}");
        GuiRoot.GlobalUIScale = scale;
        Log.Info($"ux: UI scale {scale} at {App.Window.Size} (GUI space {(Vector2)App.Window.Size / scale})");
    }

    /// <summary>The game's UI scale steps, from the names of DpiScaleMode ("Dpi125" is 1.25, as in Settings.ApplyDpiScaling).</summary>
    static readonly (DpiScaleMode? Mode, float Scale)[] Steps =
        [.. Enum.GetValues<DpiScaleMode>().Select(m => ((DpiScaleMode?)m, int.Parse(m.ToString()["Dpi".Length..], System.Globalization.CultureInfo.InvariantCulture) / 100f))];

    /// <summary>The largest UI scale that the game allows for a size (its own cap, Settings.ApplyDpiScaling).</summary>
    internal static float MaxUiScale(IntVector2 size) => Steps.Last(m => Settings.IsDpiScaleAllowedForResolution(m.Mode!.Value, size)).Scale;

    /// <summary>
    /// The window sizes of the readability pass (spec 1480): the smallest window client area and a 1280x720 window (the frame takes
    /// some height), then common screens and an ultrawide. These are test choices, not game data.
    /// </summary>
    static readonly IntVector2[] MatrixSizes = [new(1008, 681), new(1264, 681), new(1280, 720), new(1920, 1080), new(2560, 1440), new(3440, 1440)];

    /// <summary>
    /// The script lines of "matrix": for each size, at the smallest and the largest UI scale that the game allows there,
    /// the open commands, an onscreen report, a screenshot (with render on) and the close commands. Then the first size again.
    /// </summary>
    static IEnumerable<string> MatrixLines(string label, string[] open, string[] close, bool screenshots)
    {
        var start = App.Window.Size;
        foreach (var size in MatrixSizes)
            foreach (float scale in new[] { Steps[0].Scale, MaxUiScale(size) }.Distinct())
            {
                string name = $"{label}-{size.X}x{size.Y}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                yield return $"window {size.X} {size.Y}";
                yield return $"uiscale {scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                foreach (var line in open) yield return line;
                yield return $"onscreen report {name}";
                if (screenshots) yield return $"screenshot {name}";
                foreach (var line in close) yield return line;
            }
        yield return $"window {start.X} {start.Y}";
    }

    /// <summary>
    /// Spec 1481: every active window box, and every panel placed straight on a GUI root, lies inside its screen.
    /// Returns the window boxes that it checked and the widgets that leave the screen. Widgets of zero size are skipped (hidden tween states).
    /// </summary>
    internal static (List<string> Windows, List<string> Bad) OffScreen()
    {
        if (_wanted is { } wanted && App.Window.Size != wanted)
            throw new InvalidOperationException($"onscreen: the window is {App.Window.Size}, but window set {wanted}; the game set the size back");
        var windows = new List<string>();
        var bad = new List<string>();
        // The game's own GUI (GameGui) is not an app state; it hangs off the GameRoot state.
        var roots = App.Director.States.OfType<IGuiRoot>().Concat(App.Director.States.OfType<Cosmoteer.Game.GameRoot>().Select(g => (IGuiRoot)g.Gui));
        foreach (var root in roots)
        {
            if (root.RootWidget is not Widget { IsActive: true } top) continue;
            var screen = top.ScreenRect;
            var checkedWidgets = top.GetChainedWidgets(recursive: false).Concat(top.GetChainedWidgets(recursive: true).OfType<WindowBox>());
            foreach (var w in checkedWidgets.Distinct())
            {
                if (!w.IsActive) continue;
                var r = w.ScreenRect;
                if (r.Width < 1f || r.Height < 1f) continue;
                if (w is WindowBox) windows.Add($"{root.GetType().Name}/{w.GetType().Name}");
                // Half a GUI unit of slack for rounding.
                if (r.X < screen.X - 0.5f || r.Y < screen.Y - 0.5f || r.Right > screen.Right + 0.5f || r.Bottom > screen.Bottom + 0.5f)
                    bad.Add($"{root.GetType().Name}/{w.GetType().Name} {r} (screen {screen})");
            }
        }
        return (windows, bad);
    }

    internal static void RegisterCommands()
    {
        // window W H: sets the render size (spec 1480). The game applies its UI scale setting for that size.
        Dev.Autotest.Commands["window"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("window needs: width height");
            SetWindow(new IntVector2(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
            Dev.Autotest.WaitReal(0.5);
        };
        // uiscale S|min|max: sets the UI scale (spec 1480). max is the largest step that the game allows at this size.
        Dev.Autotest.Commands["uiscale"] = arg =>
        {
            float scale = arg switch
            {
                "min" => Steps[0].Scale,
                "max" => MaxUiScale(App.Window.Size),
                _ => float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture),
            };
            SetUiScale(scale);
            Dev.Autotest.WaitReal(0.5);
        };
        // matrix LABEL [| OPEN [| CLOSE]]: runs OPEN, a fit report and a screenshot, then CLOSE, at each size and UI scale of the pass
        // (spec 1480). OPEN and CLOSE are script lines separated by ';'. Without OPEN, the surface stays open through the resizes.
        Dev.Autotest.Commands["matrix"] = arg =>
        {
            var parts = arg.Split('|');
            if (parts.Length > 3 || parts[0].Trim().Length == 0) throw new InvalidOperationException("matrix needs: LABEL [| OPEN [| CLOSE]]");
            string[] Lines(int i) => i < parts.Length ? [.. parts[i].Split(';').Select(l => l.Trim()).Where(l => l.Length > 0)] : [];
            Dev.Autotest.Inject(MatrixLines(parts[0].Trim(), Lines(1), Lines(2), Screenshot.ForceRender));
        };
        // onscreen LABEL: fails when a window or a panel leaves the screen (spec 1481). "onscreen report LABEL" only logs them.
        Dev.Autotest.Commands["onscreen"] = arg =>
        {
            bool report = arg.StartsWith("report ", StringComparison.Ordinal);
            string label = report ? arg["report ".Length..] : arg;
            var (windows, bad) = OffScreen();
            foreach (var b in bad) Log.Info($"ux: off screen [{label}] {b}");
            string what = $"fit [{label}] at {App.Window.Size} scale {GuiRoot.GlobalUIScale}: every window and panel on screen" +
                $" (windows: {(windows.Count == 0 ? "none" : string.Join(", ", windows))})";
            if (report) Log.Info($"ux: {what}: {(bad.Count == 0 ? "yes" : $"no, {bad.Count} off")}");
            else Dev.Autotest.Check(bad.Count == 0, what);
        };
    }
}

namespace HotCargo.Ux;

/// <summary>How the "window" test command resizes a window type (spec 1480).</summary>
enum ResizePath
{
    /// <summary>Set WindowedSize; the window takes the size at once (the null window, the Mac runner's MacWindow).</summary>
    WindowedSize,
    /// <summary>Resize the D3D11 swap chain of a minimized test slot window, which has no client area.</summary>
    SwapChain,
}

static class ScreenSizeModel
{
    /// <summary>The resize path for a window type, by its full name: the mod references neither the platform assembly nor the Mac host. Any other type throws.</summary>
    public static ResizePath ResizePathFor(string windowTypeFullName) => windowTypeFullName switch
    {
        "Halfling.Windows.Null.NullWindow" or "MacWindow" => ResizePath.WindowedSize,
        "Halfling.Windows.D3D11.D3D11Window" => ResizePath.SwapChain,
        _ => throw new InvalidOperationException($"window: unknown window type {windowTypeFullName}"),
    };
}

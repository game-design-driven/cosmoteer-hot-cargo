using HotCargo.Ux;
using Xunit;

namespace HotCargo.Tests;

public class ScreenSizeModelTests
{
    [Fact]
    public void The_window_command_resizes_the_null_and_the_mac_window_through_WindowedSize()
    {
        Assert.Equal(ResizePath.WindowedSize, ScreenSizeModel.ResizePathFor("Halfling.Windows.Null.NullWindow"));
        // Issue 304: the Mac runner (cosmoteer-macos) has a MacWindow in the global namespace.
        Assert.Equal(ResizePath.WindowedSize, ScreenSizeModel.ResizePathFor("MacWindow"));
        Assert.Equal(ResizePath.SwapChain, ScreenSizeModel.ResizePathFor("Halfling.Windows.D3D11.D3D11Window"));
    }

    [Fact]
    public void An_unknown_window_type_fails_the_command()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ScreenSizeModel.ResizePathFor("Some.OtherWindow"));
        Assert.Contains("Some.OtherWindow", ex.Message);
    }
}

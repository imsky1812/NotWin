using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Tests;

public sealed class DwmThumbnailTests
{
    [Fact]
    public void Register_BadHandles_ReturnsNull() => Assert.Null(DwmThumbnail.Register(0, 0));


    [Fact]
    public void NormalRect_NoWindow_IsNull() => Assert.Null(WindowListNative.NormalRect(0));


    // The genie's picture: a read-only grab of a small corner of the screen as a bitmap.
    [Fact]
    public void CaptureRegion_ReturnsABitmap()
    {
        var bmp = ScreenCaptureNative.CaptureRegion(new RectI(0, 0, 140, 70));
        Assert.NotEqual(0, bmp);
        ScreenCaptureNative.Delete(bmp);
        Assert.Equal(0, ScreenCaptureNative.CaptureRegion(new RectI(0, 0, 0, 0)));
    }
}

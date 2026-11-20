using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Tests;

public sealed class DwmThumbnailTests
{
    [Fact]
    public void Register_BadHandles_ReturnsNull() => Assert.Null(DwmThumbnail.Register(0, 0));

    // Read-only: grabs a 140 × 70 px corner of the screen as grey values, one per 14 px cell.
    [Fact]
    public void CaptureGray_SizeMatchesGrid()
    {
        var gray = ScreenCaptureNative.CaptureGray(new RectI(0, 0, 140, 70), 14);
        Assert.NotNull(gray);
        Assert.Equal(10 * 5, gray.Length);
    }

    [Fact]
    public void NormalRect_NoWindow_IsNull() => Assert.Null(WindowListNative.NormalRect(0));

    // Colour dots: one BGR triple per 14 px cell (read-only grab of a small corner of the screen).
    [Fact]
    public void CaptureColour_SizeMatchesGrid()
    {
        var bgr = ScreenCaptureNative.CaptureColour(new RectI(0, 0, 140, 70), 14);
        Assert.NotNull(bgr);
        Assert.Equal(10 * 5 * 3, bgr.Length);
    }
}

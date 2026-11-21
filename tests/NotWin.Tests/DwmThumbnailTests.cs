using NotWin.Core;
using NotWin.Native;

namespace NotWin.Tests;

public sealed class DwmThumbnailTests
{
    [Fact]
    public void Register_BadHandles_ReturnsNull() => Assert.Null(DwmThumbnail.Register(0, 0));



}

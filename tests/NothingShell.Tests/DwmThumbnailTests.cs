using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Tests;

public sealed class DwmThumbnailTests
{
    [Fact]
    public void Register_BadHandles_ReturnsNull() => Assert.Null(DwmThumbnail.Register(0, 0));



}

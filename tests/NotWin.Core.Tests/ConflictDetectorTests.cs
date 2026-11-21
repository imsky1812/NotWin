using NotWin.Core;

namespace NotWin.Core.Tests;

public sealed class ConflictDetectorTests
{
    [Fact]
    public void FindConflict_DetectsFenn() =>
        Assert.Equal("Fenn", ConflictDetector.FindConflict(["explorer", "FENN"]));

    [Fact]
    public void FindConflict_NoneWhenAbsent() =>
        Assert.Null(ConflictDetector.FindConflict(["explorer", "notepad"]));
}

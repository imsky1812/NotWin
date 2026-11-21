namespace NotWin.Core.Tests;

public sealed class DragArgsTests
{
    [Fact]
    public void Parse_TakesTheAbsolutePathsAfterDrag()
    {
        Assert.Equal([@"C:\a b\photo.jpg", @"\\server\share\doc.pdf"],
            DragArgs.Parse(["--drag", @"C:\a b\photo.jpg", @"\\server\share\doc.pdf"]));
    }

    public static TheoryData<string[]> Rejected => new()
    {
        Array.Empty<string>(),
        new[] { "--drag" },                              // nothing to drag
        new[] { "--open", @"C:\x.txt" },                 // not our verb
        new[] { "--drag", @"relative\x.txt" },           // never resolved against some working folder
        new[] { "--drag", @"C:x.txt" },                  // drive-relative
        new[] { "--drag", @"C:\ok.txt", "" },            // an empty path
        new[] { "--drag", "C:\\bad\u0000name.txt" },     // invalid characters
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Parse_AnythingElse_IsNull(string[] args) => Assert.Null(DragArgs.Parse(args));

    [Fact]
    public void CommandLine_QuotesEveryPath() =>
        Assert.Equal("--drag \"C:\\a b\\photo.jpg\" \"D:\\x.txt\"", DragArgs.CommandLine([@"C:\a b\photo.jpg", @"D:\x.txt"]));

    // A folder path ending in '\' would escape its closing quote ("C:\dir\" → C:\dir"): trailing backslashes are doubled.
    [Fact]
    public void CommandLine_FolderEndingInBackslash_KeepsItsQuote() =>
        Assert.Equal("--drag \"C:\\dir\\\\\" \"C:\\\\\"", DragArgs.CommandLine([@"C:\dir\", @"C:\"]));

    // Plan 8: the helper starts on the press (before the drag) and waits for the notch to let clicks through.
    [Fact]
    public void CommandLine_WithTheNotch_PutsOverFirst() =>
        Assert.Equal(@"--over 1a2b --token 99 --drag ""D:\x.txt""", DragArgs.CommandLine([@"D:\x.txt"], 0x1a2b, 99));

    [Fact]
    public void ParseRequest_ReadsOverAndPaths()
    {
        var r = DragArgs.ParseRequest(["--over", "1a2b", "--token", "99", "--drag", @"D:\x.txt"]);
        Assert.NotNull(r);
        Assert.Equal((nint)0x1a2b, r!.Over);
        Assert.Equal(99, r.Token);
        Assert.Equal([@"D:\x.txt"], r.Paths);
        Assert.Equal([@"D:\x.txt"], DragArgs.Parse(["--over", "1a2b", "--token", "99", "--drag", @"D:\x.txt"]));
        Assert.Equal((nint)0, DragArgs.ParseRequest(["--drag", @"D:\x.txt"])!.Over);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("0")]
    [InlineData("")]
    public void ParseRequest_BadOver_IsNull(string over) => Assert.Null(DragArgs.ParseRequest(["--over", over, "--token", "5", "--drag", @"D:\x.txt"]));

    // Review (Plan 8): the notch turns click-through on its own whenever it closes, so the helper waits for its own
    // press's token, never for the window style. A waiting helper without a token is refused.
    [Fact]
    public void ParseRequest_OverWithoutToken_IsNull() =>
        Assert.Null(DragArgs.ParseRequest(["--over", "1a2b", "--drag", @"D:\x.txt"]));

    [Theory]
    [InlineData("0")]
    [InlineData("x")]
    public void ParseRequest_BadToken_IsNull(string token) =>
        Assert.Null(DragArgs.ParseRequest(["--over", "1a2b", "--token", token, "--drag", @"D:\x.txt"]));

    [Theory]
    [InlineData(true, false, 100, DragGateStep.Wait)]
    [InlineData(true, true, 100, DragGateStep.Go)]
    [InlineData(false, false, 100, DragGateStep.Clicked)]   // let go without dragging: it was a click (copy)
    [InlineData(false, true, 100, DragGateStep.Clicked)]    // never send a press for a button that is up
    [InlineData(true, false, 30_000, DragGateStep.Wait)]      // holding still is fine (only the window going ends it)
    [InlineData(true, false, 600_000, DragGateStep.GiveUp)]
    public void DragGate_Steps(bool down, bool clickThrough, int ms, DragGateStep step) =>
        Assert.Equal(step, DragGate.Next(down, clickThrough, TimeSpan.FromMilliseconds(ms)));
}

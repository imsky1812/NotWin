namespace NothingShell.Core.Tests;

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
}

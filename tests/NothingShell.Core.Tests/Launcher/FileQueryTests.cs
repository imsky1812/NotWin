using NothingShell.Core.Launcher;

namespace NothingShell.Core.Tests.Launcher;

public sealed class FileQueryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_EmptyOrWhitespace_ReturnsNull(string q) => Assert.Null(FileQuery.Build(q));

    [Theory]
    [InlineData("o'brien", "o''brien")]
    [InlineData("50%", "50[%]")]
    [InlineData("a_b", "a[_]b")]
    [InlineData("[x]", "[[]x]")]
    public void Build_EscapesQuotesAndWildcards(string q, string escaped)
    {
        Assert.Equal(escaped, FileQuery.EscapeLiteral(q));
        Assert.Contains($"LIKE '%{escaped}%'", FileQuery.Build(q));
    }

    [Fact]
    public void Build_TruncatesLongQuery()
    {
        var sql = FileQuery.Build(new string('a', 200))!;
        Assert.Contains($"'%{new string('a', FileQuery.MaxQueryLength)}%'", sql);
        Assert.DoesNotContain(new string('a', FileQuery.MaxQueryLength + 1), sql);
        Assert.Equal(64, FileQuery.MaxQueryLength);
    }

    [Fact]
    public void Build_ContainsScopeAndExcludesLnk()
    {
        var sql = FileQuery.Build("report", top: 20)!;
        Assert.StartsWith("SELECT TOP 20 System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType FROM SystemIndex", sql);
        Assert.Contains("SCOPE='file:'", sql);
        Assert.Contains("System.ItemType <> '.lnk'", sql);
        Assert.EndsWith("ORDER BY System.DateModified DESC", sql);
    }

    [Fact]
    public void Build_TrimsQuery() => Assert.Contains("LIKE '%report%'", FileQuery.Build("  report  "));
}

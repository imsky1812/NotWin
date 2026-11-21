using NothingShell.Core.Launcher;

namespace NothingShell.Core.Tests.Launcher;

public sealed class FileSearchRulesTests
{
    private static LaunchItem F(string path) => new(path, Path.GetFileName(path), LaunchKind.File, path, []);

    [Fact]
    public void Merge_RecentFirstAndDeduped()
    {
        var recent = new[] { F(@"C:\a\one.txt") };
        var indexed = new[] { F(@"C:\b\two.txt"), F(@"c:\A\ONE.txt") };
        var r = FileSearchRules.Merge(recent, indexed, 10);
        Assert.Equal([@"C:\a\one.txt", @"C:\b\two.txt"], r.Select(i => i.Id));
    }

    [Fact]
    public void Merge_CapsAtMax()
    {
        var indexed = Enumerable.Range(0, 10).Select(i => F($@"C:\f{i}.txt")).ToList();
        Assert.Equal(3, FileSearchRules.Merge([], indexed, 3).Count);
    }
}

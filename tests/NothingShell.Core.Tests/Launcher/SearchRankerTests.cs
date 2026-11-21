using NothingShell.Core.Launcher;

namespace NothingShell.Core.Tests.Launcher;

public sealed class SearchRankerTests
{
    private static LaunchItem App(string name, params string[] kw) => new($"app:{name}", name, LaunchKind.App, $"shell:AppsFolder\\{name}", kw);
    private static LaunchItem Setting(string name, params string[] kw) => new($"ms-settings:{name}", name, LaunchKind.Setting, $"ms-settings:{name}", kw);
    private static LaunchItem File(string name) => new($@"C:\f\{name}", name, LaunchKind.File, $@"C:\f\{name}", []);
    private static LaunchItem Folder(string name) => new($@"C:\d\{name}", name, LaunchKind.Folder, $@"C:\d\{name}", []);
    private static readonly IReadOnlyDictionary<string, LaunchUsage> NoUsage = new Dictionary<string, LaunchUsage>();

    [Fact] public void Score_Exact_Is100() => Assert.Equal(100, SearchRanker.Score("notepad", App("Notepad")));
    [Fact] public void Score_Prefix_Is90() => Assert.Equal(90, SearchRanker.Score("note", App("Notepad")));
    [Fact] public void Score_WordStart_Is75() => Assert.Equal(75, SearchRanker.Score("code", App("Visual Studio Code")));
    [Fact] public void Score_WordStart_SplitsOnPunctuation() => Assert.Equal(75, SearchRanker.Score("term", App("Windows-Terminal")));
    [Fact] public void Score_Initials_Is65() => Assert.Equal(65, SearchRanker.Score("vsc", App("Visual Studio Code")));
    [Fact] public void Score_InitialsNeedTwoChars() => Assert.Equal(0, SearchRanker.Score("x", App("Visual Studio Code")));
    [Fact] public void Score_KeywordPrefix_Is60() => Assert.Equal(60, SearchRanker.Score("bright", Setting("Display", "brightness")));
    [Fact] public void Score_Substring_Is50() => Assert.Equal(50, SearchRanker.Score("pad", App("Notepad")));
    [Fact] public void Score_NoMatch_Is0() => Assert.Equal(0, SearchRanker.Score("zzz", App("Notepad")));

    [Fact]
    public void Score_IsCaseAndCultureInsensitive()
    {
        Assert.Equal(90, SearchRanker.Score("NOTE", App("Notepad")));
        _ = SearchRanker.Score("ı", App("Notepad")); // dotless i: must not throw
        Assert.Equal(100, SearchRanker.Score("cafe notes", App("Café Notes")));
        Assert.Equal(90, SearchRanker.Score("café", App("Cafe Notes")));
    }

    [Fact]
    public void Rank_OrdersAppsThenSettingsThenFiles()
    {
        var items = new[] { File("sound.txt"), Setting("Sound"), App("SoundHound") };
        var r = SearchRanker.Rank("sound", items, NoUsage);
        Assert.Equal([LaunchKind.App, LaunchKind.Setting, LaunchKind.File], r.Select(i => i.Kind));
    }

    [Fact]
    public void Rank_WithinKind_ScoreThenUsageThenName()
    {
        var a = App("Notepad++");
        var b = App("Notepad Next");
        var c = App("Notes");
        var usage = new Dictionary<string, LaunchUsage> { [b.Id] = new(b.Id, 5, DateTimeOffset.UnixEpoch) };
        var r = SearchRanker.Rank("no", [a, b, c], usage);
        Assert.Equal(["Notepad Next", "Notepad++", "Notes"], r.Select(i => i.Name));
    }

    [Fact]
    public void Rank_HigherScoreBeatsUsage()
    {
        var exact = App("Mail");
        var prefix = App("Mailbird");
        var usage = new Dictionary<string, LaunchUsage> { [prefix.Id] = new(prefix.Id, 50, DateTimeOffset.UnixEpoch) };
        Assert.Equal("Mail", SearchRanker.Rank("mail", [prefix, exact], usage)[0].Name);
    }

    [Fact]
    public void Rank_CapsAtMax()
    {
        var items = Enumerable.Range(0, 20).Select(i => App($"App {i}"));
        Assert.Equal(SearchRanker.MaxResults, SearchRanker.Rank("app", items, NoUsage).Count);
        Assert.Equal(6, SearchRanker.MaxResults);
    }

    [Fact]
    public void Rank_WhitespaceQuery_ReturnsEmpty() => Assert.Empty(SearchRanker.Rank("   ", [App("Notepad")], NoUsage));

    [Fact]
    public void Rank_FoldersRankWithFiles()
    {
        var r = SearchRanker.Rank("report", [File("old report.txt"), Folder("Reports")], NoUsage);
        Assert.Equal(["Reports", "old report.txt"], r.Select(i => i.Name));
    }

    [Fact]
    public void Rank_SymbolsDoNotThrow()
    {
        var r = SearchRanker.Rank("o'brien % _ [x]", [App("Notepad")], NoUsage);
        Assert.Empty(r);
    }
}

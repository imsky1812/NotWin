using NotWin.Core.Launcher;

namespace NotWin.Core.Tests.Launcher;

public sealed class UserAssistParserTests
{
    [Fact]
    public void Rot13_RoundTrips()
    {
        const string encoded = @"{1NP14R77-02R7-4R5Q-O744-2RO1NR5198O7}\abgrcnq.rkr";
        const string decoded = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe";
        Assert.Equal(decoded, UserAssistParser.Rot13(encoded));
        Assert.Equal(encoded, UserAssistParser.Rot13(decoded));
    }

    [Fact]
    public void Parse_ReadsCountAndFiletime()
    {
        var data = new byte[72];
        BitConverter.GetBytes(7).CopyTo(data, 4);
        BitConverter.GetBytes(133000000000000000L).CopyTo(data, 60);
        var e = UserAssistParser.Parse(UserAssistParser.Rot13("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"), data);
        Assert.NotNull(e);
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", e.Value.Name);
        Assert.Equal(7, e.Value.RunCount);
        Assert.Equal(DateTimeOffset.FromFileTime(133000000000000000L), e.Value.LastRun);
    }

    [Fact]
    public void Parse_ZeroFiletime_GivesNullLastRun()
    {
        var e = UserAssistParser.Parse("k", new byte[72]);
        Assert.NotNull(e);
        Assert.Null(e.Value.LastRun);
    }

    [Fact]
    public void Parse_ShortData_ReturnsNull() => Assert.Null(UserAssistParser.Parse("k", new byte[67]));
}

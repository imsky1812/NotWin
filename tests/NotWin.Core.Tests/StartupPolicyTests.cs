using NotWin.Core;

namespace NotWin.Core.Tests;

public sealed class StartupPolicyTests
{
    private const string Install = @"C:\Program Files\NotWin";

    [Fact]
    public void InstalledCopy_MayWrite() =>
        Assert.True(StartupPolicy.ShouldWriteRunValue(@"C:\Program Files\NotWin\NotWin.exe", Install));

    [Fact]
    public void CaseAndTrailingSlash_Ignored() =>
        Assert.True(StartupPolicy.ShouldWriteRunValue(@"c:\program files\notwin\NotWin.exe", Install + @"\"));

    [Theory]
    [InlineData(@"D:\NothingLauncher\src\NotWin\bin\Debug\net10.0-windows10.0.22621.0\NotWin.exe")]
    [InlineData(@"C:\Program Files\NotWin\sub\NotWin.exe")]
    [InlineData(@"C:\Program Files\NotWin2\NotWin.exe")]
    [InlineData("")]
    public void OtherPaths_MayNotWrite(string exe) => Assert.False(StartupPolicy.ShouldWriteRunValue(exe, Install));

    [Fact]
    public void RunValuePointsAt_MatchesQuotedSelfOnly()
    {
        Assert.True(StartupPolicy.RunValuePointsAt("\"D:\\x\\a.exe\"", @"d:\x\A.exe"));
        Assert.False(StartupPolicy.RunValuePointsAt("\"C:\\Program Files\\NotWin\\NotWin.exe\"", @"D:\x\a.exe"));
        Assert.False(StartupPolicy.RunValuePointsAt(null, @"D:\x\a.exe"));
    }
}

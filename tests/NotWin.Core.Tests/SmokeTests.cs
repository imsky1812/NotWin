namespace NotWin.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.NotNull(typeof(NotWin.Core.RectI).Assembly);
    }
}

namespace NothingShell.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.NotNull(typeof(NothingShell.Core.RectI).Assembly);
    }
}

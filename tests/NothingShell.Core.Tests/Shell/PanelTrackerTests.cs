using NothingShell.Core.Shell;

namespace NothingShell.Core.Tests.Shell;

public sealed class PanelTrackerTests
{
    [Fact]
    public void NoneOpen_ByDefault() => Assert.False(new PanelTracker().AnyOpen);

    [Fact]
    public void OneSource_OpenThenClosed()
    {
        var t = new PanelTracker();
        t.Set("panel", true);
        Assert.True(t.AnyOpen);
        t.Set("panel", false);
        Assert.False(t.AnyOpen);
    }

    [Fact]
    public void TwoSources_ClosingOneKeepsOpen()
    {
        var t = new PanelTracker();
        t.Set("popup", true);
        t.Set("panel", true);
        t.Set("popup", false);
        Assert.True(t.AnyOpen);
    }

    [Fact]
    public void SameSourceTwice_IsIdempotent()
    {
        var t = new PanelTracker();
        t.Set("panel", true);
        t.Set("panel", true);
        t.Set("panel", false);
        Assert.False(t.AnyOpen);
    }
}

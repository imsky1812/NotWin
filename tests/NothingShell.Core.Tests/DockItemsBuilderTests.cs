using NothingShell.Core;
using Xunit;

namespace NothingShell.Core.Tests;

public class DockItemsBuilderTests
{
    private static AppWindow W(nint h, string appId, string title = "t", bool fg = false) =>
        new(h, appId, title, @"c:\apps\" + appId + ".exe", fg);

    private static PinnedApp P(string id) => new(id, id.ToUpperInvariant(), @"c:\apps\" + id + ".exe");

    [Fact]
    public void Build_GroupsWindowsByAppId()
    {
        var items = DockItemsBuilder.Build([], [W(1, "a"), W(2, "a")]);
        var item = Assert.Single(items);
        Assert.Equal("a", item.AppId);
        Assert.Equal(2, item.Windows.Count);
        Assert.True(item.Running);
        Assert.False(item.Pinned);
    }

    [Fact]
    public void Build_PinnedFirstInPinnedOrder()
    {
        var items = DockItemsBuilder.Build([P("b"), P("a")], [W(1, "a"), W(2, "b")]);
        Assert.Equal(["b", "a"], items.Select(i => i.AppId));
        Assert.All(items, i => Assert.True(i.Pinned));
        Assert.Equal("B", items[0].DisplayName);
        Assert.Equal(@"c:\apps\b.exe", items[0].LaunchPath);
    }

    [Fact]
    public void Build_PinnedNotRunning_HasNoWindows()
    {
        var items = DockItemsBuilder.Build([P("a")], []);
        var item = Assert.Single(items);
        Assert.False(item.Running);
        Assert.False(item.Focused);
    }

    [Fact]
    public void Build_RunningUnpinnedAfterPinned_InFirstSeenOrder()
    {
        var items = DockItemsBuilder.Build([P("p")], [W(1, "z"), W(2, "y"), W(3, "p"), W(4, "z")]);
        Assert.Equal(["p", "z", "y"], items.Select(i => i.AppId));
        Assert.Equal(2, items[1].Windows.Count);
        Assert.False(items[1].Pinned);
    }

    [Fact]
    public void Build_SkipsUntitledWindows()
    {
        var items = DockItemsBuilder.Build([], [W(1, "a", ""), W(2, "b", "ok")]);
        Assert.Equal(["b"], items.Select(i => i.AppId));
    }

    [Fact]
    public void Build_AppIdMatchIsCaseInsensitive()
    {
        var items = DockItemsBuilder.Build([P("a")], [W(1, "A")]);
        var item = Assert.Single(items);
        Assert.True(item.Pinned);
        Assert.Single(item.Windows);
    }

    [Fact]
    public void Focused_WhenAnyWindowForeground()
    {
        var item = Assert.Single(DockItemsBuilder.Build([], [W(1, "a"), W(2, "a", fg: true)]));
        Assert.True(item.Focused);
    }

    [Fact]
    public void DecideClick_NotRunning_Launch() =>
        Assert.Equal(DockClickAction.Launch, DockItemsBuilder.DecideClick(Item()));

    [Fact]
    public void DecideClick_SingleFocused_Minimize() =>
        Assert.Equal(DockClickAction.Minimize, DockItemsBuilder.DecideClick(Item(W(1, "a", fg: true))));

    [Fact]
    public void DecideClick_SingleUnfocused_Activate() =>
        Assert.Equal(DockClickAction.Activate, DockItemsBuilder.DecideClick(Item(W(1, "a"))));

    [Fact]
    public void DecideClick_Multiple_ShowWindowList() =>
        Assert.Equal(DockClickAction.ShowWindowList, DockItemsBuilder.DecideClick(Item(W(1, "a", fg: true), W(2, "a"))));

    private static DockItem Item(params AppWindow[] ws) => new("a", "A", null, false, ws);
}

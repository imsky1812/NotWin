using NotWin.Core.Panel;

namespace NotWin.Core.Tests.Panel;

public sealed class NotificationGroupsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static NotificationItem N(uint id, string app, string text, int min) => new(id, app, app.ToUpperInvariant(), text, T0.AddMinutes(min));

    [Fact]
    public void Build_GroupsByApp_CountsAndLatest()
    {
        var g = NotificationGroups.Build([N(1, "chat", "a", 0), N(2, "chat", "b", 1), N(3, "mail", "m", 0)]);
        var chat = Assert.Single(g, x => x.AppId == "chat");
        Assert.Equal(2, chat.Count);
        Assert.Equal([1u, 2u], chat.Ids.OrderBy(i => i));
        Assert.Equal("CHAT", chat.AppName);
    }

    [Fact]
    public void Build_OrdersGroupsNewestFirst()
    {
        var g = NotificationGroups.Build([N(1, "chat", "a", 0), N(2, "mail", "m", 5), N(3, "chat", "b", 3)]);
        Assert.Equal(["mail", "chat"], g.Select(x => x.AppId));
    }

    [Fact]
    public void Build_LatestTextIsNewestItemsText()
    {
        var g = NotificationGroups.Build([N(2, "chat", "newer", 2), N(1, "chat", "older", 1)]);
        Assert.Equal("newer", g[0].LatestText);
        Assert.Equal(T0.AddMinutes(2), g[0].LatestTime);
    }

    [Fact]
    public void Build_Empty_ReturnsEmpty() => Assert.Empty(NotificationGroups.Build([]));

    [Fact]
    public void Build_AppIdsAreCaseInsensitive()
    {
        var g = NotificationGroups.Build([N(1, "Chat", "a", 0), N(2, "chat", "b", 1)]);
        Assert.Single(g);
    }
}

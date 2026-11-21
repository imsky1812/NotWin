using NotWin.Notifications;

namespace NotWin.Tests;

public sealed class NotificationServiceLocalTests
{
    [Fact]
    public async Task AddLocal_ListedEvenWithoutListener_AndRemovable()
    {
        using var service = new NotificationService(); // never started: no listener, not Available
        var changed = 0;
        service.ListChanged += () => changed++;

        var item = service.AddLocal("Timer", "Time is up");
        Assert.Equal("Timer", item.AppName);
        Assert.Equal("", item.AppId);
        Assert.True(item.Id >= 0xFFFF0000u);
        Assert.Equal([item], await service.GetAllAsync());

        var second = service.AddLocal("Timer", "Time is up");
        Assert.NotEqual(item.Id, second.Id);
        service.Remove([item.Id]);
        Assert.Equal([second], await service.GetAllAsync());

        service.ClearAll();
        Assert.Empty(await service.GetAllAsync());
        Assert.True(changed >= 3);
    }
}

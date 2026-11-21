using NotWin.Core;
using Xunit;

namespace NotWin.Core.Tests;

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

    [Fact]
    public void Build_PinWithoutMatchingAppId_FallsBackToTargetExe_ForAumidLessWindow()
    {
        var pin = new PinnedApp("Chrome", "Chrome", @"c:\x.lnk", @"c:\apps\chrome.exe");
        var items = DockItemsBuilder.Build([pin], [new AppWindow(1, @"c:\apps\chrome.exe", "t", @"c:\apps\chrome.exe", false)]);
        var item = Assert.Single(items);
        Assert.True(item.Pinned);
        Assert.True(item.Running);
        Assert.Equal("Chrome", item.AppId);
    }

    [Fact]
    public void Build_ExeFallback_DoesNotAbsorbWindowWithOwnAumid()
    {
        var pin = new PinnedApp("Chrome", "Chrome", @"c:\x.lnk", @"c:\apps\chrome.exe");
        var items = DockItemsBuilder.Build([pin], [new AppWindow(1, "Chrome.PWA.xyz", "t", @"c:\apps\chrome.exe", false)]);
        Assert.Equal(2, items.Count);
        Assert.True(items[0].Pinned);
        Assert.False(items[0].Running);
        Assert.Equal("Chrome.PWA.xyz", items[1].AppId);
        Assert.False(items[1].Pinned);
    }

    [Fact]
    public void Build_ExactAppIdBeatsExeFallback()
    {
        var pinA = new PinnedApp("a", "A", "a.lnk", @"c:\apps\shared.exe");
        var pinB = new PinnedApp("b", "B", "b.lnk", @"c:\apps\shared.exe");
        var items = DockItemsBuilder.Build([pinA, pinB],
            [new AppWindow(1, "b", "t", @"c:\apps\shared.exe", false)]);
        Assert.False(items[0].Running);
        Assert.True(items[1].Running);
    }

    // User report 2026-11-21: two Spotify icons. The pin was made from the 1.309 package folder; Spotify updated itself
    // and now runs from the 1.310 folder. Same package family and exe: the same app.
    [Fact]
    public void Build_StoreAppPinMatchesTheUpdatedVersion()
    {
        const string old = @"c:\program files\windowsapps\spotifyab.spotifymusic_1.309.2055.0_x64__zpdnekdrzrea0\spotify.exe";
        const string current = @"c:\program files\windowsapps\spotifyab.spotifymusic_1.310.2586.0_x64__zpdnekdrzrea0\spotify.exe";
        var pin = new PinnedApp(old, "Spotify", @"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", old);
        var items = DockItemsBuilder.Build([pin], [new AppWindow(1, current, "Spotify Premium", current, true)]);
        var only = Assert.Single(items);
        Assert.True(only.Pinned);
        Assert.True(only.Running);
        Assert.Equal(1, only.Windows[0].Handle);
    }

    [Fact]
    public void Build_DifferentStoreAppsStayApart()
    {
        const string a = @"c:\program files\windowsapps\one.app_1.0.0.0_x64__pub1\app.exe";
        const string b = @"c:\program files\windowsapps\two.app_1.0.0.0_x64__pub1\app.exe";
        var items = DockItemsBuilder.Build([new PinnedApp(a, "One", "x", a)], [new AppWindow(1, b, "Two", b, false)]);
        Assert.Equal(2, items.Count);
        Assert.False(items[0].Running);
    }

    [Theory]
    [InlineData(@"c:\program files\windowsapps\spotifyab.spotifymusic_1.309.2055.0_x64__zpdnekdrzrea0\spotify.exe", @"package:spotifyab.spotifymusic_zpdnekdrzrea0\spotify.exe")]
    [InlineData(@"c:\apps\chrome.exe", @"c:\apps\chrome.exe")]
    [InlineData("Chrome", "Chrome")]
    public void MatchKey_DropsTheVersionFolder(string id, string key) => Assert.Equal(key, DockItemsBuilder.MatchKey(id));

    // Review (Plan 6b) #3: pin / unpin treat two versions of one Store app as the same app.
    [Fact]
    public void SameApp_AcrossStoreVersions()
    {
        Assert.True(DockItemsBuilder.SameApp(
            @"c:\program files\windowsapps\spotifyab.spotifymusic_1.309.2055.0_x64__zpdnekdrzrea0\spotify.exe",
            @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.310.2586.0_x64__zpdnekdrzrea0\Spotify.exe"));
        Assert.True(DockItemsBuilder.SameApp("Chrome", "chrome"));
        Assert.False(DockItemsBuilder.SameApp(@"c:\a\one.exe", @"c:\a\two.exe"));
    }
}

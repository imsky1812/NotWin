using System.Windows;
using System.Windows.Controls;
using NotWin.Core.Launcher;
using NotWin.Dock;
using NotWin.Widgets;
using WpfPanel = System.Windows.Controls.Panel;

namespace NotWin.Tests;

public sealed class FolderWidgetTests
{
    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException("on STA: " + error);
    }

    private static IReadOnlyList<LaunchItem> Apps(int n) =>
        Enumerable.Range(0, n).Select(i => new LaunchItem($"app{i}", $"App {i}", LaunchKind.App, $"shell:AppsFolder\\app{i}", [])).ToList();

    private static StackPanel Build(int n) =>
        FolderWidget.BuildPopupContent(Apps(n), new AppIconCache((_, _) => null, () => 0), _ => { }, _ => { }, () => { });

    private static List<FrameworkElement> Tiles(StackPanel rows) =>
        rows.Children.OfType<WpfPanel>().SelectMany(r => r.Children.OfType<FrameworkElement>()).ToList();

    [Fact]
    public void Popup_HasOneTilePerApp_PlusAdd() => OnSta(() =>
    {
        var tiles = Tiles(Build(3));
        Assert.Equal(4, tiles.Count);
        Assert.Equal("add", tiles[^1].Tag);
        Assert.All(tiles.Take(3), t => Assert.IsType<LaunchItem>(t.Tag));
    });

    [Fact]
    public void Popup_WrapsAfterEight() => OnSta(() =>
    {
        var rows = Build(10);
        Assert.Equal(2, rows.Children.Count);
        Assert.Equal(8, ((WpfPanel)rows.Children[0]).Children.Count);
        Assert.Equal(3, ((WpfPanel)rows.Children[1]).Children.Count); // 2 apps + ＋
    });

    [Fact]
    public void Popup_EmptyFolder_OnlyAdd() => OnSta(() =>
    {
        var tiles = Tiles(Build(0));
        Assert.Equal("add", Assert.Single(tiles).Tag);
    });

    // Typing in the chooser must stay cheap: at most AppChooser.MaxRows rows (each loads an icon on the UI thread).
    [Fact]
    public void Chooser_ShowsAtMostMaxRows_AndFilters()
    {
        var apps = Enumerable.Range(0, 300).Select(i => new LaunchItem($"a{i}", i % 2 == 0 ? $"Note {i}" : $"Game {i}", LaunchKind.App, "x", [])).ToList();
        Assert.Equal(AppChooser.MaxRows, AppChooser.Matches(apps, "").Count);
        Assert.True(AppChooser.MaxRows <= 40);
        Assert.All(AppChooser.Matches(apps, "game"), a => Assert.StartsWith("Game", a.Name));
        Assert.Single(AppChooser.Matches(apps, "Note 10"), a => a.Name == "Note 10");
    }
}

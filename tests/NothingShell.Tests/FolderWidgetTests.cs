using System.Windows;
using System.Windows.Controls;
using NothingShell.Core.Launcher;
using NothingShell.Dock;
using NothingShell.Widgets;
using WpfPanel = System.Windows.Controls.Panel;

namespace NothingShell.Tests;

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
}

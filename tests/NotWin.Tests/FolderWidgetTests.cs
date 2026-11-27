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

    // Folder icon "Peek + name" (user 2026-11-27): the apps inside as mono icons, a name and a count.
    private static FolderWidget Folder(int apps, string? title = null)
    {
        var items = Apps(apps);
        var config = new NotWin.Core.Widgets.WidgetConfig(Apps: items.Select(a => a.Id).ToList(), Title: title);
        var w = new FolderWidget("f1", config, () => items, id => items.FirstOrDefault(a => a.Id == id),
            new AppIconCache((_, _) => null, () => 0), _ => { });
        w.Configure(config);
        return w;
    }

    private static List<string> Texts(FrameworkElement e) => ClockWidgetsTests.Tree(ClockWidgetsTests.Laid(e, new(1, 1))).OfType<TextBlock>().Select(t => t.Text).ToList();

    [Fact]
    public void Face_Small_ShowsNameCountAndPlusN() => OnSta(() =>
    {
        var face = Folder(5, "work").Build(new NotWin.Core.Widgets.WidgetSize(1, 1));
        var texts = Texts(face);
        Assert.Contains("WORK", texts);
        Assert.Contains("5 APPS", texts);
        Assert.Contains("+3", texts); // two icons, then "+3"
        Assert.Equal(2, ClockWidgetsTests.Tree(face).OfType<Border>().Count(b => b.Tag is "app"));
    });

    [Fact]
    public void Face_Empty_InvitesToAdd() => OnSta(() =>
    {
        var texts = Texts(Folder(0).Build(new NotWin.Core.Widgets.WidgetSize(1, 1)));
        Assert.Contains("FOLDER", texts);
        Assert.Contains("ADD APPS", texts);
    });

    [Fact]
    public void Rename_SavesTheTitle_AndRedrawsTheFace() => OnSta(() =>
    {
        NotWin.Core.Widgets.WidgetConfig? saved = null;
        var items = Apps(2);
        var config = new NotWin.Core.Widgets.WidgetConfig(Apps: items.Select(a => a.Id).ToList());
        var w = new FolderWidget("f1", config, () => items, id => items.FirstOrDefault(a => a.Id == id),
            new AppIconCache((_, _) => null, () => 0), c => saved = c);
        var redraws = 0;
        w.CardChanged += () => redraws++;
        w.Rename("  Games ");
        Assert.Equal("Games", saved?.Title);
        Assert.Equal(1, redraws);
        w.Rename("   ");
        Assert.Null(saved?.Title); // cleared: shows FOLDER again
    });

    [Fact]
    public void Mono_KeepsAlpha_DropsColour() => OnSta(() =>
    {
        var px = new byte[] { 0, 0, 255, 128 }; // one half-transparent red pixel (BGRA)
        var red = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, 4);
        var mono = (System.Windows.Media.Imaging.BitmapSource)MonoIcon.From(red)!;
        var outPx = new byte[4];
        new System.Windows.Media.Imaging.FormatConvertedBitmap(mono, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(outPx, 4, 0);
        Assert.Equal(outPx[0], outPx[1]);
        Assert.Equal(outPx[1], outPx[2]);
        Assert.Equal(128, outPx[3]);
    });

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

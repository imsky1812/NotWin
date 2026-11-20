using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NothingShell.Core.Widgets;
using NothingShell.Widgets;

namespace NothingShell.Tests;

public sealed class EditChromeTests
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

    // Fully transparent pixels of a per-pixel-transparent window pass clicks through, so the bare clock could only be
    // grabbed on a lit dot. The grab layer must have a (nearly invisible) non-zero alpha everywhere.
    [Fact]
    public void GrabLayer_IsHitTestableOnEveryPixel() => OnSta(() =>
    {
        var chrome = new EditChrome(new Grid(), new WidgetPlacement(WidgetKind.Clock, new(2, 2), 0, 0), false, 0.65);
        var brush = Assert.IsType<SolidColorBrush>(chrome.Grab.Background);
        Assert.True(brush.Color.A > 0);
    });

    [Fact]
    public void EveryWidget_HasACloseButton_TopLeft() => OnSta(() =>
    {
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var p = new WidgetPlacement(kind, WidgetRules.Sizes(kind)[0], 0, 0, "x");
            var chrome = new EditChrome(new Grid(), p, false, 0.65);
            Assert.Equal(HorizontalAlignment.Left, chrome.Close.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, chrome.Close.VerticalAlignment);
        }
    });

    [Fact]
    public void PhotoOnly_HasAShapeChip() => OnSta(() =>
    {
        var photo = new EditChrome(new Grid(), new WidgetPlacement(WidgetKind.Photo, new(2, 2), 0, 0, "p"), false, 0.65);
        Assert.Equal("■ ▸", ((TextBlock)photo.ShapeChip!.Child).Text);
        var circle = new EditChrome(new Grid(), new WidgetPlacement(WidgetKind.Photo, new(2, 2), 0, 0, "p", new WidgetConfig("circle")), false, 0.65);
        Assert.Equal("● ▸", ((TextBlock)circle.ShapeChip!.Child).Text);
        var clock = new EditChrome(new Grid(), new WidgetPlacement(WidgetKind.Clock, new(3, 1), 0, 0, "c"), false, 0.65);
        Assert.Null(clock.ShapeChip);
    });
}

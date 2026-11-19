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
}

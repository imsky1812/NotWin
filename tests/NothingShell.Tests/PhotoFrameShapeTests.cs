using System.IO;
using System.Windows;
using System.Windows.Controls;
using NothingShell.Core.Widgets;
using NothingShell.Widgets;

namespace NothingShell.Tests;

public sealed class PhotoFrameShapeTests
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

    private static PhotoFrameWidget Frame(string? shape) =>
        new("t1", new WidgetConfig(shape), new PhotoStore(Path.Combine(Path.GetTempPath(), "ns-frames-" + Guid.NewGuid().ToString("N"))), _ => { });

    // User, 2026-11-20: in circle mode the frame itself is round too, not a round photo on a square card.
    [Fact]
    public void Circle_DrawsItsOwnRoundCard() => OnSta(() =>
    {
        var frame = Frame("circle");
        frame.Configure(new WidgetConfig("circle"));
        var built = frame.Build(new WidgetSize(2, 2));
        Assert.True(((IDesktopWidget)frame).Bare); // no square WidgetCard around it
        var card = Assert.IsType<Border>(built);
        Assert.Equal(WidgetSize.Px(2) / 2.0, card.CornerRadius.TopLeft);
        Assert.Same(WidgetCard.CardBrush, card.Background);
    });

    [Fact]
    public void Square_UsesTheNormalCard() => OnSta(() =>
    {
        var frame = Frame(null);
        frame.Configure(new WidgetConfig());
        frame.Build(new WidgetSize(2, 2));
        Assert.False(((IDesktopWidget)frame).Bare);
    });
}

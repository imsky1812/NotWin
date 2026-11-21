using NothingShell.Core;
using NothingShell.Widgets;

namespace NothingShell.Tests;

// Review minor (Plan 5): a second drag within the 180 ms slide left the first widget mid-way. A slide that is cut short
// now ends at its destination.
public sealed class WidgetSlideTests
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

    [Fact]
    public void Finish_PlacesTheWidgetAtItsDestination() => OnSta(() =>
    {
        var placed = new List<RectI>();
        var slide = new WidgetSlide();
        var to = new RectI(300, 300, 400, 400);
        slide.Start(placed.Add, new RectI(0, 0, 100, 100), to);
        slide.Finish();
        Assert.Equal(to, placed[^1]);
        Assert.False(slide.Running);
    });

    [Fact]
    public void StartingAnother_FinishesTheFirst() => OnSta(() =>
    {
        var first = new List<RectI>();
        var slide = new WidgetSlide();
        var to = new RectI(300, 300, 400, 400);
        slide.Start(first.Add, new RectI(0, 0, 100, 100), to);
        slide.Start(_ => { }, new RectI(0, 0, 10, 10), new RectI(5, 5, 15, 15));
        Assert.Equal(to, first[^1]);
    });

    [Fact]
    public void Finish_WithNothingRunning_DoesNothing() => OnSta(() => new WidgetSlide().Finish());
}

using System.Windows.Controls;
using System.Windows.Input;
using NotWin.Notch;

namespace NotWin.Tests;

// Plan 10: the notch's media tile gets the widget's draggable dots (one shared helper).
public sealed class SeekBarTests
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
    public void Wrap_KnownLength_IsADraggableStrip() => OnSta(() =>
    {
        var dots = new DotBar { Count = 20 };
        var strip = SeekBar.Wrap(dots, 20, TimeSpan.FromMinutes(3), _ => Task.FromResult(true), _ => { });
        var border = Assert.IsType<Border>(strip);
        Assert.Same(dots, border.Child);
        Assert.Equal(Cursors.Hand, border.Cursor);
    });

    [Fact]
    public void Wrap_UnknownLength_IsJustTheDots() => OnSta(() =>
    {
        var dots = new DotBar { Count = 20 };
        Assert.Same(dots, SeekBar.Wrap(dots, 20, TimeSpan.Zero, _ => Task.FromResult(true), _ => { }));
    });
}

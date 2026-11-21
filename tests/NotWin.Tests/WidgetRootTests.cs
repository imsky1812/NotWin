using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Core.Widgets;
using NotWin.Widgets;

namespace NotWin.Tests;

public sealed class WidgetRootTests
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

    // Live bug 2026-11-19: a rebuild (edit mode, size chip, S/M/L) put the new card into a throwaway root first, then
    // into the widget's existing root -> "already the logical child of another element"; edit mode never appeared
    // and the clock's window was left empty.
    [Fact]
    public void Fill_ReusesTheExistingRoot_ForEveryRebuild() => OnSta(() =>
    {
        var first = new Border();
        var root = WidgetRoots.Fill(null, first, new WidgetSize(2, 2), 0.65);
        Assert.Same(first, Assert.Single(root.Children.OfType<Border>()));

        var second = new Border();
        var again = WidgetRoots.Fill(root, second, new WidgetSize(4, 2), 0.8);
        Assert.Same(root, again);
        Assert.Same(second, Assert.Single(again.Children.OfType<Border>()));
        Assert.Equal(WidgetSize.Px(4), again.Width);
        Assert.Equal(0.8, ((ScaleTransform)again.LayoutTransform).ScaleX);
    });
}

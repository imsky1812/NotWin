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

    // User report 2026-11-21: ＋ in the photo popup made the file picker and the popup vanish. The picker opened on top
    // of the popup, the popup closed when the picker took focus and pulled the picker down with it. Now the popup is
    // gone before the picker opens.
    [Fact]
    public void AddTile_ClosesThePopupBeforeThePickerOpens() => OnSta(() =>
    {
        var store = new PhotoStore(Path.Combine(Path.GetTempPath(), "ns-frames-" + Guid.NewGuid().ToString("N")));
        bool? popupVisibleAtAdd = null;
        PhotoManagePopup? popup = null;
        popup = PhotoManagePopup.Show(store, "f", [], new NothingShell.Core.RectI(0, 0, 100, 100), _ => { }, () => popupVisibleAtAdd = popup!.IsVisible);
        popup.RequestAdd();
        Assert.Null(popupVisibleAtAdd); // not synchronously inside the click
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.False(popupVisibleAtAdd);
    });

    // Plan 6b: "Up next" without a Google sign-in says where to connect, at every size.
    [Fact]
    public void UpNext_NotSignedIn_SaysWhereToConnect() => OnSta(() =>
    {
        foreach (var size in WidgetRules.Sizes(WidgetKind.Agenda))
        {
            using var w = new AgendaWidget(null);
            var built = w.Build(size);
            var texts = new List<string>();
            void Walk(DependencyObject d)
            {
                if (d is TextBlock t) texts.Add(t.Text);
                foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(child);
            }
            Walk(built);
            Assert.Contains("CONNECT GOOGLE CALENDAR IN SETTINGS", texts);
        }
    });
}

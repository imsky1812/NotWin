using NothingShell.Core;
using NothingShell.Dock;

namespace NothingShell.Tests;

public sealed class WindowPreviewCardTests
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

    private static List<AppWindow> Windows(int n) =>
        Enumerable.Range(1, n).Select(i => new AppWindow(i, "app", $"Window {i}", @"C:\app.exe", false)).ToList();

    [Fact]
    public void Card_OneCellPerWindow_UpToSixVisible() => OnSta(() =>
    {
        var card = new WindowPreviewCard();
        card.Build(Windows(3));
        Assert.Equal(3, card.Cells.Count);
        card.Build(Windows(8));
        Assert.Equal(8, card.Cells.Count);
        Assert.Equal(6 * WindowPreviewCard.CellWidth + 5 * WindowPreviewCard.CellGap, card.RowMaxWidth, 6); // then it scrolls
    });

    [Fact]
    public void Card_WindowGone_RemovesPreview() => OnSta(() =>
    {
        var card = new WindowPreviewCard();
        var closed = 0;
        card.Closed += () => closed++;
        card.Build(Windows(3));
        card.Refresh(Windows(2));
        Assert.Equal([1, 2], card.Cells.Select(c => (int)c.Hwnd));
        card.Refresh([]);
        Assert.Empty(card.Cells);
        Assert.Equal(1, closed);
    });

    [Fact]
    public void Card_CellShowsTheTitle() => OnSta(() =>
    {
        var card = new WindowPreviewCard();
        card.Build([new AppWindow(7, "app", "Notes — a very long window title that goes on", @"C:\app.exe", false)]);
        var title = card.Cells[0].Title;
        Assert.Equal("Notes — a very long window title that goes on", title.Text);
        Assert.Equal(System.Windows.TextTrimming.CharacterEllipsis, title.TextTrimming);
    });

    // Review minor (Plan 7): when the card's window is closed by something other than Close() (Windows, its owner
    // going), Closed still fires once, so the dock is not held up for good.
    [Fact]
    public void Card_ClosedFromOutside_RaisesClosedOnce() => OnSta(() =>
    {
        var card = new WindowPreviewCard();
        var closed = 0;
        card.Closed += () => closed++;
        card.Show(Windows(1), new RectI(100, 900, 140, 940), 0);
        Assert.True(card.IsOpen);
        card.WindowForTests!.Close();
        Assert.False(card.IsOpen);
        Assert.Equal(1, closed);
    });
}

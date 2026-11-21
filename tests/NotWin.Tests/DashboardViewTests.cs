using System.Windows;
using NotWin.Core.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Tests;

public sealed class DashboardViewTests
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

    private sealed class ThrowingBuild(TileKind kind) : ITileView
    {
        public TileKind Kind => kind;
        public FrameworkElement Build(TileSize size) => throw new NullReferenceException("bad cached data");
        public void Opened() { }
        public void Closed() { }
        public void Dispose() { }
    }

    [Fact]
    public void TileFactoryOrBuildThrows_ShowsPlaceholder_InsteadOfCrashing() => OnSta(() =>
    {
        var view = new DashboardView(k => k switch
        {
            TileKind.Timer => throw new InvalidOperationException("factory failed"),
            TileKind.Media => new ThrowingBuild(k),
            _ => new PlaceholderTile(k),
        });
        view.Show(DashboardLayout.Default, 0, editing: false); // must not throw
        view.SetOpen(true);
        Assert.IsType<PlaceholderTile>(view.View(TileKind.Timer));
        Assert.True(view.Height > 0);
    });

    [Fact]
    public void StartingADrag_SurvivesTheReentrantMoveThatMouseCaptureRaises() => OnSta(() =>
    {
        // Live crash 2026-11-19 09:35/09:37: CaptureMouse() makes WPF raise MouseMove synchronously; that re-entered the
        // drag before the ghost existed and Canvas.SetLeft(null) took the shell down.
        var view = new DashboardView(k => new PlaceholderTile(k));
        FrameworkElement? first = null;
        view.TilePlaced += (el, i) => { if (i == 0) first = el; };
        view.Show(DashboardLayout.Default, 0, editing: true);
        Assert.NotNull(first);
        var reentered = false;
        view.CaptureForDrag = () => { reentered = true; view.DragTo(new Point(30, 30), pressed: true); return true; };
        view.PressAt(first!, 0, new Point(10, 10));
        view.DragTo(new Point(40, 40), pressed: true); // must not throw
        Assert.True(reentered);
        Assert.True(view.IsDragging);
    });

    [Fact]
    public void DraggingOverTheGrid_InAShownWindow_DoesNotThrow() => OnSta(() =>
    {
        // The page dots are not under the viewport: hit-testing them during a drag must not throw (TransformToAncestor).
        var view = new DashboardView(k => new PlaceholderTile(k));
        FrameworkElement? first = null;
        view.TilePlaced += (el, i) => { if (i == 0) first = el; };
        var window = new Window
        {
            Left = -5000, Top = -5000, Width = 800, Height = 360, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, Content = view.Root,
        };
        window.Show();
        try
        {
            view.Show(DashboardLayout.Default, 0, editing: true);
            window.UpdateLayout();
            view.CaptureForDrag = () => true;
            view.PressAt(first!, 0, new Point(10, 10));
            foreach (var x in new[] { 40.0, 200, 400, 600, 740 }) view.DragTo(new Point(x, 60), pressed: true);
            Assert.True(view.IsDragging);
        }
        finally { window.Close(); }
    });
}

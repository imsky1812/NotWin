using System.Windows;
using NothingShell.Core.Notch;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Tests;

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
}

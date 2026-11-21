using System.Diagnostics;
using System.Windows.Threading;
using NothingShell.Core;

namespace NothingShell.Widgets;

/// <summary>
/// The 180 ms ease-out slide of a widget window in edit mode. A slide cut short (a new drag, another slide, edit mode
/// ending) always finishes at its destination: before, a second drag within 180 ms left the first widget mid-way.
/// </summary>
public sealed class WidgetSlide
{
    private const double Ms = 180;
    private DispatcherTimer? _timer;
    private Action<RectI>? _place;
    private RectI _to;

    public bool Running => _timer is not null;

    public void Start(Action<RectI> place, RectI from, RectI to)
    {
        Finish();
        _place = place;
        _to = to;
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / Ms);
            var k = 1 - Math.Pow(1 - t, 3);
            int L(int a, int b) => (int)Math.Round(a + (b - a) * k);
            place(new RectI(L(from.Left, to.Left), L(from.Top, to.Top), L(from.Right, to.Right), L(from.Bottom, to.Bottom)));
            if (t >= 1) Stop();
        };
        _timer = timer;
        timer.Start();
    }

    /// <summary>Ends a running slide at its destination; nothing when none runs.</summary>
    public void Finish()
    {
        if (_timer is null) return;
        var place = _place;
        Stop();
        place?.Invoke(_to);
    }

    private void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _place = null;
    }
}

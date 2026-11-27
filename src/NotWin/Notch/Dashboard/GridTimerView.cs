using System.Windows;
using System.Windows.Media;
using NotWin.Core.Timing;

namespace NotWin.Notch.Dashboard;

/// <summary>
/// The grid timer (mockup <c>gridTimer(size, N, frac, flash)</c>): N×N dots, lit #F2F2F2, gone #2C2C2C, the next to go
/// out red while running, every dot red during the time-up flash. Dot radius = pitch × 0.32; snapped to device pixels.
/// </summary>
public sealed class GridTimerView : FrameworkElement
{
    private static readonly Brush Red = TileChrome.Red;

    /// <summary>Lit and gone dots (the desktop timer widget passes its theme colours, Plan 13 follow-up).</summary>
    public Brush Lit { get; init; } = TileChrome.Text;

    public Brush Gone { get; init; } = TileChrome.Frozen(0x2C, 0x2C, 0x2C);

    /// <summary>Dot diameter as a share of the pitch: 0.64 on the desktop widget, finer (0.5) in the notch's 2x2 tile.</summary>
    public double DotRatio { get; init; } = 0.64;
    private readonly double _size;
    private readonly int _n;
    private double _fraction = 1;
    private bool _running, _flash;

    public GridTimerView(double size, int n)
    {
        _size = size;
        _n = n;
        SnapsToDevicePixels = true;
    }

    public void Show(double fraction, bool running, bool flash)
    {
        if (fraction == _fraction && running == _running && flash == _flash) return;
        _fraction = fraction;
        _running = running;
        _flash = flash;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(_size, _size);

    protected override void OnRender(DrawingContext dc)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        var pitchPx = Math.Max(2, Math.Round(_size / _n * scale));
        var dotPx = Math.Max(1, Math.Round(pitchPx * DotRatio));
        var pitch = pitchPx / scale;
        var r = dotPx / scale / 2;
        var offset = Math.Round((_size - pitch * _n) / 2 * scale) / scale; // centre the snapped grid in the box
        var (alive, next) = TimerGrid.State(_n, _fraction);
        for (var i = 0; i < _n * _n; i++)
        {
            var brush = _flash ? Red : alive.Contains(i) ? (_running && i == next ? Red : Lit) : Gone;
            var x = offset + (i % _n) * pitch + pitch / 2;
            var y = offset + (i / _n) * pitch + pitch / 2;
            dc.DrawEllipse(brush, null, new Point(x, y), r, r);
        }
    }
}

using System.Windows;
using System.Windows.Media;
using NotWin.Core.Widgets;
using NotWin.Notch.Dashboard;

namespace NotWin.Notch;

/// <summary>
/// A ring of <see cref="Count"/> dots (Plan 13: stopwatch, sun compass), the first at 12 o'clock: the first
/// <see cref="Lit"/> in <see cref="LitFill"/>, the rest in <see cref="DimFill"/>.
/// </summary>
public sealed class DotRingView : FrameworkElement
{
    private int _lit;

    public int Count { get; set; } = 60;
    public double Radius { get; set; } = 40;
    public double Dot { get; set; } = 3;
    public Brush LitFill { get; set; } = TileChrome.Red;
    public Brush DimFill { get; set; } = TileChrome.FrozenArgb(0x40, 0xE8, 0xE8, 0xE8);

    public int Lit
    {
        get => _lit;
        set
        {
            if (_lit == value) return;
            _lit = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(2 * Radius + Dot, 2 * Radius + Dot);

    protected override void OnRender(DrawingContext dc)
    {
        var c = Radius + Dot / 2;
        var points = DotRing.Points(Count, Radius);
        for (var i = 0; i < points.Count; i++)
            dc.DrawEllipse(i < _lit ? LitFill : DimFill, null, new Point(c + points[i].X, c + points[i].Y), Dot / 2, Dot / 2);
    }
}

using System.Windows;
using System.Windows.Media;
using NotWin.Core.Notch;

namespace NotWin.Notch;

/// <summary>
/// The music equalizer: <see cref="EqualizerModel.Columns"/> × <see cref="EqualizerModel.Rows"/> dots lit from the bottom,
/// the top lit dot red where the model says so. Drawn in one pass (no child elements) and snapped to whole device
/// pixels like <see cref="DotMatrix"/>, so every dot is the same size at 125 %.
/// </summary>
public sealed class DotColumns : FrameworkElement
{
    private const int Cols = EqualizerModel.Columns, Rows = EqualizerModel.Rows;
    private readonly int[] _heights = Enumerable.Repeat(1, Cols).ToArray();
    private readonly bool[] _red = new bool[Cols];

    public double Dot { get; set; } = 3;
    public double Gap { get; set; } = 2;
    public Brush On { get; set; } = Brushes.White;
    public Brush Off { get; set; } = Brushes.DimGray;
    public Brush Accent { get; set; } = Brushes.Red;

    public DotColumns() => SnapsToDevicePixels = true;

    public void Show(EqualizerModel model)
    {
        for (var c = 0; c < Cols; c++)
        {
            _heights[c] = model.Heights[c];
            _red[c] = model.RedTop(c);
        }
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
    }

    private (double Dot, double Pitch) Grid()
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        var dotPx = Math.Max(2, Math.Round(Dot * scale));
        var gapPx = Math.Max(1, Math.Round(Gap * scale));
        return (dotPx / scale, (dotPx + gapPx) / scale);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (dot, pitch) = Grid();
        return new Size((Cols - 1) * pitch + dot, (Rows - 1) * pitch + dot);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var (dot, pitch) = Grid();
        var r = dot / 2;
        for (var c = 0; c < Cols; c++)
        {
            var top = Rows - _heights[c]; // first lit row (rows count from the top)
            for (var row = 0; row < Rows; row++)
            {
                var brush = row < top ? Off : row == top && _red[c] ? Accent : On;
                dc.DrawEllipse(brush, null, new Point(c * pitch + r, row * pitch + r), r, r);
            }
        }
    }
}

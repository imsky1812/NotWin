using System.Windows;
using System.Windows.Media;
using NotWin.Notch.Dashboard;

namespace NotWin.Notch;

/// <summary>
/// Columns of dots lit from the bottom (Plan 13: the meters' CPU history, the screen-time week). Each value in
/// <see cref="Columns"/> is the lit count of its column (clamped to <see cref="Rows"/>); <see cref="AccentColumn"/> is
/// drawn in <see cref="Accent"/>.
/// </summary>
public sealed class DotChartView : FrameworkElement
{
    private IReadOnlyList<int> _columns = [];

    public int Rows { get; set; } = 8;
    public int ColumnCount { get; set; } = 7;
    public double Dot { get; set; } = 4;
    public double Gap { get; set; } = 2;
    public double ColumnGap { get; set; } = 2;
    public Brush On { get; set; } = TileChrome.Text;
    public Brush Off { get; set; } = TileChrome.FrozenArgb(0x26, 0xE8, 0xE8, 0xE8);
    public Brush Accent { get; set; } = TileChrome.Red;
    public int AccentColumn { get; set; } = -1;

    public IReadOnlyList<int> Columns
    {
        get => _columns;
        set
        {
            _columns = value;
            InvalidateVisual();
        }
    }

    private double ColumnPitch => Dot + ColumnGap;

    protected override Size MeasureOverride(Size availableSize) =>
        new(ColumnCount * ColumnPitch - ColumnGap, Rows * (Dot + Gap) - Gap);

    protected override void OnRender(DrawingContext dc)
    {
        // The newest value is the right-most column; fewer values than columns leave the left side unlit.
        var offset = ColumnCount - _columns.Count;
        for (var col = 0; col < ColumnCount; col++)
        {
            var i = col - offset;
            var lit = i >= 0 ? Math.Clamp(_columns[i], 0, Rows) : 0;
            var brush = i == AccentColumn ? Accent : On;
            for (var row = 0; row < Rows; row++)
            {
                var fromBottom = Rows - 1 - row;
                var x = col * ColumnPitch + Dot / 2;
                var y = row * (Dot + Gap) + Dot / 2;
                dc.DrawEllipse(fromBottom < lit ? brush : Off, null, new Point(x, y), Dot / 2, Dot / 2);
            }
        }
    }
}

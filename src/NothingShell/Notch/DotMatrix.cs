using System.Windows;
using System.Windows.Media;
using NothingShell.Core.Notch;

namespace NothingShell.Notch;

/// <summary>
/// Draws <see cref="Text"/> in the 5×7 dot font (numbers only, spec §5). Dots are snapped to whole device pixels: at
/// 125% a 3 DIP dot is 3.75 px and antialiasing made every dot a slightly different size (user feedback 2026-11-19).
/// </summary>
public sealed class DotMatrix : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DotMatrix),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(DotMatrix),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// <summary>Requested dot diameter in DIPs (rounded to whole device pixels when drawn).</summary>
    public double Dot { get; set; } = 2.6;

    /// <summary>Requested gap between dots in DIPs (rounded to whole device pixels, at least 1).</summary>
    public double Gap { get; set; } = 1;

    public DotMatrix() => SnapsToDevicePixels = true;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
    }

    // Dot and pitch in DIPs that are exact multiples of a device pixel.
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
        var cols = DotFont.ColumnsFor(Text ?? "");
        if (cols == 0) return new Size(0, 0);
        var (dot, pitch) = Grid();
        return new Size((cols - 1) * pitch + dot, (DotFont.Rows - 1) * pitch + dot);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var (dot, pitch) = Grid();
        var r = dot / 2;
        foreach (var (col, row) in DotFont.LitDots(Text ?? ""))
            dc.DrawEllipse(Fill, null, new Point(col * pitch + r, row * pitch + r), r, r);
    }
}

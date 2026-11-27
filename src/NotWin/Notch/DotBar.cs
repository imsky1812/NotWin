using System.Windows;
using System.Windows.Media;

namespace NotWin.Notch;

/// <summary>
/// A row of <see cref="Count"/> round dots, the first <see cref="Lit"/> lit and the last lit one in <see cref="Accent"/>
/// (the charging card's battery level). Dots are snapped to whole device pixels like <see cref="DotMatrix"/>.
/// </summary>
public sealed class DotBar : FrameworkElement
{
    public static readonly DependencyProperty LitProperty = DependencyProperty.Register(
        nameof(Lit), typeof(int), typeof(DotBar), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Lit { get => (int)GetValue(LitProperty); set => SetValue(LitProperty, value); }

    public int Count { get; set; } = 10;
    public double Dot { get; set; } = 4;
    public double Gap { get; set; } = 2;
    public Brush On { get; set; } = Brushes.White;
    public Brush Off { get; set; } = Brushes.DimGray;

    /// <summary>
    /// Spreads the dots evenly over the whole width the bar is given (the notch music tile's seek dots run edge to edge
    /// at any display scale); otherwise the bar is exactly as wide as its dots.
    /// </summary>
    public bool Spread { get; set; }

    /// <summary>Brush of the last lit dot; may be animated (its opacity pulses while charging).</summary>
    public Brush Accent { get; set; } = Brushes.Red;

    public DotBar() => SnapsToDevicePixels = true;

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
        if (Count <= 0) return new Size(0, 0);
        var natural = (Count - 1) * pitch + dot;
        return new Size(Spread && !double.IsInfinity(availableSize.Width) ? Math.Max(natural, availableSize.Width) : natural, dot);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var (dot, pitch) = Grid();
        var r = dot / 2;
        var lit = Math.Clamp(Lit, 0, Count);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        if (Spread && Count > 1 && ActualWidth > (Count - 1) * pitch + dot)
            pitch = (ActualWidth - dot) / (Count - 1);
        for (var i = 0; i < Count; i++)
        {
            var brush = i >= lit ? Off : i == lit - 1 ? Accent : On;
            var x = Math.Round(i * pitch * scale) / scale; // whole device pixels, so spread dots stay crisp
            dc.DrawEllipse(brush, null, new Point(x + r, r), r, r);
        }
    }
}

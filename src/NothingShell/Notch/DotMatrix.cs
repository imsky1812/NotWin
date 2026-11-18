using System.Windows;
using System.Windows.Media;
using NothingShell.Core.Notch;

namespace NothingShell.Notch;

/// <summary>Draws <see cref="Text"/> in the 5×7 dot font (numbers only, spec §5).</summary>
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

    /// <summary>Dot diameter in DIPs.</summary>
    public double Dot { get; set; } = 3;

    /// <summary>Space between dots in DIPs.</summary>
    public double Gap { get; set; } = 1;

    private double Pitch => Dot + Gap;

    protected override Size MeasureOverride(Size availableSize)
    {
        var cols = DotFont.ColumnsFor(Text ?? "");
        return cols == 0 ? new Size(0, 0) : new Size(cols * Pitch - Gap, DotFont.Rows * Pitch - Gap);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = Dot / 2;
        foreach (var (col, row) in DotFont.LitDots(Text ?? ""))
            dc.DrawEllipse(Fill, null, new Point(col * Pitch + r, row * Pitch + r), r, r);
    }
}

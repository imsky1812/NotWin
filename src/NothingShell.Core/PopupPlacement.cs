namespace NothingShell.Core;

/// <summary>
/// Where a widget's editor popup goes (Plan 10): right of the widget with a gap, else left of it, kept inside the
/// widget's own screen (physical px).
/// </summary>
public static class PopupPlacement
{
    public static (int Left, int Top) NextTo(RectI anchor, RectI monitor, int width, int height, int gap)
    {
        var left = anchor.Right + gap + width <= monitor.Right ? anchor.Right + gap : Math.Max(monitor.Left, anchor.Left - gap - width);
        var top = Math.Clamp(anchor.Top, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - height));
        return (left, top);
    }
}

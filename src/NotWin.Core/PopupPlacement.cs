namespace NotWin.Core;

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

    /// <summary>
    /// Centred under the anchor with a gap (Plan 13: edit mode's choice list); above it when there is no room below;
    /// always kept inside the anchor's screen.
    /// </summary>
    public static (int Left, int Top) Below(RectI anchor, RectI monitor, int width, int height, int gap)
    {
        var left = (anchor.Left + anchor.Right) / 2 - width / 2;
        left = Math.Clamp(left, monitor.Left, Math.Max(monitor.Left, monitor.Right - width));
        var top = anchor.Bottom + gap;
        if (top + height > monitor.Bottom) top = anchor.Top - gap - height;
        top = Math.Clamp(top, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - height));
        return (left, top);
    }
}

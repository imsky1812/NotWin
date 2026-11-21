namespace NotWin.Core;

/// <summary>
/// Pure edge/dock geometry. Unit-agnostic: the cursor and monitor bounds must be in the same
/// coordinate space (physical pixels in a PerMonitorV2 app). Only <see cref="DockBounds"/>
/// takes DIP-style doubles for the dock size and gap; callers pass values already in the
/// monitor's coordinate space (i.e. scaled by the monitor DPI) or accept 1:1 mapping.
/// </summary>
public static class EdgeGeometry
{
    /// <summary>
    /// True only when the cursor is on the monitor's last pixel row (Y == Bottom - 1) and X is in
    /// [Left, Right). Bounded above Bottom so a monitor stacked below does not count as the edge.
    /// </summary>
    public static bool IsAtBottomEdge(PointI cursor, RectI monitor) =>
        cursor.Y >= monitor.Bottom - 1 && cursor.Y < monitor.Bottom &&
        cursor.X >= monitor.Left && cursor.X < monitor.Right;

    /// <summary>
    /// Dock rectangle centered horizontally on the monitor, its bottom <paramref name="bottomGap"/>
    /// above <c>monitor.Bottom</c>. Width, height and gap are each rounded to the nearest integer
    /// (midpoint away from zero); the horizontal leftover is split with integer division, so an odd
    /// leftover puts the extra pixel on the right and the dock width is always preserved exactly.
    /// The 60/12 defaults are DIP values: when the monitor is in physical pixels the caller must
    /// pass width, height and gap already scaled by the monitor DPI.
    /// </summary>
    public static RectI DockBounds(RectI monitor, double width, double height = 60, double bottomGap = 12)
    {
        int w = (int)Math.Round(width, MidpointRounding.AwayFromZero);
        int h = (int)Math.Round(height, MidpointRounding.AwayFromZero);
        int gap = (int)Math.Round(bottomGap, MidpointRounding.AwayFromZero);

        int left = monitor.Left + (monitor.Right - monitor.Left - w) / 2;
        int bottom = monitor.Bottom - gap;
        return new RectI(left, bottom - h, left + w, bottom);
    }
}

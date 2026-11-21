using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NotWin.Core.Notch;

namespace NotWin.Notch;

/// <summary>
/// Draggable progress dots (user, 2026-11-21), shared by the media widget and the notch's media tile: press anywhere
/// on the dots to jump there, drag to pick the spot, let go to play from it. A taller see-through strip around the
/// dots takes the pointer (the gaps between dots would not). <c>seeking</c> tells the owner to stop redrawing meanwhile
/// (true) and that it may redraw again (false).
/// </summary>
public static class SeekBar
{
    public static FrameworkElement Wrap(DotBar dots, int count, TimeSpan duration, Func<TimeSpan, Task<bool>> seek, Action<bool> seeking)
    {
        if (duration <= TimeSpan.Zero) return dots;
        var hit = new Border
        {
            Background = Brushes.Transparent, Padding = new Thickness(0, 5, 0, 5), Cursor = Cursors.Hand,
            HorizontalAlignment = dots.HorizontalAlignment, VerticalAlignment = dots.VerticalAlignment, Child = dots,
        };
        var active = false;
        hit.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            active = true;
            seeking(true);
            hit.CaptureMouse();
            dots.Lit = MediaProgress.LitAt(e.GetPosition(dots).X, dots.ActualWidth, count);
        };
        hit.MouseMove += (_, e) =>
        {
            if (active && hit.IsMouseCaptured) dots.Lit = MediaProgress.LitAt(e.GetPosition(dots).X, dots.ActualWidth, count);
        };
        hit.MouseLeftButtonUp += (_, e) =>
        {
            if (!active) return;
            e.Handled = true;
            var target = MediaProgress.SeekTarget(e.GetPosition(dots).X, dots.ActualWidth, duration);
            active = false;
            hit.ReleaseMouseCapture();
            seeking(false);
            if (target is { } t) _ = seek(t);
        };
        hit.LostMouseCapture += (_, _) =>
        {
            if (!active) return;
            active = false; // cancelled (another window took the mouse): back to the real position
            seeking(false);
        };
        return hit;
    }
}

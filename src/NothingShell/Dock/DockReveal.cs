using System.Diagnostics;
using System.Windows.Threading;
using NothingShell.Core;

namespace NothingShell.Dock;

/// <summary>
/// Feeds <see cref="DockRevealMachine"/> from the mouse hook, the dock's own hover state, full-screen changes and
/// settings, and mirrors its <c>Visible</c> into <see cref="DockViewModel.IsVisible"/>. UI thread only.
/// A single DispatcherTimer runs only while the machine has a deadline pending; otherwise nothing ticks.
/// </summary>
public sealed class DockReveal : IDisposable
{
    private readonly DockRevealMachine _machine = DockRevealMachine.CreateDefault();
    private readonly DockViewModel _vm;
    private readonly Func<DockPlacement> _placement;
    private readonly DispatcherTimer _deadline;
    private readonly long _epoch = Stopwatch.GetTimestamp();
    private readonly bool _trace = Environment.GetEnvironmentVariable("NOTHINGSHELL_DOCK_TRACE") == "1";

    private PointI? _cursor;
    private bool _hoverDock;
    private bool _panelOpen;
    private bool _keepVisible;
    private bool _fullscreen;
    private bool _disposed;

    public DockReveal(DockViewModel vm, Func<DockPlacement> placement)
    {
        _vm = vm;
        _placement = placement;
        _deadline = new DispatcherTimer(DispatcherPriority.Input);
        _deadline.Tick += (_, _) =>
        {
            _deadline.Stop();
            Evaluate("deadline");
        };
    }

    /// <summary>Last inputs given to the machine (for diagnostics and the Debug self-test).</summary>
    public DockInputs LastInputs { get; private set; }

    public void OnCursorMoved(PointI p)
    {
        _cursor = p;
        Evaluate("move");
    }

    public void SetHoverDock(bool over)
    {
        if (_hoverDock == over) return;
        _hoverDock = over;
        Evaluate(over ? "enter" : "leave");
    }

    /// <summary>
    /// The hook is uninstalled while full-screen, so the last known point goes stale. Entering full-screen forgets it;
    /// leaving full-screen takes <paramref name="cursorNow"/> (the real position from GetCursorPos) so a point left
    /// on the bottom edge before the game/video started can't pop the dock up.
    /// </summary>
    public void SetFullscreen(bool on, PointI? cursorNow = null)
    {
        if (_fullscreen == on) return;
        _fullscreen = on;
        _cursor = on ? null : cursorNow;
        Evaluate(on ? "fullscreen-on" : "fullscreen-off");
    }

    public void SetKeepVisible(bool on)
    {
        if (_keepVisible == on) return;
        _keepVisible = on;
        Evaluate(on ? "keep-on" : "keep-off");
    }

    public void SetPanelOpen(bool open)
    {
        if (_panelOpen == open) return;
        _panelOpen = open;
        Evaluate(open ? "panel-open" : "panel-closed");
    }

    /// <summary>Re-runs the machine with the current inputs (e.g. after the display layout changed).</summary>
    public void Refresh() => Evaluate("refresh");

    private void Evaluate(string reason)
    {
        if (_disposed) return;
        var place = _placement();
        bool atEdge = false, overPill = false;
        if (_cursor is { } c)
        {
            atEdge = EdgeGeometry.IsAtBottomEdge(c, place.Monitor);
            overPill = c.X >= place.Pill.Left && c.X < place.Pill.Right && c.Y >= place.Pill.Top && c.Y < place.Pill.Bottom;
        }

        var inputs = new DockInputs(
            CursorAtEdge: atEdge,
            CursorOverDock: _hoverDock || overPill,
            PanelOpen: _panelOpen,
            KeepVisible: _keepVisible,
            Fullscreen: _fullscreen);
        LastInputs = inputs;

        var now = Stopwatch.GetElapsedTime(_epoch);
        var was = _machine.Visible;
        _machine.Update(inputs, now);

        if (_machine.Visible != was && _trace)
            Log.Info($"dock {(was ? "hide" : "show")} at {now.TotalMilliseconds:F0} ms ({reason}; {inputs})");
        _vm.IsVisible = _machine.Visible;

        _deadline.Stop();
        if (_machine.NextDeadline is { } due)
        {
            var wait = due - now;
            if (wait < TimeSpan.FromMilliseconds(1)) wait = TimeSpan.FromMilliseconds(1);
            _deadline.Interval = wait;
            _deadline.Start();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _deadline.Stop();
    }
}

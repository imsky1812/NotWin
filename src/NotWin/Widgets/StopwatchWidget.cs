using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Stopwatch (Plan 13, 1×1 circle / 2×1 pill): a ring of 60 dots with the seconds of the current minute lit red and the
/// time in dots ("MM:SS", "H:MM" past an hour). A click starts / pauses; a double-click while paused resets. In memory
/// only. Redraws on each whole elapsed second, only while running and not paused.
/// </summary>
public sealed class StopwatchWidget : IDesktopWidget
{
    private readonly StopwatchModel _model;
    private readonly DispatcherTimer _timer = new();
    private DotRingView? _ring;
    private DotMatrix? _time;
    private TextBlock? _state;
    private bool _paused, _wasRunning;

    public StopwatchWidget(Func<TimeSpan> clock)
    {
        _model = new StopwatchModel(clock);
        _timer.Tick += (_, _) => { Update(); Schedule(); };
    }

    public WidgetKind Kind => WidgetKind.Stopwatch;

    internal bool Running => _model.Running;
    internal TimeSpan Elapsed => _model.Elapsed;
    internal bool Ticking => _timer.IsEnabled;

    public FrameworkElement Build(WidgetSize size)
    {
        FrameworkElement content;
        if (size.W == 1)
        {
            _ring = new DotRingView { Count = 60, Radius = 54, Dot = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _time = new DotMatrix { Dot = 2.4, Gap = 1, Fill = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _state = null;
            content = new Grid { Children = { _ring, _time } };
        }
        else
        {
            _ring = new DotRingView { Count = 60, Radius = 50, Dot = 3, VerticalAlignment = VerticalAlignment.Center };
            _time = new DotMatrix { Dot = 3.4, Gap = 1.2, Fill = TileChrome.Text, Margin = new Thickness(0, 8, 0, 8) };
            _state = TileChrome.Label("");
            content = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    _ring,
                    new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Children = { TileChrome.Label("Stopwatch"), _time, _state } },
                },
            };
        }
        var hit = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { content } };
        hit.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) DoubleClick();
            else Click();
        };
        Update();
        Restart();
        return hit;
    }

    /// <summary>Start / pause.</summary>
    internal void Click()
    {
        _wasRunning = _model.Running;
        if (_model.Running) _model.Pause();
        else _model.Start();
        Update();
        Restart();
    }

    /// <summary>The second click of a double-click: on a stopwatch that was paused it undoes the start and resets.</summary>
    internal void DoubleClick()
    {
        if (!_wasRunning)
        {
            _model.Pause();
            _model.Reset();
        }
        Update();
        Restart();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (!paused) Update();
        Restart();
    }

    private void Restart()
    {
        _timer.Stop();
        if (_model.Running && !_paused) Schedule();
    }

    private void Schedule()
    {
        _timer.Stop();
        _timer.Interval = _model.UntilNextSecond + TimeSpan.FromMilliseconds(10);
        _timer.Start();
    }

    private void Update()
    {
        var e = _model.Elapsed;
        if (_ring is not null) _ring.Lit = (int)e.TotalSeconds % 60;
        if (_time is not null) _time.Text = StopwatchModel.Format(e);
        if (_state is not null)
            _state.Text = _model.Running ? "RUNNING" : e == TimeSpan.Zero ? "TAP TO START" : "PAUSED · DOUBLE-CLICK TO RESET";
    }

    public void Dispose() => _timer.Stop();
}

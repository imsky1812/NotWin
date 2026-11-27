using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Notch.Dashboard;
using NotWin.Timing;

namespace NotWin.Widgets;

/// <summary>
/// Timer widget (spec §6.5 grid style, mockup <c>renderTimer</c>): the same <see cref="TimerService"/> as the notch
/// (spec §6.6). 1×1 a 10×10 grid; 2×1 grid + "TIMER" + dot mm:ss; 2×2 a 12×12 grid, dot mm:ss and RUNNING / TAP TO
/// START. A click starts or pauses; the grid flashes red at zero.
/// </summary>
public sealed class TimerWidget : IDesktopWidget
{
    private readonly TimerService _timer;
    private GridTimerView? _grid;
    private DotMatrix? _dots;
    private TextBlock? _state;

    public TimerWidget(TimerService timer)
    {
        _timer = timer;
        _timer.Changed += Update;
    }

    public WidgetKind Kind => WidgetKind.Timer;

    public FrameworkElement Build(WidgetSize size)
    {
        _dots = null;
        _state = null;
        FrameworkElement content;
        if (size == new WidgetSize(1, 1))
        {
            _grid = new GridTimerView(100, 10) { Lit = WidgetTheme.Text, Gone = WidgetTheme.Unlit, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content = _grid;
        }
        else if (size == new WidgetSize(2, 1))
        {
            _grid = new GridTimerView(100, 10) { Lit = WidgetTheme.Text, Gone = WidgetTheme.Unlit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            _dots = new DotMatrix { Dot = 3.4, Gap = 1.2, Fill = WidgetTheme.Text, Margin = new Thickness(0, 10, 0, 0) };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { WidgetTheme.Label("Timer"), _dots } };
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _grid, text } };
        }
        else
        {
            _grid = new GridTimerView(200, 12) { Lit = WidgetTheme.Text, Gone = WidgetTheme.Unlit, HorizontalAlignment = HorizontalAlignment.Center };
            _dots = new DotMatrix { Dot = 3, Gap = 1.2, Fill = WidgetTheme.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _state = WidgetTheme.Label("");
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), Children = { _dots, _state } };
            content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _grid, row } };
        }
        var hit = new Grid { Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand, Children = { content } };
        hit.MouseLeftButtonUp += (_, e) => { e.Handled = true; _timer.Toggle(); };
        Update();
        return hit;
    }

    private void Update()
    {
        _grid?.Show(_timer.Fraction, _timer.Running, _timer.FlashOn);
        if (_dots is not null) _dots.Text = _timer.Text;
        if (_state is not null) _state.Text = _timer.Running ? "RUNNING" : "TAP TO START";
    }

    public void Dispose() => _timer.Changed -= Update;
}

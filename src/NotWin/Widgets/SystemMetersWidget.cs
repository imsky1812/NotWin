using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// System meters (Plan 13, 2×1 / 2×2). 2×1: CPU, RAM and NET rows, each a caps label, a 10-dot bar and the value; a
/// bar turns red above 90%. 2×2: CPU as a dot history (last 20 samples × 8 rows) with its value, then RAM, DOWN and UP
/// rows. Samples every 2 s, only while on the desktop and not paused. Adapter names are never shown.
/// </summary>
public sealed class SystemMetersWidget : IDesktopWidget
{
    private static Brush Unlit => WidgetTheme.Unlit;
    private readonly MeterSampler _sampler;
    private (int Cpu, int Ram, double Down, double Up) _last;
    private readonly Dictionary<string, (DotBar Bar, TextBlock Value)> _rows = [];
    private DotChartView? _history;
    private TextBlock? _cpuValue;
    private bool _paused, _disposed;

    public SystemMetersWidget(Func<MeterSampler> makeSampler)
    {
        _sampler = makeSampler();
        _sampler.Sampled += (cpu, ram, down, up) =>
        {
            _last = (cpu, ram, down, up);
            Fill();
        };
    }

    public WidgetKind Kind => WidgetKind.SystemMeters;

    internal bool Sampling => _sampler.Running;

    internal void SampleNow() => _sampler.Sample();

    public FrameworkElement Build(WidgetSize size)
    {
        _rows.Clear();
        _history = null;
        _cpuValue = null;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (size.H == 1)
        {
            stack.Children.Add(Row("CPU"));
            stack.Children.Add(Row("RAM"));
            stack.Children.Add(Row("NET"));
        }
        else
        {
            _cpuValue = Value();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(_cpuValue, System.Windows.Controls.Dock.Right);
            head.Children.Add(_cpuValue);
            head.Children.Add(WidgetTheme.Label("CPU"));
            _history = new DotChartView
            {
                ColumnCount = MeterSampler.HistoryLength, Rows = 8, Dot = 5, Gap = 3, ColumnGap = 6, Off = Unlit,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14),
            };
            stack.Children.Add(head);
            stack.Children.Add(_history);
            stack.Children.Add(Row("RAM"));
            stack.Children.Add(Row("DOWN"));
            stack.Children.Add(Row("UP"));
        }
        Fill();
        if (!_paused && !_disposed) _sampler.Start();
        return stack;
    }

    private FrameworkElement Row(string name)
    {
        var bar = new DotBar { Count = 10, Dot = 6, Gap = 4, Off = Unlit, VerticalAlignment = VerticalAlignment.Center };
        var value = Value();
        _rows[name] = (bar, value);
        var label = WidgetTheme.Label(name);
        label.Width = 44;
        var grid = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(label, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
        grid.Children.Add(label);
        grid.Children.Add(value);
        grid.Children.Add(bar);
        return grid;
    }

    private static TextBlock Value() => new()
    {
        FontFamily = TileChrome.Mono, FontSize = 11, Foreground = WidgetTheme.Text, MinWidth = 64, TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void Fill()
    {
        var (cpu, ram, down, up) = _last;
        Set("CPU", cpu, Percent(cpu), MeterMath.Hot(cpu));
        Set("RAM", ram, Percent(ram), MeterMath.Hot(ram));
        Set("NET", RatePercent(down + up), MeterMath.FormatRate(down + up), false);
        Set("DOWN", RatePercent(down), MeterMath.FormatRate(down), false);
        Set("UP", RatePercent(up), MeterMath.FormatRate(up), false);
        if (_cpuValue is not null) _cpuValue.Text = Percent(cpu);
        if (_history is not null)
        {
            _history.Columns = _sampler.CpuHistory.Select(v => MeterMath.Lit(v, 8)).ToList();
            _history.On = MeterMath.Hot(cpu) ? TileChrome.Red : WidgetTheme.Text;
        }
    }

    private void Set(string name, int percent, string text, bool hot)
    {
        if (!_rows.TryGetValue(name, out var row)) return;
        var brush = hot ? TileChrome.Red : WidgetTheme.Text;
        row.Bar.On = brush;
        row.Bar.Accent = brush;
        row.Bar.Lit = MeterMath.Lit(percent, row.Bar.Count);
        row.Bar.InvalidateVisual();
        row.Value.Text = text;
    }

    private static string Percent(int v) => v.ToString(CultureInfo.InvariantCulture) + "%";

    // Network bars on a log scale: 1 KB/s lights the first dot, 100 MB/s the whole bar.
    private static int RatePercent(double bytesPerSec) =>
        bytesPerSec < 1024 ? 0 : (int)Math.Clamp(Math.Log10(bytesPerSec / 1024) / 5 * 100, 1, 100);

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused) _sampler.Stop();
        else if (!_disposed) _sampler.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _sampler.Dispose();
    }
}

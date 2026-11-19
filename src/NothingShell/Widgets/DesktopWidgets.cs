using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NothingShell.Core;
using NothingShell.Core.Widgets;
using NothingShell.Native;

namespace NothingShell.Widgets;

/// <summary>
/// The desktop widgets (spec §6.5): one <see cref="WidgetHost"/> per widget on the desktop layer, placed from the
/// saved layout at the global scale (S/M/L) and the monitor's DPI. The layout origin is the primary monitor's top-left
/// plus (80, 56) unscaled. When Explorer restarts (the widget windows die with the desktop) a 250 ms check runs only
/// until the new desktop appears, then every widget is re-created there.
/// </summary>
public sealed class DesktopWidgets : IDisposable
{
    private const double OriginX = 80, OriginY = 56;
    private readonly Func<ShellSettings> _get;
    private readonly Action<Func<ShellSettings, ShellSettings>> _update;
    private readonly IReadOnlyDictionary<WidgetKind, IDesktopWidget> _widgets;
    private readonly Dictionary<WidgetKind, (WidgetHost Host, Grid Root)> _hosts = [];
    private readonly DispatcherTimer _reattach = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _lostFor = new();
    private DesktopLayout _layout = DesktopLayout.Default;
    private RectI _monitor;
    private double _px = 1;   // physical px per unscaled widget px = DPI scale × widget scale
    private double _scale = 0.65;
    private bool _started, _disposed;

    public DesktopWidgets(Func<ShellSettings> get, Action<Func<ShellSettings, ShellSettings>> update,
        IReadOnlyDictionary<WidgetKind, IDesktopWidget> widgets)
    {
        _get = get;
        _update = update;
        _widgets = widgets;
        _reattach.Tick += (_, _) => TryReattach();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    public DesktopLayout Layout => _layout;
    public bool Editing { get; private set; }

    /// <summary>Unscaled layout area (the part of the monitor right/below the origin, with the same margins).</summary>
    public (int W, int H) Area
    {
        get
        {
            var w = (int)((_monitor.Right - _monitor.Left) / _px - OriginX * 2);
            var h = (int)((_monitor.Bottom - _monitor.Top) / _px - OriginY * 2);
            return (Math.Max(0, w), Math.Max(0, h));
        }
    }

    public void Start()
    {
        _started = true;
        DesktopAttach.Prepare();
        Rebuild();
    }

    /// <summary>Global widget scale S / M / L (spec §5), applied live and saved.</summary>
    public void SetScale(string scale)
    {
        _update(s => s with { WidgetScale = scale });
        Rebuild();
    }

    /// <summary>Re-reads geometry and settings and rebuilds every widget (scale, display or size changes).</summary>
    public void Rebuild()
    {
        if (!_started || _disposed) return;
        Measure();
        _layout = DesktopLayout.Sanitize(_get().Widgets, Area.W, Area.H);
        var host = DesktopAttach.FindHost();
        foreach (var p in _layout.Widgets)
        {
            if (!_widgets.TryGetValue(p.Kind, out var widget)) continue;
            FrameworkElement card;
            try { card = WidgetCard.Wrap(widget.Build(p.Size), widget.Bare); }
            catch (Exception e)
            {
                Log.Error($"widget {p.Kind} failed to build", e);
                continue;
            }
            var root = Root(p, card);
            if (!_hosts.TryGetValue(p.Kind, out var h))
            {
                var wh = new WidgetHost(root);
                wh.Lost += OnLost;
                h = (wh, root);
                _hosts[p.Kind] = h;
            }
            else
            {
                h.Root.Children.Clear();
                h.Root.Children.Add(card);
                h.Root.Width = p.Size.PxW;
                h.Root.Height = p.Size.PxH;
                h.Root.LayoutTransform = new ScaleTransform(_scale, _scale);
                root = h.Root;
            }
            var rect = ScreenRect(p);
            if (host != 0) h.Host.Attach(host, rect);
            Safe(() => widget.Placed(rect, _monitor));
        }
        if (host == 0) StartReattach();
    }

    /// <summary>The widget's window rectangle on screen (physical px).</summary>
    public RectI ScreenRect(WidgetPlacement p) => ScreenRect(p.X, p.Y, p.Size);

    public RectI ScreenRect(double x, double y, WidgetSize size)
    {
        var left = _monitor.Left + (int)Math.Round((OriginX + x) * _px);
        var top = _monitor.Top + (int)Math.Round((OriginY + y) * _px);
        return new RectI(left, top, left + (int)Math.Ceiling(size.PxW * _px), top + (int)Math.Ceiling(size.PxH * _px));
    }

    /// <summary>Screen px → unscaled layout coordinates (edit-mode dragging).</summary>
    public (double X, double Y) ToLayout(double screenX, double screenY) =>
        ((screenX - _monitor.Left) / _px - OriginX, (screenY - _monitor.Top) / _px - OriginY);

    public double PxPerUnit => _px;
    public RectI Monitor => _monitor;

    internal IReadOnlyDictionary<WidgetKind, (WidgetHost Host, Grid Root)> Hosts => _hosts;

    /// <summary>Applies a layout change (edit mode) and saves it.</summary>
    public void Apply(DesktopLayout layout)
    {
        _update(s => s with { Widgets = layout });
        Rebuild();
    }

    // Edit mode is added by Task 11 (BeginEdit / EndEdit).
    public void SetEditing(bool editing) => Editing = editing;

    private Grid Root(WidgetPlacement p, FrameworkElement card) => new()
    {
        Width = p.Size.PxW,
        Height = p.Size.PxH,
        LayoutTransform = new ScaleTransform(_scale, _scale),
        Children = { card },
    };

    private void Measure()
    {
        var (mon, dpi) = DockWindowNative.GetPrimaryMonitor();
        _monitor = mon;
        _scale = ShellSettings.ScaleFactor(_get().WidgetScale);
        _px = dpi / 96.0 * _scale;
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Application.Current?.Dispatcher.BeginInvoke(Rebuild);

    private void OnLost()
    {
        if (_disposed) return;
        StartReattach();
    }

    private void StartReattach()
    {
        if (_reattach.IsEnabled) return;
        _lostFor.Restart();
        Log.Info("widgets lost the desktop (Explorer restart?), waiting for it");
        _reattach.Start();
    }

    private void TryReattach()
    {
        var host = DesktopAttach.FindHost();
        if (host == 0) return;
        _reattach.Stop();
        DesktopAttach.Prepare();
        Rebuild();
        Log.Info($"widgets reattached in {_lostFor.ElapsedMilliseconds} ms");
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Error("widget placement handler failed", e); }
    }

    public void Dispose()
    {
        _disposed = true;
        _reattach.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        foreach (var (host, _) in _hosts.Values) host.Dispose();
        foreach (var w in _widgets.Values) w.Dispose();
    }
}

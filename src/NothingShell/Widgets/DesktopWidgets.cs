using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly Func<WidgetPlacement, Action<WidgetConfig>, IDesktopWidget?> _factory;
    private readonly Dictionary<string, Slot> _slots = [];
    private readonly DispatcherTimer _reattach = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _lostFor = new();
    private readonly DispatcherTimer _dpiDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private DesktopLayout _layout = DesktopLayout.Default;
    private RectI _monitor;
    private double _px = 1;   // physical px per unscaled widget px = DPI scale × widget scale
    private double _scale = 0.65;
    private bool _started, _disposed;
    private int _failedRebuilds;
    private WidgetHost? _backdrop;
    private Dock.DockPopup? _popup;
    private (string Id, PointI Cursor, RectI Start, EditChrome Chrome)? _drag;
    private readonly WidgetSlide _settle = new();
    private AddPicker? _picker;
    private bool _paused;

    // One widget instance on the desktop: its window, the window's root, and the widget object (Plan 5b).
    private sealed class Slot(WidgetHost host, Grid root, IDesktopWidget widget)
    {
        public WidgetHost Host { get; } = host;
        public Grid Root { get; } = root;
        public IDesktopWidget Widget { get; } = widget;
    }

    /// <param name="factory">Makes the widget object for a new instance (null = this kind is unavailable); the second
    /// argument saves the instance's own settings.</param>
    public DesktopWidgets(Func<ShellSettings> get, Action<Func<ShellSettings, ShellSettings>> update,
        Func<WidgetPlacement, Action<WidgetConfig>, IDesktopWidget?> factory)
    {
        _get = get;
        _update = update;
        _factory = factory;
        _reattach.Tick += (_, _) => TryReattach();
        _dpiDebounce.Tick += (_, _) => { _dpiDebounce.Stop(); Rebuild(); };
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
        try
        {
            RebuildCore();
            _failedRebuilds = 0;
        }
        catch (Exception e)
        {
            // A monitor that briefly has no info, or the desktop dying mid-create: try again a few times, never forever.
            _failedRebuilds++;
            Log.Error($"widget rebuild failed ({_failedRebuilds})", e);
            if (_failedRebuilds <= 3) StartReattach();
        }
    }

    // Many WM_DPICHANGED_AFTERPARENT messages arrive together (one per widget): one rebuild after they settle.
    private void OnDpiChanged()
    {
        _dpiDebounce.Stop();
        _dpiDebounce.Start();
    }

    private void RebuildCore()
    {
        Measure();
        var saved = _get();
        _layout = DesktopLayout.Sanitize(saved.WidgetList ?? saved.Widgets, Area.W, Area.H, () => DesktopLayout.NewId(Random.Shared));
        // First run of Plan 5b (or entries without ids): save once so the new ids stay the same from now on.
        if (saved.WidgetList is null || saved.WidgetList.Widgets.Any(p => string.IsNullOrEmpty(p.Id)))
        {
            var converted = _layout;
            _update(s => s with { WidgetList = converted });
        }
        // Instances that left the layout (removed in edit mode): their windows and widget objects go.
        foreach (var gone in _slots.Keys.Where(id => _layout.IndexOf(id) < 0).ToList())
        {
            var slot = _slots[gone];
            _slots.Remove(gone);
            DisposeSlot(slot);
        }
        var host = DesktopAttach.FindHost();
        foreach (var p in _layout.Widgets)
        {
            if (!_slots.TryGetValue(p.Id, out var slot))
            {
                var id = p.Id;
                IDesktopWidget? made;
                try { made = _factory(p, config => SaveConfig(id, config)); }
                catch (Exception e)
                {
                    Log.Error($"widget {p.Kind} could not be created", e);
                    continue;
                }
                if (made is null) continue;
                var root = WidgetRoots.Fill(null, new Grid(), p.Size, _scale);
                root.MouseRightButtonUp += (_, e) => OnContext(e);
                var wh = new WidgetHost(root);
                wh.Lost += OnLost;
                wh.DpiChanged += OnDpiChanged;
                slot = new Slot(wh, root, made);
                _slots[id] = slot;
                if (_paused) Safe(() => made.SetPaused(true));
            }
            var widget = slot.Widget;
            FrameworkElement card;
            try
            {
                widget.Configure(p.Config);
                card = WidgetCard.Wrap(widget.Build(p.Size), widget.Bare);
                if (Editing) card = EditFrame(p, card);
            }
            catch (Exception e)
            {
                Log.Error($"widget {p.Kind} failed to build", e);
                continue;
            }
            WidgetRoots.Fill(slot.Root, card, p.Size, _scale);
            var rect = ScreenRect(p);
            if (host != 0)
            {
                try { slot.Host.Attach(host, rect); }
                catch (Exception e)
                {
                    Log.Error($"widget {p.Kind} could not attach to the desktop", e);
                    StartReattach();
                    continue;
                }
            }
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

    /// <summary>A full-screen app came to the front or left: every widget pauses / resumes its animation.</summary>
    public void SetFullscreen(bool fullscreen)
    {
        _paused = fullscreen;
        foreach (var slot in _slots.Values) Safe(() => slot.Widget.SetPaused(fullscreen));
        if (fullscreen) EndEdit();
    }

    // A widget's own settings changed (photos, apps): saved without a rebuild (the widget already shows it).
    private void SaveConfig(string id, WidgetConfig config)
    {
        if (_layout.IndexOf(id) < 0) return;
        _layout = _layout.WithConfig(id, config);
        var saved = _layout;
        _update(s => s with { WidgetList = saved });
    }

    private void DisposeSlot(Slot slot)
    {
        try { slot.Host.Dispose(); }
        catch (Exception e) { Log.Error("widget window dispose failed", e); }
        Safe(slot.Widget.Dispose);
    }

    /// <summary>Applies a layout change (edit mode) and saves it.</summary>
    public void Apply(DesktopLayout layout)
    {
        _update(s => s with { WidgetList = layout });
        Rebuild();
    }

    /// <summary>Raised when edit mode starts (true) or ends (false).</summary>
    public event Action<bool>? EditingChanged;

    /// <summary>
    /// Edit mode (spec §6.5): the dot-grid backdrop under the widgets, every widget draggable with a 74 px snap and a
    /// size chip; content ignores clicks. Ends with Done, Esc (<see cref="EndEdit"/> from the key hook) or full screen.
    /// </summary>
    public void BeginEdit()
    {
        if (!_started || _disposed || Editing) return;
        Editing = true;
        Log.Info("widget edit mode on");
        Safe(() => EditingChanged?.Invoke(true));
        Rebuild();
        ShowBackdrop();
    }

    public void EndEdit()
    {
        if (!Editing) return;
        Editing = false;
        _drag = null;
        _settle.Finish();
        ClosePicker();
        _backdrop?.Dispose();
        _backdrop = null;
        Log.Info("widget edit mode off");
        Rebuild();
        Safe(() => EditingChanged?.Invoke(false));
    }

    private void ShowBackdrop()
    {
        try { ShowBackdropCore(); }
        catch (Exception e) { Log.Error("widget edit backdrop failed", e); }
    }

    private void ShowBackdropCore()
    {
        var host = DesktopAttach.FindHost();
        if (host == 0) return;
        var dpi = _px / _scale;
        var content = EditBackdrop.Build((_monitor.Right - _monitor.Left) / dpi, (_monitor.Bottom - _monitor.Top) / dpi,
            new Point(OriginX * _scale, OriginY * _scale), _scale, EndEdit, ShowPicker);
        _backdrop = new WidgetHost(content);
        _backdrop.Attach(host, _monitor);
        foreach (var slot in _slots.Values) slot.Host.Raise(); // the widgets above the backdrop
    }

    // ＋ Add: the picker under the button; a pick places the widget in the first free spot (a new folder gets a
    // random picture).
    private void ShowPicker(FrameworkElement button)
    {
        ClosePicker();
        var kinds = _layout.Addable();
        if (kinds.Count == 0) return;
        var a = button.PointToScreen(new Point(0, 0));
        var b = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight));
        var below = new RectI((int)a.X, (int)a.Y, (int)b.X, (int)b.Y);
        _picker = AddPicker.Show(kinds, below, kind =>
        {
            var config = kind == WidgetKind.Folder ? new WidgetConfig(Picture: DotArt.Random(Random.Shared)) : null;
            var (layout, _) = _layout.Add(kind, Area.W, Area.H, () => DesktopLayout.NewId(Random.Shared), config);
            Log.Info($"widget added: {kind}");
            Apply(layout);
        });
    }

    private void ClosePicker()
    {
        var picker = _picker;
        _picker = null;
        try { picker?.Close(); }
        catch (InvalidOperationException) { } // already closing
    }

    private FrameworkElement EditFrame(WidgetPlacement p, FrameworkElement card)
    {
        var index = _layout.IndexOf(p.Id);
        var chrome = new EditChrome(card, p, index >= 0 && _layout.Overlaps(index), _scale);
        var id = p.Id;
        chrome.Grab.MouseLeftButtonDown += (_, e) => { e.Handled = true; StartDrag(id, chrome); };
        chrome.Grab.MouseMove += (_, _) => DragMove();
        chrome.Grab.MouseLeftButtonUp += (_, e) => { e.Handled = true; EndDrag(); };
        chrome.Grab.LostMouseCapture += (_, _) => EndDrag();
        if (chrome.Chip is { } chip)
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var i = _layout.IndexOf(id);
                if (i >= 0) Apply(_layout.CycleSize(i)); // may overlap: the outline stays until it is moved
            };
        if (chrome.ShapeChip is { } shape)
            shape.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var i = _layout.IndexOf(id);
                if (i < 0) return;
                var config = _layout.Widgets[i].Config ?? new WidgetConfig();
                Apply(_layout.WithConfig(id, config with { Shape = config.Shape == "circle" ? "square" : "circle" }));
            };
        chrome.Close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_slots.TryGetValue(id, out var slot)) Safe(slot.Widget.Removed);
            Log.Info($"widget removed: {p.Kind}");
            Apply(_layout.Remove(id));
        };
        return chrome.Root;
    }

    private void StartDrag(string id, EditChrome chrome)
    {
        var i = _layout.IndexOf(id);
        if (i < 0 || !_slots.TryGetValue(id, out var slot) || DockWindowNative.GetCursorPos() is not { } cursor) return;
        _settle.Finish();
        _drag = (id, cursor, ScreenRect(_layout.Widgets[i]), chrome);
        slot.Host.Raise();
        chrome.Grab.CaptureMouse();
    }

    private (RectI Rect, DesktopLayout Layout, bool Moved)? DragTarget()
    {
        if (_drag is not { } d || DockWindowNative.GetCursorPos() is not { } c) return null;
        var i = _layout.IndexOf(d.Id);
        if (i < 0) return null;
        var dx = c.X - d.Cursor.X;
        var dy = c.Y - d.Cursor.Y;
        var rect = new RectI(d.Start.Left + dx, d.Start.Top + dy, d.Start.Right + dx, d.Start.Bottom + dy);
        var (x, y) = ToLayout(rect.Left, rect.Top);
        var (layout, moved) = _layout.TryMove(i, x, y, Area.W, Area.H);
        return (rect, layout, moved);
    }

    private void DragMove()
    {
        if (_drag is not { } d || DragTarget() is not { } t || !_slots.TryGetValue(d.Id, out var h)) return;
        h.Host.Place(t.Rect); // follows the pointer; the snapped spot is taken on release
        d.Chrome.ShowOverlap(!t.Moved);
    }

    private void EndDrag()
    {
        if (_drag is not { } d) return;
        var target = DragTarget();
        _drag = null;
        if (d.Chrome.Grab.IsMouseCaptured) d.Chrome.Grab.ReleaseMouseCapture();
        if (!_slots.TryGetValue(d.Id, out var h)) return;
        var i = _layout.IndexOf(d.Id);
        // A click without movement on a folder steps to its next picture (spec §5.4).
        if (i >= 0 && _layout.Widgets[i].Kind == WidgetKind.Folder && target is { } still
            && Math.Abs(still.Rect.Left - d.Start.Left) < 4 && Math.Abs(still.Rect.Top - d.Start.Top) < 4)
        {
            h.Host.Place(d.Start);
            var config = _layout.Widgets[i].Config ?? new WidgetConfig();
            Apply(_layout.WithConfig(d.Id, config with { Picture = DotArt.Next(config.Picture) }));
            return;
        }
        if (target is { Moved: true } t && i >= 0)
        {
            _layout = t.Layout;
            var saved = _layout;
            _update(s => s with { WidgetList = saved });
            var rect = ScreenRect(_layout.Widgets[i]);
            Animate(h.Host, t.Rect, rect);
            d.Chrome.ShowOverlap(_layout.Overlaps(i));
            Safe(() => h.Widget.Placed(rect, _monitor));
            return;
        }
        // Refused (overlap) or cancelled: back to where it was.
        Animate(h.Host, target?.Rect ?? d.Start, d.Start);
        if (i >= 0) d.Chrome.ShowOverlap(_layout.Overlaps(i));
    }

    // 180 ms ease-out slide of the widget window between two screen rectangles (a slide cut short ends at its spot).
    private void Animate(WidgetHost host, RectI from, RectI to) => _settle.Start(host.Place, from, to);

    private void OnContext(MouseButtonEventArgs e)
    {
        e.Handled = true;
        _popup?.CloseIfOpen();
        if (DockWindowNative.GetCursorPos() is not { } c) return;
        var popup = new Dock.DockPopup([Editing ? new Dock.PopupRow("Done editing", EndEdit) : new Dock.PopupRow("Edit widgets", BeginEdit)]);
        popup.Dismissed += () => { if (ReferenceEquals(_popup, popup)) _popup = null; };
        _popup = popup;
        popup.ShowAbove(0, c.X, c.Y - 4, _monitor);
    }



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
        try { DesktopAttach.Prepare(); }
        catch (Exception e) { Log.Error("desktop prepare failed", e); }
        Rebuild();
        if (Editing)
        {
            _backdrop?.Dispose();
            ShowBackdrop();
        }
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
        _dpiDebounce.Stop();
        _settle.Finish();
        _popup?.CloseIfOpen();
        ClosePicker();
        _backdrop?.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        foreach (var slot in _slots.Values) DisposeSlot(slot);
        _slots.Clear();
    }
}

/// <summary>A widget window's root: the card at the widget's size and the global scale, reused on every rebuild.</summary>
public static class WidgetRoots
{
    public static Grid Fill(Grid? existing, FrameworkElement card, WidgetSize size, double scale)
    {
        var root = existing ?? new Grid();
        root.Children.Clear();
        root.Children.Add(card);
        root.Width = size.PxW;
        root.Height = size.PxH;
        root.LayoutTransform = new ScaleTransform(scale, scale);
        return root;
    }
}

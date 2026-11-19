using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Widgets;

/// <summary>
/// One desktop widget's window: a per-pixel-transparent child <see cref="HwndSource"/> of the desktop host (Progman),
/// so it sits on the desktop layer (spec §7) and never shows in Alt+Tab (spike D.4). A small surface per widget keeps
/// redraws cheap. When Explorer restarts, the desktop and with it this child window are destroyed: <see cref="Lost"/>
/// fires and <see cref="Attach"/> builds a fresh window on the new desktop.
/// </summary>
public sealed class WidgetHost : IDisposable
{
    private readonly FrameworkElement _content;
    private HwndSource? _source;
    private nint _host;
    private RectI _rect;
    private bool _disposed;

    public WidgetHost(FrameworkElement content) => _content = content;

    /// <summary>The widget's window was destroyed with the desktop (Explorer restart).</summary>
    public event Action? Lost;

    public nint Hwnd => _source?.Handle ?? 0;

    public FrameworkElement Content => _content;

    /// <summary>True while the window exists and is a child of <paramref name="host"/>.</summary>
    public bool IsAttachedTo(nint host) => _source is not null && DesktopAttach.IsAlive(Hwnd) && DesktopAttach.ParentOf(Hwnd) == host;

    /// <summary>Creates the window under <paramref name="host"/> if needed and places it at <paramref name="screen"/> (physical px).</summary>
    public void Attach(nint host, RectI screen)
    {
        if (_disposed || host == 0) return;
        _rect = screen;
        if (!IsAttachedTo(host))
        {
            Destroy();
            _host = host;
            Create();
        }
        DesktopAttach.Place(Hwnd, _host, screen);
    }

    /// <summary>Moves / resizes (physical px) without changing the z-order.</summary>
    public void Place(RectI screen)
    {
        _rect = screen;
        if (_source is not null && DesktopAttach.IsAlive(Hwnd)) DesktopAttach.Place(Hwnd, _host, screen, raise: false);
    }

    /// <summary>Brings this widget above the other widgets (dragging in edit mode).</summary>
    public void Raise() { if (_source is not null) DesktopAttach.Place(Hwnd, _host, _rect, raise: true); }

    private void Create()
    {
        var r = DesktopAttach.ToHost(_host, _rect);
        var p = new HwndSourceParameters("NothingShell.Widget", Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top))
        {
            ParentWindow = _host,
            PositionX = r.Left,
            PositionY = r.Top,
            WindowStyle = DesktopAttach.ChildStyle(DesktopAttach.WsVisible | DesktopAttach.WsClipSiblings),
            ExtendedWindowStyle = DesktopAttach.ChildExStyle(0),
            UsesPerPixelTransparency = true,
        };
        _source = new HwndSource(p) { RootVisual = _content };
        _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        _source.Disposed += OnDisposed;
    }

    private void OnDisposed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _source)) return;
        _source = null;
        if (!_disposed) Lost?.Invoke();
    }

    private void Destroy()
    {
        var s = _source;
        _source = null;
        if (s is null) return;
        s.Disposed -= OnDisposed;
        s.RootVisual = null;
        s.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Destroy();
    }
}

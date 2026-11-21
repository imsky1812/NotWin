using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core.Notch;
using NothingShell.Core.Widgets;
using NothingShell.Media;
using NothingShell.Notch;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// Media widget (spec §6.5, §6.6, mockup <c>renderMedia</c>): the same system media session as the notch. 1×1 art
/// filling the card with play/pause; 2×1 art 104 wide, title, artist, a 20-dot bar and controls; 2×2 art 150 high,
/// title, artist, a 24-dot bar and controls. Progress ticks once a second only while playing. The dots are draggable
/// (user, 2026-11-21): press anywhere on them to jump there, drag to pick the spot, let go to play from it.
/// </summary>
public sealed class MediaWidget : IDesktopWidget
{
    private static readonly Brush Placeholder = MakePlaceholder();
    private readonly MediaSessionService _media;
    private readonly DispatcherTimer _progress = new() { Interval = TimeSpan.FromSeconds(1) };
    private Border? _host;
    private WidgetSize _size;
    private bool _seeking; // no redraw while the dots are being dragged

    public MediaWidget(MediaSessionService media)
    {
        _media = media;
        _media.Changed += OnChanged;
        _progress.Tick += (_, _) => Fill();
    }

    public WidgetKind Kind => WidgetKind.Media;

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border();
        if (size == new WidgetSize(1, 1)) _host.Margin = new Thickness(-16); // art fills the card (mockup: padding 0)
        Fill();
        Arm();
        return _host;
    }

    private void OnChanged(MediaSnapshot _)
    {
        Fill();
        Arm();
    }

    private void Arm()
    {
        if (_media.Current.Playing && _host is not null) _progress.Start();
        else _progress.Stop();
    }

    private void Fill()
    {
        if (_host is null || _seeking) return;
        var m = _media.Current;
        var has = m.Title is not null || m.SourceAppId is not null;
        _host.Child = _size switch
        {
            { W: 1 } => One(m),
            { H: 2 } => Big(m, has),
            _ => Wide(m, has),
        };
    }

    private Border Play(MediaSnapshot m)
    {
        var b = TileChrome.PlayButton(m.Playing, 36);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = _media.PlayPauseAsync(); };
        return b;
    }

    private FrameworkElement Controls(MediaSnapshot m, bool has)
    {
        var row = new DockPanel { Opacity = has ? 1 : 0.4 };
        var prev = Button("prev", () => _media.PreviousAsync());
        DockPanel.SetDock(prev, System.Windows.Controls.Dock.Left);
        var next = Button("next", () => _media.NextAsync());
        DockPanel.SetDock(next, System.Windows.Controls.Dock.Right);
        row.Children.Add(prev);
        row.Children.Add(next);
        var play = Play(m);
        play.HorizontalAlignment = HorizontalAlignment.Center;
        row.Children.Add(play);
        return row;
    }

    private static FrameworkElement Button(string icon, Func<Task<bool>> act)
    {
        var b = new Border { Background = Brushes.Transparent, Padding = new Thickness(4), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Child = TileChrome.Icon(icon, 18) };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = act(); };
        return b;
    }

    private FrameworkElement Bar(MediaSnapshot m, bool has, int count)
    {
        var now = MediaProgress.Position(m.Position, m.Duration, m.PositionAt, m.Playing, DateTimeOffset.Now);
        var dots = new DotBar
        {
            Count = count, Dot = 4, Gap = 3, On = TileChrome.Text, Off = TileChrome.Frozen(0x33, 0x33, 0x33), Accent = TileChrome.Red,
            Lit = has ? MediaProgress.Dots(now, m.Duration, count) : 0, HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (!has || m.Duration <= TimeSpan.Zero) return dots;
        // A taller see-through strip around the dots takes the pointer (the gaps between dots would not).
        var hit = new Border
        {
            Background = Brushes.Transparent, Padding = new Thickness(0, 5, 0, 5),
            Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Child = dots,
        };
        var duration = m.Duration;
        hit.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _seeking = true;
            hit.CaptureMouse();
            dots.Lit = MediaProgress.LitAt(e.GetPosition(dots).X, dots.ActualWidth, count);
        };
        hit.MouseMove += (_, e) =>
        {
            if (_seeking && hit.IsMouseCaptured) dots.Lit = MediaProgress.LitAt(e.GetPosition(dots).X, dots.ActualWidth, count);
        };
        hit.MouseLeftButtonUp += (_, e) =>
        {
            if (!_seeking) return;
            e.Handled = true;
            var target = MediaProgress.SeekTarget(e.GetPosition(dots).X, dots.ActualWidth, duration);
            _seeking = false;
            hit.ReleaseMouseCapture();
            if (target is { } t) _ = _media.SeekAsync(t);
        };
        hit.LostMouseCapture += (_, _) =>
        {
            if (!_seeking) return;
            _seeking = false; // cancelled (another window took the mouse): back to the real position
            Fill();
        };
        return hit;
    }

    private static Brush ArtBrush(MediaSnapshot m) => m.Art is null ? Placeholder : new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };

    private FrameworkElement One(MediaSnapshot m)
    {
        var play = Play(m);
        play.HorizontalAlignment = HorizontalAlignment.Right;
        play.VerticalAlignment = VerticalAlignment.Bottom;
        play.Margin = new Thickness(10);
        return new Grid { Children = { new Border { Background = ArtBrush(m), CornerRadius = new CornerRadius(24.5) }, play } };
    }

    private FrameworkElement Wide(MediaSnapshot m, bool has)
    {
        var panel = new DockPanel();
        var art = new Border { Width = 104, CornerRadius = new CornerRadius(14), Background = ArtBrush(m), Margin = new Thickness(0, 0, 14, 0) };
        DockPanel.SetDock(art, System.Windows.Controls.Dock.Left);
        panel.Children.Add(art);
        var right = new DockPanel();
        var controls = Controls(m, has);
        DockPanel.SetDock(controls, System.Windows.Controls.Dock.Bottom);
        right.Children.Add(controls);
        var bar = Bar(m, has, 20);
        bar.Margin = bar is Border ? new Thickness(0, -5, 0, 3) : new Thickness(0, 0, 0, 8); // the touch strip adds 5 above and below
        DockPanel.SetDock(bar, System.Windows.Controls.Dock.Bottom);
        right.Children.Add(bar);
        right.Children.Add(Titles(m, has));
        panel.Children.Add(right);
        return panel;
    }

    private FrameworkElement Big(MediaSnapshot m, bool has)
    {
        var panel = new DockPanel();
        var art = new Border { Height = 150, CornerRadius = new CornerRadius(14), Background = ArtBrush(m), Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(art, System.Windows.Controls.Dock.Top);
        panel.Children.Add(art);
        var controls = Controls(m, has);
        DockPanel.SetDock(controls, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(controls);
        var bar = Bar(m, has, 24);
        bar.Margin = bar is Border ? new Thickness(0, -5, 0, 5) : new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(bar, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(bar);
        panel.Children.Add(Titles(m, has));
        return panel;
    }

    private static FrameworkElement Titles(MediaSnapshot m, bool has)
    {
        var stack = new StackPanel();
        stack.Children.Add(TileChrome.Body(has ? m.Title ?? "" : "Nothing playing", 17, FontWeights.SemiBold, has ? null : TileChrome.Dim));
        if (has && !string.IsNullOrEmpty(m.Artist)) stack.Children.Add(TileChrome.Body(m.Artist, 12, FontWeights.Normal, TileChrome.Dim));
        return stack;
    }

    private static Brush MakePlaceholder()
    {
        var b = new LinearGradientBrush(Color.FromRgb(0x4A, 0x4A, 0x4A), Color.FromRgb(0x11, 0x11, 0x11), 45);
        b.Freeze();
        return b;
    }

    public void Dispose()
    {
        _progress.Stop();
        _media.Changed -= OnChanged;
    }
}

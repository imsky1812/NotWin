using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core.Notch;
using NothingShell.Media;

namespace NothingShell.Notch.Dashboard.Tiles;

/// <summary>
/// Media tile (mockup <c>NR.media</c>, spec §6.4): the system's current media session. 1×1 art with play/pause;
/// 2×1 art 40, title, artist, play; 2×2 art 54, source app and m:ss / m:ss, title, artist, a 20-dot progress bar and
/// prev / play / next. Progress ticks once a second only while the notch is open and the track is playing.
/// Titles are never logged.
/// </summary>
public sealed class MediaTile : ITileView
{
    private static readonly Brush Placeholder = MakePlaceholder();
    private readonly MediaSessionService _media;
    private readonly Func<string?, string> _appName;
    private readonly DispatcherTimer _progress;
    private TileSize _size = TileSize.Big;
    private Border? _host;
    private bool _open;

    public MediaTile(MediaSessionService media, Func<string?, string> appName)
    {
        _media = media;
        _appName = appName;
        _media.Changed += OnChanged;
        _progress = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progress.Tick += (_, _) => Fill();
    }

    public TileKind Kind => TileKind.Media;

    public FrameworkElement Build(TileSize size)
    {
        _size = size;
        _host = TileChrome.Tile(new Grid());
        if (size == TileSize.One) _host.Padding = new Thickness(0);
        Fill();
        return _host;
    }

    private void OnChanged(MediaSnapshot _)
    {
        if (!_open) return;
        Fill();
        Arm();
    }

    private void Arm()
    {
        var run = _open && _size == TileSize.Big && _media.Current.Playing;
        if (run && !_progress.IsEnabled) _progress.Start();
        else if (!run) _progress.Stop();
    }

    // Rebuilds the tile's content from the current snapshot (cheap: a handful of elements).
    private bool _seeking; // no redraw while the dots are being dragged

    private void Fill()
    {
        if (_host is null || _seeking) return;
        var m = _media.Current;
        var has = m.Title is not null || m.SourceAppId is not null;
        _host.Child = _size switch
        {
            TileSize.One => One(m),
            TileSize.Wide => Wide(m, has),
            _ => Big(m, has),
        };
    }

    private FrameworkElement One(MediaSnapshot m)
    {
        var grid = new Grid();
        grid.Children.Add(new Border { Background = ArtBrush(m), CornerRadius = new CornerRadius(21) }); // a Border's clip is square
        var play = Play(m, 28);
        play.HorizontalAlignment = HorizontalAlignment.Right;
        play.VerticalAlignment = VerticalAlignment.Bottom;
        play.Margin = new Thickness(8);
        grid.Children.Add(play);
        return grid;
    }

    private FrameworkElement Wide(MediaSnapshot m, bool has)
    {
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var art = Art(m, 40);
        art.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(art, System.Windows.Controls.Dock.Left);
        row.Children.Add(art);
        var play = Play(m, 30);
        play.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(play, System.Windows.Controls.Dock.Right);
        row.Children.Add(play);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = TileChrome.Body(has ? m.Title ?? "" : "Nothing playing", 12, FontWeights.SemiBold, has ? null : TileChrome.Dim);
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxHeight = 32;
        text.Children.Add(title);
        if (has && !string.IsNullOrEmpty(m.Artist)) text.Children.Add(TileChrome.Label(m.Artist));
        row.Children.Add(text);
        return row;
    }

    private FrameworkElement Big(MediaSnapshot m, bool has)
    {
        var panel = new DockPanel();
        var top = new DockPanel();
        var art = Art(m, 54);
        art.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(art, System.Windows.Controls.Dock.Left);
        top.Children.Add(art);
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        meta.Children.Add(TileChrome.Label(has ? _appName(m.SourceAppId) : "Media"));
        var now = MediaProgress.Position(m.Position, m.Duration, m.PositionAt, m.Playing, DateTimeOffset.Now);
        if (has && m.Duration > TimeSpan.Zero)
        {
            var clock = TileChrome.Label($"{MediaProgress.Clock(now)} / {MediaProgress.Clock(m.Duration)}");
            clock.Margin = new Thickness(0, 4, 0, 0);
            meta.Children.Add(clock);
        }
        top.Children.Add(meta);
        DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
        panel.Children.Add(top);

        var title = TileChrome.Body(has ? m.Title ?? "" : "Nothing playing", 14, FontWeights.SemiBold, has ? null : TileChrome.Dim);
        title.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(title, System.Windows.Controls.Dock.Top);
        panel.Children.Add(title);
        if (has && !string.IsNullOrEmpty(m.Artist))
        {
            var artist = TileChrome.Body(m.Artist, 11, FontWeights.Normal, TileChrome.Dim);
            DockPanel.SetDock(artist, System.Windows.Controls.Dock.Top);
            panel.Children.Add(artist);
        }

        var controls = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Opacity = has ? 1 : 0.4 };
        var prev = Button("prev", () => _media.PreviousAsync());
        DockPanel.SetDock(prev, System.Windows.Controls.Dock.Left);
        var next = Button("next", () => _media.NextAsync());
        DockPanel.SetDock(next, System.Windows.Controls.Dock.Right);
        controls.Children.Add(prev);
        controls.Children.Add(next);
        var play = Play(m, 34);
        play.HorizontalAlignment = HorizontalAlignment.Center;
        controls.Children.Add(play);
        DockPanel.SetDock(controls, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(controls);

        var bar = new DotBar
        {
            Count = 20, Dot = 4, Gap = 3, On = TileChrome.Text, Off = TileChrome.Frozen(0x33, 0x33, 0x33), Accent = TileChrome.Red,
            Lit = has ? MediaProgress.Dots(now, m.Duration, 20) : 0, VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        // Draggable like the desktop widget's (Plan 10): jump anywhere in the song.
        var seekable = has ? SeekBar.Wrap(bar, 20, m.Duration, _media.SeekAsync, on => { _seeking = on; if (!on) Fill(); }) : bar;
        if (seekable is Border strip) strip.Margin = new Thickness(0, 0, 0, -5); // the strip's extra 5 px below stay inside the tile
        panel.Children.Add(seekable);
        return panel;
    }

    private Border Play(MediaSnapshot m, double size)
    {
        var b = TileChrome.PlayButton(m.Playing, size);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = _media.PlayPauseAsync(); };
        return b;
    }

    private static FrameworkElement Button(string icon, Func<Task<bool>> act)
    {
        var holder = new Border { Background = Brushes.Transparent, Padding = new Thickness(4), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        holder.Child = TileChrome.Icon(icon, 16);
        holder.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = act(); };
        return holder;
    }

    private static Border Art(MediaSnapshot m, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(12), Background = ArtBrush(m),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Brush ArtBrush(MediaSnapshot m) =>
        m.Art is null ? Placeholder : new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };

    private static Brush MakePlaceholder()
    {
        var b = new LinearGradientBrush(Color.FromRgb(0x4A, 0x4A, 0x4A), Color.FromRgb(0x11, 0x11, 0x11), 45);
        b.Freeze();
        return b;
    }

    public void Opened()
    {
        _open = true;
        Fill();
        Arm();
    }

    public void Closed()
    {
        _open = false;
        _progress.Stop();
    }

    public void Dispose()
    {
        _progress.Stop();
        _media.Changed -= OnChanged;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Core.Widgets;
using NothingShell.Native;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// Photo frame (Plan 5b design §5.3): inside the normal black card, a slideshow of the user's own photos (copies made
/// by <see cref="PhotoStore"/>), square (corners radius 14) or circle. Empty: a dashed outline, a dot camera and
/// "ADD PHOTOS" that opens the file picker. With photos: every 60 s a 600 ms crossfade to the next (only the current
/// and next photo are decoded, at the widget's on-screen width); a click opens the manage popup (thumbnails with ✕,
/// ＋ to add more, 24 at most). Paused while a full-screen app is in front.
/// </summary>
public sealed class PhotoFrameWidget : IDesktopWidget
{
    private static readonly Brush DashBrush = TileChrome.FrozenArgb(0x66, 0xF2, 0xF2, 0xF2);
    private readonly string _id;
    private readonly PhotoStore _store;
    private readonly Action<WidgetConfig> _save;
    private readonly DispatcherTimer _next = new() { Interval = TimeSpan.FromSeconds(60) };
    private WidgetConfig _config;
    private Grid? _host;
    private Image? _front, _back;
    private int _index;
    private int _decodeWidth = 400;
    private bool _paused;
    private RectI _screen;
    private PhotoManagePopup? _popup;

    public PhotoFrameWidget(string id, WidgetConfig? config, PhotoStore store, Action<WidgetConfig> save)
    {
        _id = id;
        _store = store;
        _save = save;
        _config = config ?? new WidgetConfig();
        _next.Tick += (_, _) => Advance();
    }

    public WidgetKind Kind => WidgetKind.Photo;

    private IReadOnlyList<string> Photos => _config.Photos ?? [];
    private bool Circle => _config.Shape == "circle";

    public void Configure(WidgetConfig? config) => _config = config ?? new WidgetConfig();

    public FrameworkElement Build(WidgetSize size)
    {
        _host = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _host.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnClick(); };
        Fill();
        return _host;
    }

    public void Placed(RectI screen, RectI monitor)
    {
        _screen = screen;
        var width = Math.Max(64, screen.Right - screen.Left);
        if (Math.Abs(width - _decodeWidth) > 8)
        {
            _decodeWidth = width;
            Fill();
        }
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        UpdateTimer();
    }

    public void Removed() => _store.Delete(_id);

    private void Fill()
    {
        if (_host is null) return;
        _host.Children.Clear();
        _front = _back = null;
        if (Photos.Count == 0)
        {
            _host.Children.Add(EmptyState());
            UpdateTimer();
            return;
        }
        _index = Math.Clamp(_index, 0, Photos.Count - 1);
        _back = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        _front = new Image { Stretch = Stretch.UniformToFill, Source = Load(Photos[_index]) };
        var photos = new Grid { Children = { _front, _back } };
        photos.SizeChanged += (_, _) => photos.Clip = ClipFor(photos.ActualWidth, photos.ActualHeight);
        _host.Children.Add(photos);
        UpdateTimer();
    }

    private Geometry ClipFor(double w, double h)
    {
        if (!Circle) return new RectangleGeometry(new Rect(0, 0, w, h), 14, 14);
        var d = Math.Min(w, h);
        return new EllipseGeometry(new Point(w / 2, h / 2), d / 2, d / 2);
    }

    private FrameworkElement EmptyState()
    {
        var outline = new Rectangle
        {
            Stroke = DashBrush, StrokeThickness = 1.5, StrokeDashArray = [4, 4], RadiusX = 14, RadiusY = 14,
        };
        var label = TileChrome.Label("Add photos");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, 10, 0, 0);
        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { new DotArtView { Rows = DotArt.Rows("camera"), Dot = 5, Gap = 2, HorizontalAlignment = HorizontalAlignment.Center }, label },
        };
        return new Grid { Children = { outline, center } };
    }

    private ImageSource? Load(string name)
    {
        try
        {
            var path = _store.PhotoPath(_id, name);
            if (!File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad; // no file lock, and the file can be deleted later
            image.DecodePixelWidth = _decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e)
        {
            Log.Error("a frame photo could not be shown", e);
            return null;
        }
    }

    private void UpdateTimer()
    {
        if (Photos.Count > 1 && !_paused && _host is not null) _next.Start();
        else _next.Stop();
    }

    // 600 ms crossfade: the next photo fades in on top, then becomes the front.
    private void Advance()
    {
        if (_front is null || _back is null || Photos.Count < 2) return;
        _index = PhotoList.Next(_index, Photos.Count);
        var back = _back;
        var front = _front;
        back.Source = Load(Photos[_index]);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600));
        fade.Completed += (_, _) =>
        {
            front.Source = back.Source;
            back.BeginAnimation(UIElement.OpacityProperty, null);
            back.Opacity = 0;
            back.Source = null; // only the current photo stays decoded
        };
        back.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void OnClick()
    {
        if (Photos.Count == 0) { _ = PickAndAddAsync(); return; }
        _popup?.CloseIfOpen();
        _popup = PhotoManagePopup.Show(_store, _id, Photos, _screen, remove: RemovePhoto, add: () => _ = PickAndAddAsync());
    }

    private async Task PickAndAddAsync()
    {
        try
        {
            var room = PhotoList.Max - Photos.Count;
            if (room <= 0) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true, Title = "Add photos",
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.heic;*.heif|All files|*.*",
            };
            if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;
            var (added, skipped) = await _store.ImportAsync(_id, dialog.FileNames, room);
            Log.Info($"photo frame: {added.Count} added, {skipped} skipped");
            if (added.Count == 0) return;
            _config = _config with { Photos = [.. Photos, .. added] };
            _save(_config);
            Fill();
            _popup?.Refresh(Photos);
        }
        catch (Exception e) { Log.Error("adding photos failed", e); }
    }

    private void RemovePhoto(string name)
    {
        _store.Remove(_id, name);
        _config = _config with { Photos = Photos.Where(p => p != name).ToList() };
        _save(_config);
        Fill();
        if (Photos.Count == 0) _popup?.CloseIfOpen();
        else _popup?.Refresh(Photos);
    }

    public void Dispose()
    {
        _next.Stop();
        _popup?.CloseIfOpen();
    }
}

/// <summary>The photo frame's manage popup: a grid of thumbnails (✕ removes one) and a ＋ tile; closes on a click outside or Esc.</summary>
public sealed class PhotoManagePopup : Window
{
    private readonly PhotoStore _store;
    private readonly string _frameId;
    private readonly Action<string> _remove;
    private readonly Action _add;
    private readonly WrapPanel _grid = new() { MaxWidth = 4 * 84 };
    private readonly TextBlock _note = TileChrome.Label("");
    private bool _closed;

    private PhotoManagePopup(PhotoStore store, string frameId, Action<string> remove, Action add)
    {
        _store = store;
        _frameId = frameId;
        _remove = remove;
        _add = add;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        _note.Margin = new Thickness(6, 8, 0, 0);
        Content = new Border
        {
            Background = WidgetCard.CardBrush, BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(10),
            Child = new StackPanel { Children = { _grid, _note } },
        };
        Deactivated += (_, _) => CloseIfOpen();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseIfOpen(); };
    }

    public static PhotoManagePopup Show(PhotoStore store, string frameId, IReadOnlyList<string> photos, RectI widget, Action<string> remove, Action add)
    {
        var popup = new PhotoManagePopup(store, frameId, remove, add) { Opacity = 0, Left = widget.Left, Top = widget.Top };
        popup.Refresh(photos);
        popup.Show();
        popup.UpdateLayout();
        var scale = VisualTreeHelper.GetDpi(popup).DpiScaleX;
        var w = (int)Math.Ceiling(popup.ActualWidth * scale);
        var h = (int)Math.Ceiling(popup.ActualHeight * scale);
        var gap = (int)Math.Round(10 * scale);
        // Right of the widget; on its left when there is no room.
        var (mon, _) = DockWindowNative.GetPrimaryMonitor();
        var left = widget.Right + gap + w <= mon.Right ? widget.Right + gap : Math.Max(mon.Left, widget.Left - gap - w);
        var top = Math.Clamp(widget.Top, mon.Top, Math.Max(mon.Top, mon.Bottom - h));
        DockWindowNative.PlaceTopmost(new WindowInteropHelper(popup).Handle, new RectI(left, top, left + w, top + h));
        popup.Opacity = 1;
        popup.Activate();
        return popup;
    }

    public void Refresh(IReadOnlyList<string> photos)
    {
        _grid.Children.Clear();
        foreach (var name in photos) _grid.Children.Add(Thumb(name));
        if (photos.Count < PhotoList.Max) _grid.Children.Add(AddTile());
        _note.Text = photos.Count >= PhotoList.Max ? $"{PhotoList.Max} MAX" : "";
        _note.Visibility = photos.Count >= PhotoList.Max ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement Thumb(string name)
    {
        ImageSource? source = null;
        try
        {
            var path = _store.ThumbPath(_frameId, name);
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 144;
                bmp.EndInit();
                bmp.Freeze();
                source = bmp;
            }
        }
        catch (Exception e) { Log.Error("a frame thumbnail could not be shown", e); }
        var image = new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(10), ClipToBounds = true,
            Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill },
        };
        var close = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = TileChrome.Frozen(0x2A, 0x2A, 0x2A),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 3, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "✕", FontSize = 11, Foreground = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Guard(() => _remove(name)); };
        return new Grid { Margin = new Thickness(6), Children = { image, close } };
    }

    private FrameworkElement AddTile()
    {
        var tile = new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(10), Margin = new Thickness(6), Cursor = Cursors.Hand,
            BorderBrush = TileChrome.FrozenArgb(0x66, 0xF2, 0xF2, 0xF2), BorderThickness = new Thickness(1.5), Background = Brushes.Transparent,
            Child = new TextBlock { Text = "＋", FontSize = 26, Foreground = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; Guard(_add); };
        return tile;
    }

    private static void Guard(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Error("photo frame popup action failed", e); }
    }

    public void CloseIfOpen()
    {
        if (_closed) return;
        _closed = true;
        Dispatcher.BeginInvoke(() => { try { Close(); } catch (InvalidOperationException) { } });
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Core.Forecast;
using NothingShell.Core.Wallpapers;
using NothingShell.Forecast;
using NothingShell.Native;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Settings;

/// <summary>
/// Nothing Shell's settings (spec §6.7, Plan 4 contents): weather city, start with Windows, keep dock visible, and
/// Quit. Opened from the notch's Settings tile, a right-click on the open notch, and the dock's start-button menu.
/// One instance; Esc or ✕ closes. Widget scale and wallpaper arrive with Plan 5.
/// </summary>
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? s_open;
    private readonly Func<ShellSettings> _settings;
    private readonly Action<bool> _setStart, _setKeep;
    private readonly Action<WeatherPlace> _saveWeather;
    private readonly WeatherService? _weather;
    private readonly TextBox _city;
    private readonly TextBlock _cityStatus;
    private readonly Border _startToggle = new(), _keepToggle = new(), _drainToggle = new(), _previewToggle = new();
    private DockEffects? _effects;

    /// <summary>Plan 5 widget settings: widget size (S/M/L), the wallpapers folder, and entering widget edit mode.</summary>
    /// <param name="GeneratedDir">Where "✦ New" wallpapers are saved (the user's data folder).</param>
    /// <summary>Plan 7 switches: dot drain when minimizing, window previews in the dock.</summary>
    public sealed record DockEffects(Action<bool> SetDotDrain, Action<bool> SetPreviews);

    public sealed record WidgetOptions(Action<string> SetScale, string? WallpaperDir, Action EditWidgets, string? GeneratedDir = null);

    private SettingsWindow(Func<ShellSettings> settings, Action<bool> setStart, Action<bool> setKeep, WeatherService? weather,
        Action<WeatherPlace> saveWeather, Action quit, WidgetOptions? widgets, DockEffects? effects = null)
    {
        _effects = effects;
        _settings = settings;
        _setStart = setStart;
        _setKeep = setKeep;
        _weather = weather;
        _saveWeather = saveWeather;
        Title = "Nothing Shell Settings";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = TileChrome.Font;
        Foreground = TileChrome.Text;

        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        var close = new TextBlock { Text = "✕", FontSize = 13, Foreground = TileChrome.Dim, Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        close.MouseLeftButtonUp += (_, _) => Close();
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
        head.Children.Add(close);
        head.Children.Add(TileChrome.Body("Settings", 20, FontWeights.SemiBold));

        // Weather city (spec §6.5: a city the user types in Settings).
        stack.Children.Add(Section("Weather"));
        _city = new TextBox
        {
            Background = Brushes.Transparent, Foreground = TileChrome.Text, CaretBrush = TileChrome.Text, // the rounded Border paints #222
            BorderThickness = new Thickness(0), FontFamily = TileChrome.Font, FontSize = 13, Padding = new Thickness(10, 8, 10, 8),
            Text = settings().Weather?.Name ?? "",
        };
        _city.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = SetCityAsync(); } };
        var set = Pill("Set");
        set.MouseLeftButtonUp += (_, _) => _ = SetCityAsync();
        var cityRow = new DockPanel();
        set.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(set, System.Windows.Controls.Dock.Right);
        cityRow.Children.Add(set);
        cityRow.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = TileChrome.Frozen(0x22, 0x22, 0x22), Child = _city, ClipToBounds = true });
        stack.Children.Add(cityRow);
        _cityStatus = TileChrome.Label(PlaceText(settings().Weather) ?? "Type a city and press Enter");
        _cityStatus.Margin = new Thickness(2, 8, 0, 18);
        stack.Children.Add(_cityStatus);

        if (widgets is not null) AddWidgetSections(stack, widgets);

        stack.Children.Add(Section("Shell"));
        stack.Children.Add(ToggleRow("Start with Windows", _startToggle, () => _settings().StartWithWindows, _setStart));
        stack.Children.Add(ToggleRow("Keep dock visible", _keepToggle, () => _settings().KeepDockVisible, _setKeep));
        if (_effects is { } fx)
        {
            stack.Children.Add(ToggleRow("Dot drain when minimizing", _drainToggle, () => _settings().DotDrain, fx.SetDotDrain));
            var hint = TileChrome.Label("Turn off Windows' own minimize animation for the best look");
            hint.Margin = new Thickness(0, -4, 0, 10);
            hint.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(hint);
            stack.Children.Add(ToggleRow("Window previews in the dock", _previewToggle, () => _settings().WindowPreviews, fx.SetPreviews));
        }

        var quitText = TileChrome.Body("Quit Nothing Shell", 13, FontWeights.SemiBold, TileChrome.Red);
        var quitRow = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(0, 14, 0, 0), Child = quitText };
        quitRow.MouseLeftButtonUp += (_, _) => { Close(); quit(); };
        stack.Children.Add(quitRow);

        Content = new Border
        {
            Background = (Brush)Application.Current.FindResource("Brush.GlassDense"),
            BorderBrush = (Brush)Application.Current.FindResource("Brush.Line"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(28), Padding = new Thickness(24),
            // The header (with ✕) stays put; everything below scrolls once the window reaches the screen's height
            // (user report 2026-11-20: many wallpapers pushed the window and its ✕ above the screen).
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    head,
                    new ScrollViewer
                    {
                        Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly,
                    },
                },
            },
        };
        SizeChanged += (_, _) => Fit();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) => s_open = null;
        Loaded += (_, _) =>
        {
            Fit();
            ForegroundNative.Activate(new WindowInteropHelper(this).Handle);
            Activate();
        };
    }

    // Never taller than the screen's work area, and kept inside it (re-centred when the content changes size).
    private void Fit()
    {
        var (top, maxHeight) = SettingsLayout.Fit(ActualHeight, SystemParameters.WorkArea);
        if (Math.Abs(MaxHeight - maxHeight) > 0.5) MaxHeight = maxHeight;
        if (Math.Abs(Top - top) > 0.5) Top = top;
    }

    /// <summary>Shows the settings window, or brings the open one to the front.</summary>
    public static void ShowOrActivate(Func<ShellSettings> settings, Action<bool> setStart, Action<bool> setKeep,
        WeatherService? weather, Action<WeatherPlace> saveWeather, Action quit, WidgetOptions? widgets = null, DockEffects? effects = null)
    {
        if (s_open is not null)
        {
            s_open.Activate();
            ForegroundNative.Activate(new WindowInteropHelper(s_open).Handle);
            return;
        }
        s_open = new SettingsWindow(settings, setStart, setKeep, weather, saveWeather, quit, widgets, effects);
        s_open.Show();
    }

    // Widgets: size S / M / L (applies live) and "Edit widgets"; Wallpaper: the four Nothing wallpapers (spec §5, §6.7).
    // The wallpaper changes only when the user clicks one.
    private void AddWidgetSections(StackPanel stack, WidgetOptions w)
    {
        stack.Children.Add(Section("Widgets"));
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        void PaintSizes()
        {
            sizes.Children.Clear();
            foreach (var (key, label) in new[] { ("S", "Small"), ("M", "Medium"), ("L", "Large") })
            {
                var on = (_settings().WidgetScale ?? "M") == key;
                var pill = new Border
                {
                    Background = on ? TileChrome.Text : TileChrome.Frozen(0x22, 0x22, 0x22), CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = label, FontFamily = TileChrome.Font, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = on ? TileChrome.Dark : TileChrome.Text },
                };
                var k = key;
                pill.MouseLeftButtonUp += (_, _) => { w.SetScale(k); PaintSizes(); };
                sizes.Children.Add(pill);
            }
        }
        PaintSizes();
        stack.Children.Add(sizes);
        var edit = new DockPanel { Margin = new Thickness(0, 0, 0, 18), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var editArrow = TileChrome.Label("›");
        editArrow.FontSize = 14;
        DockPanel.SetDock(editArrow, System.Windows.Controls.Dock.Right);
        edit.Children.Add(editArrow);
        edit.Children.Add(TileChrome.Body("Edit widgets", 13, FontWeights.Normal));
        edit.MouseLeftButtonUp += (_, _) => { Close(); w.EditWidgets(); };
        stack.Children.Add(edit);

        var bundledDir = w.WallpaperDir is { } d && System.IO.Directory.Exists(d) ? d : null;
        if (bundledDir is null && w.GeneratedDir is null) return;
        stack.Children.Add(Section("Wallpaper"));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 18) };
        stack.Children.Add(grid);

        // ✦ New first (user, 2026-11-20: a fresh Nothing-style wallpaper whenever they like), then the ones made
        // before (newest first), then the four bundled ones. A click on New makes one and applies it.
        void Fill()
        {
            grid.Children.Clear();
            var current = WallpaperNative.Current();
            if (w.GeneratedDir is { } gen) grid.Children.Add(NewTile(gen));
            var files = new List<(string Full, string Preview, string Tip)>();
            if (w.GeneratedDir is { } g)
                files.AddRange(WallpaperMaker.List(g).Select(f => (f, WallpaperMaker.PreviewOf(f), "made by you")));
            var mine = files.Select(f => f.Full).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (bundledDir is not null)
                foreach (var name in new[] { "nothing_halftone_light", "nothing_halftone_dark", "nothing_glyph_dark", "nothing_grid_light" })
                    files.Add((System.IO.Path.Combine(bundledDir, name + ".png"), System.IO.Path.Combine(bundledDir, name + "_preview.jpg"),
                        name.Replace("nothing_", "").Replace('_', ' ')));
            foreach (var (full, preview, tip) in files)
            {
                if (!System.IO.File.Exists(full)) continue;
                var selected = string.Equals(current, full, StringComparison.OrdinalIgnoreCase);
                var thumb = new Border
                {
                    Height = 96, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                    BorderBrush = selected ? TileChrome.Text : Brushes.Transparent, BorderThickness = new Thickness(2),
                    Background = System.IO.File.Exists(preview)
                        ? new ImageBrush(Thumb(preview)) { Stretch = Stretch.UniformToFill }
                        : TileChrome.Frozen(0x33, 0x33, 0x33),
                    ToolTip = tip,
                };
                var path = full;
                if (mine.Contains(full) && w.GeneratedDir is { } genDir)
                {
                    // ✕ (on hover) deletes a wallpaper you made; the bundled four cannot be deleted.
                    var delete = new Border
                    {
                        Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(6),
                        Background = TileChrome.FrozenArgb(0xCC, 0x11, 0x11, 0x11), Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                        Visibility = Visibility.Hidden, ToolTip = "Delete",
                        Child = new TextBlock { Text = "✕", FontSize = 11, Foreground = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    };
                    delete.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true; // not a click on the wallpaper
                        var ok = WallpaperMaker.Delete(genDir, path);
                        Log.Info($"wallpaper deleted ok={ok}");
                        Fill();
                    };
                    thumb.Child = new Grid { Children = { delete } };
                    thumb.MouseEnter += (_, _) => delete.Visibility = Visibility.Visible;
                    thumb.MouseLeave += (_, _) => delete.Visibility = Visibility.Hidden;
                }
                thumb.MouseLeftButtonUp += (_, _) =>
                {
                    var ok = WallpaperNative.Apply(path);
                    Log.Info($"wallpaper applied ok={ok}");
                    foreach (var t in grid.Children.OfType<Border>()) t.BorderBrush = Brushes.Transparent;
                    if (ok) thumb.BorderBrush = TileChrome.Text;
                };
                grid.Children.Add(thumb);
            }
        }

        Border NewTile(string gen)
        {
            var label = new TextBlock
            {
                Text = "✦ New", FontFamily = TileChrome.Font, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = TileChrome.Text,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var tile = new Border
            {
                Height = 96, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                Background = TileChrome.Frozen(0x22, 0x22, 0x22), BorderBrush = TileChrome.FrozenArgb(0x55, 0xF2, 0xF2, 0xF2),
                BorderThickness = new Thickness(1.5), ToolTip = "Make a new Nothing-style wallpaper", Child = label,
            };
            var busy = false;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                if (busy) return;
                busy = true;
                label.Text = "MAKING…";
                // Let "MAKING…" paint first; drawing a full-screen wallpaper takes about a second.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    try
                    {
                        var (monitor, _) = DockWindowNative.GetPrimaryMonitor();
                        var recipe = WallpaperArt.Random(System.Random.Shared);
                        var png = WallpaperMaker.Make(recipe, Math.Max(640, monitor.Right - monitor.Left), Math.Max(360, monitor.Bottom - monitor.Top), gen);
                        var ok = WallpaperNative.Apply(png);
                        Log.Info($"wallpaper made ({recipe.Style}) applied ok={ok}");
                        WallpaperMaker.Prune(gen, keep: png);
                    }
                    catch (Exception e) { Log.Error("making a wallpaper failed", e); }
                    Fill();
                });
            };
            return tile;
        }

        Fill();
    }

    private static ImageSource Thumb(string path)
    {
        var b = new System.Windows.Media.Imaging.BitmapImage();
        b.BeginInit();
        b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        b.DecodePixelWidth = 400; // ~190 DIP thumbnails at up to 200 %
        b.UriSource = new Uri(path);
        b.EndInit();
        b.Freeze();
        return b;
    }

    private static string? PlaceText(WeatherPlace? p) =>
        p is null ? null : string.IsNullOrEmpty(p.Country) ? p.Name : $"{p.Name}, {p.Country}";

    private async Task SetCityAsync()
    {
        if (_weather is null) return;
        var city = _city.Text.Trim();
        if (city.Length == 0) return;
        _cityStatus.Text = "SEARCHING…";
        var (place, offline) = await _weather.FindAsync(city);
        if (place is null)
        {
            _cityStatus.Text = offline ? "OFFLINE" : "CITY NOT FOUND";
            return;
        }
        _saveWeather(place);
        _city.Text = place.Name;
        _cityStatus.Text = PlaceText(place)!.ToUpperInvariant();
    }

    private static TextBlock Section(string text)
    {
        var t = TileChrome.Label(text);
        t.FontSize = 10;
        t.Margin = new Thickness(2, 0, 0, 8);
        return t;
    }

    private static Border Pill(string text) => new()
    {
        Background = TileChrome.Text, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 8, 16, 8), Cursor = Cursors.Hand,
        Child = new TextBlock { Text = text, FontFamily = TileChrome.Font, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TileChrome.Dark },
    };

    private static DockPanel ToggleRow(string label, Border host, Func<bool> get, Action<bool> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        void Paint() => host.Child = TileChrome.Toggle(get());
        Paint();
        DockPanel.SetDock(host, System.Windows.Controls.Dock.Right);
        row.Children.Add(host);
        var text = TileChrome.Body(label, 13, FontWeights.Normal);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        row.MouseLeftButtonUp += (_, _) => { set(!get()); Paint(); };
        return row;
    }
}

/// <summary>Where the settings window sits: never taller than the work area minus a margin, kept inside it.</summary>
public static class SettingsLayout
{
    public const double Margin = 24;

    public static (double Top, double MaxHeight) Fit(double actualHeight, Rect workArea)
    {
        var maxHeight = Math.Max(200, workArea.Height - 2 * Margin);
        var height = Math.Min(actualHeight, maxHeight);
        var top = workArea.Top + (workArea.Height - height) / 2;
        return (Math.Max(workArea.Top + Margin, top), maxHeight);
    }
}

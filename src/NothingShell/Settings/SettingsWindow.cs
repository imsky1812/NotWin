using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Core.Forecast;
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
    private readonly Border _startToggle = new(), _keepToggle = new();

    /// <summary>Plan 5 widget settings: widget size (S/M/L), the wallpapers folder, and entering widget edit mode.</summary>
    public sealed record WidgetOptions(Action<string> SetScale, string? WallpaperDir, Action EditWidgets);

    private SettingsWindow(Func<ShellSettings> settings, Action<bool> setStart, Action<bool> setKeep, WeatherService? weather,
        Action<WeatherPlace> saveWeather, Action quit, WidgetOptions? widgets)
    {
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
        var close = new TextBlock { Text = "✕", FontSize = 13, Foreground = TileChrome.Dim, Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        close.MouseLeftButtonUp += (_, _) => Close();
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
        head.Children.Add(close);
        head.Children.Add(TileChrome.Body("Settings", 20, FontWeights.SemiBold));
        stack.Children.Add(head);

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

        var quitText = TileChrome.Body("Quit Nothing Shell", 13, FontWeights.SemiBold, TileChrome.Red);
        var quitRow = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(0, 14, 0, 0), Child = quitText };
        quitRow.MouseLeftButtonUp += (_, _) => { Close(); quit(); };
        stack.Children.Add(quitRow);

        Content = new Border
        {
            Background = (Brush)Application.Current.FindResource("Brush.GlassDense"),
            BorderBrush = (Brush)Application.Current.FindResource("Brush.Line"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(28), Padding = new Thickness(24), Child = stack,
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) => s_open = null;
        Loaded += (_, _) =>
        {
            ForegroundNative.Activate(new WindowInteropHelper(this).Handle);
            Activate();
        };
    }

    /// <summary>Shows the settings window, or brings the open one to the front.</summary>
    public static void ShowOrActivate(Func<ShellSettings> settings, Action<bool> setStart, Action<bool> setKeep,
        WeatherService? weather, Action<WeatherPlace> saveWeather, Action quit, WidgetOptions? widgets = null)
    {
        if (s_open is not null)
        {
            s_open.Activate();
            ForegroundNative.Activate(new WindowInteropHelper(s_open).Handle);
            return;
        }
        s_open = new SettingsWindow(settings, setStart, setKeep, weather, saveWeather, quit, widgets);
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

        if (w.WallpaperDir is not { } dir || !System.IO.Directory.Exists(dir)) return;
        stack.Children.Add(Section("Wallpaper"));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 18) };
        var current = WallpaperNative.Current();
        foreach (var name in new[] { "nothing_halftone_light", "nothing_halftone_dark", "nothing_glyph_dark", "nothing_grid_light" })
        {
            var full = System.IO.Path.Combine(dir, name + ".png");
            var preview = System.IO.Path.Combine(dir, name + "_preview.jpg");
            if (!System.IO.File.Exists(full)) continue;
            var selected = string.Equals(current, full, StringComparison.OrdinalIgnoreCase);
            var thumb = new Border
            {
                Height = 96, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                BorderBrush = selected ? TileChrome.Text : Brushes.Transparent, BorderThickness = new Thickness(2),
                Background = System.IO.File.Exists(preview)
                    ? new ImageBrush(Thumb(preview)) { Stretch = Stretch.UniformToFill }
                    : TileChrome.Frozen(0x33, 0x33, 0x33),
                ToolTip = name.Replace("nothing_", "").Replace('_', ' '),
            };
            thumb.MouseLeftButtonUp += (_, _) =>
            {
                var ok = WallpaperNative.Apply(full);
                Log.Info($"wallpaper applied ok={ok} ({name})");
                foreach (var t in grid.Children.OfType<Border>()) t.BorderBrush = Brushes.Transparent;
                if (ok) thumb.BorderBrush = TileChrome.Text;
            };
            grid.Children.Add(thumb);
        }
        stack.Children.Add(grid);
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

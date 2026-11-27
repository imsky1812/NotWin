using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NotWin.Core;
using NotWin.Core.Forecast;
using NotWin.Forecast;
using NotWin.GoogleCalendar;
using NotWin.Native;
using NotWin.Notch.Dashboard;

using NotWin.Theme;

namespace NotWin.Settings;

/// <summary>
/// NotWin's settings (spec §6.7, Plan 4 contents): weather city, start with Windows, keep dock visible, and
/// Quit. Opened from the notch's Settings tile, a right-click on the open notch, and the dock's start-button menu.
/// One instance; Esc or ✕ closes. Widget scale and wallpaper arrive with Plan 5.
/// </summary>
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? s_open;
    private static Action? s_reopen;
    private readonly Func<ShellSettings> _settings;
    private readonly Action<bool> _setStart, _setKeep;
    private readonly Action<WeatherPlace> _saveWeather;
    private readonly WeatherService? _weather;
    private readonly TextBox _city;
    private readonly TextBlock _cityStatus;
    private readonly Border _startToggle = new(), _keepToggle = new(), _previewToggle = new();
    private DockEffects? _effects;
    private readonly GoogleCalendarService? _calendar;
    private readonly StackPanel _calendarHost = new();
    private readonly TextBox _clientId = SettingsFields.Text();
    private readonly PasswordBox _clientSecret = SettingsFields.Secret();

    /// <summary>Plan 5 widget settings: widget size (S/M/L), entering widget edit mode, and (Plan 13) the theme.</summary>
    /// <param name="IsDark">True while Windows is in dark mode; with <paramref name="SetDark"/>, the Theme row.</param>
    /// <summary>Plan 7 switch: window previews in the dock.</summary>
    public sealed record DockEffects(Action<bool> SetPreviews);

    /// <summary>Plan 9 sync: the folder (null = off), the status line, and choosing / clearing the folder.</summary>
    public sealed record SyncOptions(Func<string?> Folder, Func<string> Status, Action<string?> SetFolder);

    public sealed record WidgetOptions(Action<string> SetScale, Action EditWidgets, Func<bool>? IsDark = null, Func<bool, Task>? SetDark = null);

    private SettingsWindow(Func<ShellSettings> settings, Action<bool> setStart, Action<bool> setKeep, WeatherService? weather,
        Action<WeatherPlace> saveWeather, Action quit, WidgetOptions? widgets, DockEffects? effects = null,
        GoogleCalendarService? calendar = null, SyncOptions? sync = null)
    {
        NotWin.Theme.Motion.Smooth(this); // quick pop in, quicker fade out (Plan 13 follow-up)
        _effects = effects;
        _calendar = calendar;
        _settings = settings;
        _setStart = setStart;
        _setKeep = setKeep;
        _weather = weather;
        _saveWeather = saveWeather;
        Title = "NotWin Settings";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = DashChrome.Font;
        Foreground = DashChrome.Text;

        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 18) };
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        head.Children.Add(CloseChip());
        head.Children.Add(TitleRow());

        // Weather city (spec §6.5: a city the user types in Settings).
        _city = new TextBox
        {
            Background = Brushes.Transparent, Foreground = DashChrome.Text, CaretBrush = DashChrome.Text, // the rounded Border paints the field
            BorderThickness = new Thickness(0), FontFamily = DashChrome.Font, FontSize = 13, Padding = new Thickness(10, 8, 10, 8),
            Text = settings().Weather?.Name ?? "",
        };
        _city.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = SetCityAsync(); } };
        var set = Pill("Set");
        set.MouseLeftButtonUp += (_, _) => _ = SetCityAsync();
        var cityRow = new DockPanel();
        set.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(set, System.Windows.Controls.Dock.Right);
        cityRow.Children.Add(set);
        cityRow.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = DashChrome.Chip, Child = _city, ClipToBounds = true });
        _cityStatus = DashChrome.Label(PlaceText(settings().Weather) ?? "Type a city and press Enter");
        _cityStatus.Margin = new Thickness(2, 10, 0, 0);
        stack.Children.Add(Card("WEATHER", cityRow, _cityStatus));

        // Google Calendar (Plan 6): the guided setup, sign-in and status.
        if (calendar is not null)
        {
            _clientId.Text = calendar.SavedClientId ?? "";
            PaintCalendar();
            calendar.Changed += PaintCalendar;
            Closed += (_, _) => calendar.Changed -= PaintCalendar;
            stack.Children.Add(Card("GOOGLE CALENDAR", _calendarHost));
        }

        if (widgets is not null) AddWidgetSections(stack, widgets);

        // Sync (Plan 9): settings, notes and alarms kept in a folder you pick, for your other PCs.
        if (sync is not null)
        {
            var host = new StackPanel();
            void PaintSync()
            {
                host.Children.Clear();
                var folder = sync.Folder();
                var about = DashChrome.Body(folder is null
                    ? "Keep settings, pins, widgets, notes and alarms in a folder (e.g. OneDrive) so your other PCs get them. The Google sign-in and photos stay on each PC."
                    : folder, 12, FontWeights.Normal, DashChrome.Dim);
                about.TextWrapping = TextWrapping.Wrap;
                host.Children.Add(about);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
                var choose = Pill(folder is null ? "Choose folder…" : "Change…");
                choose.MouseLeftButtonUp += (_, _) =>
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to sync NotWin through" };
                    if (dialog.ShowDialog(this) == true) { sync.SetFolder(dialog.FolderName); PaintSync(); }
                };
                row.Children.Add(choose);
                if (folder is not null)
                {
                    var off = Pill("Turn off");
                    off.Margin = new Thickness(8, 0, 0, 0);
                    off.MouseLeftButtonUp += (_, _) => { sync.SetFolder(null); PaintSync(); };
                    row.Children.Add(off);
                }
                host.Children.Add(row);
                var status = DashChrome.Label(sync.Status());
                status.Margin = new Thickness(2, 8, 0, 0);
                host.Children.Add(status);
            }
            PaintSync();
            stack.Children.Add(Card("SYNC", host));
        }

        var shell = new StackPanel();
        shell.Children.Add(ToggleRow("Start with Windows", _startToggle, () => _settings().StartWithWindows, _setStart));
        shell.Children.Add(ToggleRow("Keep dock visible", _keepToggle, () => _settings().KeepDockVisible, _setKeep));
        if (_effects is { } fx)
            shell.Children.Add(ToggleRow("Window previews in the dock", _previewToggle, () => _settings().WindowPreviews, fx.SetPreviews));
        if (shell.Children.Count > 0) ((DockPanel)shell.Children[^1]).Margin = new Thickness(0); // the card pads the bottom
        stack.Children.Add(Card("SHELL", shell));

        var quitText = DashChrome.Body("Quit NotWin", 13, FontWeights.SemiBold, DashChrome.Red);
        quitText.HorizontalAlignment = HorizontalAlignment.Center;
        var quitRow = new Border
        {
            Background = Brushes.Transparent, BorderBrush = DashChrome.FrozenArgb(0x55, 0xE0, 0x1F, 0x25), BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16), Cursor = Cursors.Hand, Padding = new Thickness(0, 12, 0, 12), Margin = new Thickness(0, 4, 0, 0), Child = quitText,
        };
        quitRow.MouseEnter += (_, _) => quitRow.Background = DashChrome.FrozenArgb(0x1F, 0xE0, 0x1F, 0x25);
        quitRow.MouseLeave += (_, _) => quitRow.Background = Brushes.Transparent;
        quitRow.MouseLeftButtonUp += (_, _) => { Close(); quit(); };
        stack.Children.Add(quitRow);

        Content = new Border
        {
            Background = (Brush)Application.Current.FindResource("Shell.Glass"),
            BorderBrush = (Brush)Application.Current.FindResource("Shell.Line"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(32), Padding = new Thickness(20),
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
        Closed += (_, _) => { s_open = null; ShellTheme.Changed -= OnThemeChanged; };
        ShellTheme.Changed += OnThemeChanged;
        Loaded += (_, _) =>
        {
            Fit();
            ForegroundNative.Activate(new WindowInteropHelper(this).Handle);
            Activate();
        };
    }

    // The theme changed while open (Theme row, the tile, or Windows): built again in the new colours.
    private void OnThemeChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (s_open != this || s_reopen is not { } reopen) return;
        Motion.CloseNow(this); // at once: a fading window would still count as open and be "activated" instead
        reopen();
    });

    // Never taller than the screen's work area, and kept inside it (re-centred when the content changes size).
    private void Fit()
    {
        var (top, maxHeight) = SettingsLayout.Fit(ActualHeight, SystemParameters.WorkArea);
        if (Math.Abs(MaxHeight - maxHeight) > 0.5) MaxHeight = maxHeight;
        if (Math.Abs(Top - top) > 0.5) Top = top;
    }

    /// <summary>Shows the settings window, or brings the open one to the front.</summary>
    public static void ShowOrActivate(Func<ShellSettings> settings, Action<bool> setStart, Action<bool> setKeep,
        WeatherService? weather, Action<WeatherPlace> saveWeather, Action quit, WidgetOptions? widgets = null, DockEffects? effects = null,
        GoogleCalendarService? calendar = null, SyncOptions? sync = null)
    {
        if (s_open is not null)
        {
            s_open.Activate();
            ForegroundNative.Activate(new WindowInteropHelper(s_open).Handle);
            return;
        }
        s_reopen = () => ShowOrActivate(settings, setStart, setKeep, weather, saveWeather, quit, widgets, effects, calendar, sync);
        s_open = new SettingsWindow(settings, setStart, setKeep, weather, saveWeather, quit, widgets, effects, calendar, sync);
        s_open.Show();
    }

    // Widgets: size S / M / L (applies live) and "Edit widgets"; Theme: light / dark (Plan 13). The Wallpaper section was
    // removed (user 2026-11-26): the Lucent wallpaper now follows the theme and the photo frame.
    private void AddWidgetSections(StackPanel stack, WidgetOptions w)
    {
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        void PaintSizes()
        {
            sizes.Children.Clear();
            foreach (var (key, label) in new[] { ("S", "Small"), ("M", "Medium"), ("L", "Large") })
            {
                var k = key;
                sizes.Children.Add(Choice(label, (_settings().WidgetScale ?? "M") == key, () => { w.SetScale(k); PaintSizes(); }));
            }
        }
        PaintSizes();
        var edit = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var editArrow = DashChrome.Label("›");
        editArrow.FontSize = 14;
        DockPanel.SetDock(editArrow, System.Windows.Controls.Dock.Right);
        edit.Children.Add(editArrow);
        edit.Children.Add(DashChrome.Body("Edit widgets", 13, FontWeights.Normal));
        edit.MouseLeftButtonUp += (_, _) => { Close(); w.EditWidgets(); };
        stack.Children.Add(Card("WIDGETS", sizes, edit));

        // Theme (user 2026-11-26): light or dark, the same as the Dark mode tile (Windows' theme and the Lucent wallpaper).
        if (w.SetDark is { } setDark && w.IsDark is { } isDark)
        {
            var themes = new StackPanel { Orientation = Orientation.Horizontal };
            void PaintThemes()
            {
                themes.Children.Clear();
                foreach (var (dark, label) in new[] { (false, "Light"), (true, "Dark") })
                {
                    var want = dark;
                    themes.Children.Add(Choice(label, isDark() == dark, async () =>
                    {
                        try { await setDark(want); }
                        catch (Exception e) { Log.Error("changing the theme failed", e); }
                    }));
                }
            }
            PaintThemes();
            stack.Children.Add(Card("THEME", themes));
        }
    }

    // The Google Calendar section: signed in, the status and Refresh / Disconnect; otherwise the four setup steps, the
    // console link, the client ID and secret, and Connect (Cancel while the browser is open). The ID and secret boxes
    // are kept across repaints so typing is never lost; they are never logged.
    private void PaintCalendar()
    {
        if (_calendar is not { } c) return;
        _calendarHost.Children.Clear();
        if (c.Status == CalendarStatus.SignedIn)
        {
            _calendarHost.Children.Add(DashChrome.Label(c.StatusText.Length > 0 ? c.StatusText : "CONNECTED"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var refresh = Pill("Refresh");
            refresh.MouseLeftButtonUp += (_, _) => _ = c.RefreshAsync();
            var disconnect = Pill("Disconnect");
            disconnect.Margin = new Thickness(8, 0, 0, 0);
            disconnect.MouseLeftButtonUp += (_, _) => _ = c.DisconnectAsync();
            row.Children.Add(refresh);
            row.Children.Add(disconnect);
            _calendarHost.Children.Add(row);
            return;
        }
        foreach (var step in SettingsFields.GuideSteps)
        {
            var t = DashChrome.Body(step, 12, FontWeights.Normal, DashChrome.Dim);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 0, 0, 6);
            _calendarHost.Children.Add(t);
        }
        var link = DashChrome.Body("Open Google Cloud Console ›", 13, FontWeights.SemiBold);
        link.Cursor = Cursors.Hand;
        link.Margin = new Thickness(0, 4, 0, 12);
        link.MouseLeftButtonUp += (_, _) => UnelevatedShell.Open(SettingsFields.ConsoleUrl, null, (m, _) => Log.Info("settings: " + m));
        _calendarHost.Children.Add(link);
        _calendarHost.Children.Add(SettingsFields.Caption("CLIENT ID"));
        _calendarHost.Children.Add(SettingsFields.Frame(_clientId));
        _calendarHost.Children.Add(SettingsFields.Caption(c.HasSavedSecret ? "CLIENT SECRET (EMPTY: THE SAVED ONE IS USED)" : "CLIENT SECRET"));
        _calendarHost.Children.Add(SettingsFields.Frame(_clientSecret));
        var connect = Pill(c.Connecting ? "Cancel" : "Connect");
        connect.HorizontalAlignment = HorizontalAlignment.Left;
        connect.Margin = new Thickness(0, 12, 0, 0);
        connect.MouseLeftButtonUp += (_, _) =>
        {
            if (c.Connecting) { c.CancelConnect(); return; }
            _ = ConnectAsync(c);
        };
        _calendarHost.Children.Add(connect);
        var status = DashChrome.Label(c.StatusText.Length > 0 ? c.StatusText
            : "GOOGLE WILL SAY IT HASN'T VERIFIED THIS APP. IT'S YOURS: ADVANCED, THEN CONTINUE");
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(2, 8, 0, 0);
        _calendarHost.Children.Add(status);
    }

    private async Task ConnectAsync(GoogleCalendarService c)
    {
        Topmost = false; // the browser comes to the front for the sign-in
        try
        {
            if (await c.ConnectAsync(_clientId.Text, _clientSecret.Password)) _clientSecret.Clear();
        }
        finally
        {
            if (IsLoaded)
            {
                Topmost = true;
                ForegroundNative.Activate(new WindowInteropHelper(this).Handle);
            }
        }
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

    // A section is its own rounded card (Plan 14, user 2026-11-27: a Nothing restyle): a mono label over its controls.
    private static Border Card(string title, params UIElement[] body)
    {
        var inner = new StackPanel();
        inner.Children.Add(CardLabel(title));
        foreach (var e in body) inner.Children.Add(e);
        return new Border
        {
            Background = DashChrome.TileBrush, BorderBrush = DashChrome.Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 12), Child = inner,
        };
    }

    private static TextBlock CardLabel(string text)
    {
        var t = DashChrome.Label(text);
        t.FontFamily = DashChrome.Mono;
        t.FontSize = 10;
        t.Margin = new Thickness(0, 0, 0, 12);
        return t;
    }

    // The header: a red dot and "SETTINGS" in the mono face, with a round ✕ chip (the notch's language).
    private static UIElement TitleRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = DashChrome.Red, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = "SETTINGS", FontFamily = DashChrome.Mono, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = DashChrome.Text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private Border CloseChip()
    {
        var chip = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = DashChrome.Chip, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "✕", FontSize = 12, Foreground = DashChrome.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        chip.MouseEnter += (_, _) => chip.Background = DashChrome.ChipHover;
        chip.MouseLeave += (_, _) => chip.Background = DashChrome.Chip;
        chip.MouseLeftButtonUp += (_, _) => Close();
        DockPanel.SetDock(chip, System.Windows.Controls.Dock.Right);
        return chip;
    }

    // A pill in a choice group (widget size, theme): filled when chosen.
    private static Border Choice(string label, bool on, Action pick)
    {
        var pill = new Border
        {
            Background = on ? DashChrome.Text : DashChrome.Chip, CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = label, FontFamily = DashChrome.Font, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = on ? DashChrome.Dark : DashChrome.Text },
        };
        pill.MouseLeftButtonUp += (_, _) => pick();
        return pill;
    }

    private static Border Pill(string text) => new()
    {
        Background = DashChrome.Text, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 8, 16, 8), Cursor = Cursors.Hand,
        Child = new TextBlock { Text = text, FontFamily = DashChrome.Font, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = DashChrome.Dark },
    };

    private static DockPanel ToggleRow(string label, Border host, Func<bool> get, Action<bool> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        void Paint() => host.Child = DashChrome.Toggle(get());
        Paint();
        DockPanel.SetDock(host, System.Windows.Controls.Dock.Right);
        row.Children.Add(host);
        var text = DashChrome.Body(label, 13, FontWeights.Normal);
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

/// <summary>The Google Calendar section's text boxes and the guided setup text (Plan 6).</summary>
public static class SettingsFields
{
    public const string ConsoleUrl = "https://console.cloud.google.com/apis/credentials";

    public static readonly string[] GuideSteps =
    [
        "1. Open Google Cloud Console and create a project.",
        "2. In APIs & Services, enable the Google Calendar API.",
        "3. In Google Auth Platform > Audience (older consoles: OAuth consent screen) choose External, then click Publish app (in Testing, Google signs you out every 7 days and only listed test users can sign in).",
        "4. In Google Auth Platform > Clients (older consoles: Credentials), create an OAuth client of type Desktop app, and paste its ID and secret below.",
    ];

    public static TextBox Text() => new()
    {
        Background = Brushes.Transparent, Foreground = DashChrome.Text, CaretBrush = DashChrome.Text, BorderThickness = new Thickness(0),
        FontFamily = DashChrome.Font, FontSize = 13, Padding = new Thickness(10, 8, 10, 8),
    };

    public static PasswordBox Secret() => new()
    {
        Background = Brushes.Transparent, Foreground = DashChrome.Text, CaretBrush = DashChrome.Text, BorderThickness = new Thickness(0),
        FontFamily = DashChrome.Font, FontSize = 13, Padding = new Thickness(10, 8, 10, 8),
    };

    /// <summary>The rounded #222 frame the weather city box uses; detaches the box from an earlier frame first.</summary>
    public static Border Frame(Control box)
    {
        if (box.Parent is Border old) old.Child = null;
        return new Border { CornerRadius = new CornerRadius(12), Background = DashChrome.Chip, Child = box, ClipToBounds = true };
    }

    public static TextBlock Caption(string text)
    {
        var t = DashChrome.Label(text);
        t.FontSize = 11;
        t.Margin = new Thickness(2, 6, 0, 6);
        return t;
    }
}

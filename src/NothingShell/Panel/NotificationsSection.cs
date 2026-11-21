using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Core.Panel;
using NothingShell.Dock;
using NothingShell.Notifications;

namespace NothingShell.Panel;

/// <summary>
/// The panel's notifications (spec §6.3): grouped by app (count + newest text), ✕ dismisses a group, "Clear all",
/// a click opens the app. Plus the one-time guided tip about Windows' own pop-ups (the user flips the setting; we
/// never change it). Rebuilt when the panel opens and whenever the list changes while it is open.
/// </summary>
public sealed class NotificationsSection
{
    private readonly StackPanel _host;
    private readonly NotificationService _service;
    private readonly Func<ShellSettings> _settings;
    private readonly Action<Func<ShellSettings, ShellSettings>> _update;
    private readonly Action _closePanel;
    private readonly Func<bool> _panelOpen;
    private readonly AppIconCache _icons = new();
    private int _version;

    public NotificationsSection(StackPanel host, NotificationService service, Func<ShellSettings> settings,
        Action<Func<ShellSettings, ShellSettings>> update, Action closePanel, Func<bool> panelOpen)
    {
        _host = host;
        _service = service;
        _settings = settings;
        _update = update;
        _closePanel = closePanel;
        _panelOpen = panelOpen;
        _service.ListChanged += () => { if (_panelOpen()) _ = RefreshAsync(); };
    }

    public async Task RefreshAsync()
    {
        var version = ++_version;
        var items = _service.Available ? await _service.GetAllAsync() : [];
        if (version != _version) return; // a newer refresh is on its way
        Render(NotificationGroups.Build(items));
    }

    private void Render(IReadOnlyList<NotificationGroup> groups)
    {
        _host.Children.Clear();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (groups.Count > 0)
        {
            var clear = Label("CLEAR ALL");
            clear.Cursor = Cursors.Hand;
            clear.MouseLeftButtonUp += (_, _) => _service.ClearAll();
            DockPanel.SetDock(clear, System.Windows.Controls.Dock.Right);
            header.Children.Add(clear);
        }
        header.Children.Add(Label("NOTIFICATIONS"));
        _host.Children.Add(header);

        if (!_settings().BannerTipDismissed && _service.Available) _host.Children.Add(BannerTip());

        if (!_service.Available)
        {
            _host.Children.Add(Dim(_service.UnavailableReason switch
            {
                "no package identity" => "Notifications need the Nothing Shell package. Run build\\install.ps1.",
                "access denied" => "Notification access is off for Nothing Shell.",
                _ => "Notifications are unavailable.",
            }));
            return;
        }
        if (groups.Count == 0) { _host.Children.Add(Dim("No notifications")); return; }
        foreach (var g in groups) _host.Children.Add(Card(g));
    }

    // Mockup .nc: #0F FFFFFF, radius 18, padding 12/14; 32 px icon circle; app + count SemiBold 13; text 12 #BBB; ✕ top right.
    private Border Card(NotificationGroup g)
    {
        var icon = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0),
            Child = new Image { Width = 20, Height = 20, Source = string.IsNullOrEmpty(g.AppId) ? null : _icons.Get(g.AppId, 40) },
        };
        var title = new TextBlock
        {
            Text = g.Count > 1 ? $"{g.AppName}  ·  {g.Count}" : g.AppName, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 20, 0),
        };
        var body = new TextBlock
        {
            Text = g.LatestText, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
            TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0),
        };
        var text = new StackPanel { Children = { title, body } };
        var row = new DockPanel();
        row.Children.Add(icon);
        row.Children.Add(text);

        var close = new TextBlock
        {
            Text = "✕", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Dismiss",
        };
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; _service.Remove(g.Ids); };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand,
            Child = new Grid { Children = { row, close } },
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            _service.OpenApp(g.AppId);
            _closePanel();
        };
        return card;
    }

    private Border BannerTip()
    {
        var text = new TextBlock
        {
            Text = "New notifications appear in the notch. To stop Windows' pop-ups too, turn on Do not disturb, or turn off banners for chatty apps.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        };
        var open = Button("Open settings", () => TilesSection.OpenSettings("ms-settings:notifications"));
        var gotIt = Button("Got it", () =>
        {
            _update(s => s with { BannerTipDismissed = true });
            _ = RefreshAsync();
        });
        gotIt.Margin = new Thickness(8, 0, 0, 0);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8),
            Child = new StackPanel
            {
                Children =
                {
                    text,
                    new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { open, gotIt } },
                },
            },
        };
    }

    private static Border Button(string text, Action onClick)
    {
        var b = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 6, 10, 6), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text.ToUpperInvariant(), FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"), FontSize = 11, FontWeight = FontWeights.Medium },
        };
        b.MouseLeftButtonUp += (_, _) => onClick();
        return b;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"), FontSize = 11,
        FontWeight = FontWeights.Medium, Foreground = (Brush)Application.Current.FindResource("Brush.Dim"), VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Dim(string text) => new()
    {
        Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Brush.Dim"),
        Margin = new Thickness(2, 0, 0, 4),
    };
}

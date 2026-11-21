using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NothingShell.Core.Launcher;
using NothingShell.Native;

namespace NothingShell.Launcher;

/// <summary>
/// The launcher surface: a scrim over the primary monitor and the centered glass panel (mockup final-v2 .start).
/// Created once and then only shown/hidden; per-open state (query, icons) is dropped on hide.
/// </summary>
public partial class LauncherWindow : Window
{
    private static readonly Duration OpenDuration = new(TimeSpan.FromMilliseconds(200));
    private readonly LauncherViewModel _vm;
    private bool _hiding;
    private bool _allowClose;
    private IReadOnlyList<LaunchItem>? _allAppsShown;
    private Point? _openPointer;

    /// <summary>The launcher should close: Esc, a click on the scrim, or focus moved elsewhere.</summary>
    public event Action? Dismissed;

    /// <summary>A power choice was clicked ("sleep", "restart", "shutdown").</summary>
    public event Action<string>? PowerRequested;

    public LauncherWindow(LauncherViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _vm.PropertyChanged += OnVmChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        // Power menu: right edge lined up with the power button, 8 DIP above it (sizes here are device pixels).
        PowerMenu.CustomPopupPlacementCallback = (popup, target, _) =>
        {
            var gap = 8 * VisualTreeHelper.GetDpi(PowerButton).DpiScaleY;
            return [new CustomPopupPlacement(new Point(target.Width - popup.Width, -popup.Height - gap), PopupPrimaryAxis.Vertical)];
        };
        // WPF also raises MouseMove when a window appears under a still cursor: only a real change of position counts.
        PreviewMouseMove += (_, e) =>
        {
            var p = e.GetPosition(Root);
            if (_openPointer is null) { _openPointer = p; return; }
            if ((p - _openPointer.Value).Length > 2) _vm.PointerMoved();
        };
        Deactivated += (_, _) => { if (IsVisible && !_hiding) Dismissed?.Invoke(); };
        // Alt+F4 would really close (destroy) the window, and the launcher (and the Win key, which still masks
        // Start) would be dead until the shell restarts. Turn it into an ordinary dismiss instead.
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            Dismissed?.Invoke();
        };
    }

    public void ShowAndFocus()
    {
        _hiding = false;
        _openPointer = null;
        LauncherIcons.BeginSession();
        _vm.Reset();
        PowerMenu.IsOpen = false;
        SearchBox.Text = "";
        // Cover the primary monitor (DIPs).
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        RenderAll();

        Root.Opacity = 0;
        PanelShift.Y = 8;
        Show();
        var ok = ForegroundNative.Activate(new WindowInteropHelper(this).Handle);
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        Log.Info($"launcher focus ok={ok}");

        var ease = new KeySpline(0.2, 0.8, 0.2, 1);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames = { new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(OpenDuration.TimeSpan), ease) },
        });
        PanelShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames = { new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(OpenDuration.TimeSpan), ease) },
        });
    }

    /// <summary>Really closes the window (shell exit only).</summary>
    public void CloseForGood()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Hides at once (no exit animation) and drops per-open state.</summary>
    public void HideNow()
    {
        _hiding = true;
        Root.BeginAnimation(OpacityProperty, null);
        PanelShift.BeginAnimation(TranslateTransform.YProperty, null);
        Hide();
        _vm.Reset();
        MostGrid.Children.Clear();
        ResultRows.Children.Clear();
        AllAppsList.ItemsSource = null;
        _allAppsShown = null;
        LauncherIcons.EndSession();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: _vm.MoveSelection(1); e.Handled = true; break;
            case Key.Up: _vm.MoveSelection(-1); e.Handled = true; break;
            case Key.Enter: _vm.ActivateSelected(); e.Handled = true; break;
            case Key.Tab: e.Handled = true; break;
            case Key.Escape:
                if (PowerMenu.IsOpen) PowerMenu.IsOpen = false;
                else if (!_vm.Back()) Dismissed?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _vm.Query = SearchBox.Text;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LauncherViewModel.View):
            case nameof(LauncherViewModel.Results):
            case nameof(LauncherViewModel.MostUsed):
            case nameof(LauncherViewModel.AllApps):
                RenderAll();
                break;
            case nameof(LauncherViewModel.Selected):
                RenderSelection();
                break;
        }
    }

    private void RenderAll()
    {
        HomeView.Visibility = _vm.View == LauncherView.Home ? Visibility.Visible : Visibility.Collapsed;
        ResultsView.Visibility = _vm.View == LauncherView.Results ? Visibility.Visible : Visibility.Collapsed;
        AllAppsView.Visibility = _vm.View == LauncherView.AllApps ? Visibility.Visible : Visibility.Collapsed;
        AllAppsLink.Visibility = _vm.View == LauncherView.AllApps ? Visibility.Collapsed : Visibility.Visible;

        if (_vm.View == LauncherView.Home)
        {
            MostGrid.Children.Clear();
            for (var i = 0; i < _vm.MostUsed.Count; i++) MostGrid.Children.Add(MostTile(_vm.MostUsed[i]));
        }
        else if (_vm.View == LauncherView.Results)
        {
            ResultRows.Children.Clear();
            for (var i = 0; i < _vm.Results.Count; i++)
                ResultRows.Children.Add(i == 0 ? BestCard(_vm.Results[i]) : Row(_vm.Results[i]));
            NoResults.Visibility = _vm.Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (!ReferenceEquals(_allAppsShown, _vm.AllApps))
        {
            _allAppsShown = _vm.AllApps;
            AllAppsList.ItemsSource = LetterGroups(_vm.AllApps);
        }
        RenderSelection();
    }

    private void RenderSelection()
    {
        var sel = _vm.SelectedItem;
        if (_vm.View == LauncherView.AllApps)
        {
            AllAppsList.SelectedIndex = _vm.Selected;
            if (sel is not null) AllAppsList.ScrollIntoView(sel);
            return;
        }
        var panel = _vm.View == LauncherView.Home ? (System.Windows.Controls.Panel)MostGrid : ResultRows;
        foreach (var child in panel.Children.OfType<Border>())
        {
            var isSel = sel is not null && child.Tag is LaunchItem it && it.Id == sel.Id;
            if (child.Name == "Best")
            {
                child.BorderThickness = new Thickness(isSel ? 1.5 : 0);
            }
            else
            {
                child.Background = isSel ? (Brush)FindResource("Brush.RowSel") : Brushes.Transparent;
            }
        }
    }

    // ---- item visuals (mockup .app / .best / .ri) ----

    private Border Circle(LaunchItem item, double circle, double icon, int px) => new()
    {
        Width = circle,
        Height = circle,
        CornerRadius = new CornerRadius(circle / 2),
        Background = (Brush)FindResource("Brush.Circle"),
        Child = new Image { Width = icon, Height = icon, Source = LauncherIcons.For(item, px) },
    };

    private Border MostTile(LaunchItem item)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(Circle(item, 48, 28, 48));
        stack.Children.Add(new TextBlock
        {
            Text = item.Name, FontSize = 11.5, FontWeight = FontWeights.Medium, Foreground = (Brush)FindResource("Brush.SoftText"),
            Margin = new Thickness(4, 7, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 92,
        });
        return Interactive(new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(0, 10, 0, 10), Margin = new Thickness(2), Child = stack }, item);
    }

    private Border BestCard(LaunchItem item)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Name, FontSize = 19, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = $"{KindLabel(item.Kind)} · ENTER TO OPEN", Style = (Style)FindResource("Label"), Margin = new Thickness(0, 4, 0, 0) });
        var row = new DockPanel();
        var circle = Circle(item, 50, 28, 48); // user feedback: the first icon read too large
        circle.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(circle);
        row.Children.Add(text);
        var card = new Border
        {
            Name = "Best", CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10),
            Background = (Brush)FindResource("Brush.BestCard"), BorderBrush = (Brush)FindResource("Brush.Text"), Child = row,
        };
        return Interactive(card, item);
    }

    private Border Row(LaunchItem item)
    {
        var dock = new DockPanel();
        var kind = new TextBlock { Text = KindLabel(item.Kind), Style = (Style)FindResource("Label") };
        DockPanel.SetDock(kind, System.Windows.Controls.Dock.Right);
        dock.Children.Add(kind);
        var circle = Circle(item, 32, 20, 32);
        circle.Margin = new Thickness(0, 0, 12, 0);
        dock.Children.Add(circle);
        dock.Children.Add(new TextBlock { Text = item.Name, FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return Interactive(new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 1, 0, 1), Child = dock }, item);
    }

    private Border Interactive(Border b, LaunchItem item)
    {
        b.Tag = item;
        b.Cursor = Cursors.Hand;
        if (b.Background is null) b.Background = Brushes.Transparent; // hit-testable
        b.MouseEnter += (_, _) => _vm.HoverSelect(item);
        b.MouseLeftButtonUp += (_, _) => _vm.Activate(item);
        return b;
    }

    private static string KindLabel(LaunchKind k) => k switch
    {
        LaunchKind.App => "APP",
        LaunchKind.Setting => "SETTING",
        LaunchKind.Folder => "FOLDER",
        _ => "FILE",
    };

    // ---- footer, scrim, all apps ----

    private void OnScrimDown(object sender, MouseButtonEventArgs e) => Dismissed?.Invoke();

    private void OnAllAppsClick(object sender, MouseButtonEventArgs e) => _vm.ShowAllApps();

    private void OnBackClick(object sender, MouseButtonEventArgs e) => _vm.Back();

    // A-Z sections ("#" for names starting with a digit or symbol). The view keeps the list's order, so the view
    // model's selection index and the ListBox index stay the same.
    private static ListCollectionView LetterGroups(IReadOnlyList<LaunchItem> apps)
    {
        var view = new ListCollectionView(apps.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LaunchItem.Name), new FirstLetter()));
        return view;
    }

    private sealed class FirstLetter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var s = (value as string)?.TrimStart() ?? "";
            var ch = s.Length > 0 ? char.ToUpperInvariant(s[0]) : '#';
            return ch is >= 'A' and <= 'Z' ? ch.ToString() : "#";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    private void OnAllAppsItemUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LaunchItem item }) _vm.Activate(item);
    }

    private void OnPowerClick(object sender, MouseButtonEventArgs e)
        => PowerMenu.IsOpen = !PowerMenu.IsOpen;

    private void OnSleep(object sender, MouseButtonEventArgs e) { PowerMenu.IsOpen = false; PowerRequested?.Invoke("sleep"); }
    private void OnRestart(object sender, MouseButtonEventArgs e) { PowerMenu.IsOpen = false; PowerRequested?.Invoke("restart"); }
    private void OnShutDown(object sender, MouseButtonEventArgs e) { PowerMenu.IsOpen = false; PowerRequested?.Invoke("shutdown"); }
}

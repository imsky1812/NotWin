using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotWin.Core.Widgets;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Toggle tile (Plan 13): Wi-Fi, Bluetooth, Dark mode, Keep awake, Mic access or Camera access, chosen in edit mode. 1×1
/// a centred line icon on a circle; 2×1 a pill with the icon, the name and the state ("On", never an SSID or device
/// name). Off is a dark card, on a light card; mic / camera turn red while blocked (slashed icon). A click flips it; an
/// unavailable tile, or one blocked by the machine-wide switch, opens Settings instead.
/// </summary>
public sealed class ToggleWidget : IDesktopWidget
{
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["wifi"] = "wifi", ["bluetooth"] = "bluetooth", ["dark"] = "contrast", ["awake"] = "cup", ["mic"] = "mic", ["camera"] = "camera",
    };

    private readonly Func<string, IToggleBackend> _backendFor;
    private readonly Action<string> _launch;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private IToggleBackend? _backend;
    private string _id = "wifi";
    private ToggleState _state = ToggleState.Off;
    private ToggleState? _built;
    private bool _busy;

    /// <param name="backendFor">Makes the backend for a toggle id.</param>
    /// <param name="launch">Opens a Settings URI.</param>
    public ToggleWidget(Func<string, IToggleBackend> backendFor, Action<string> launch)
    {
        _backendFor = backendFor;
        _launch = launch;
    }

    public WidgetKind Kind => WidgetKind.Toggle;

    public event Action? CardChanged;

    private bool IsPrivacy => _id is "mic" or "camera";

    public Surface Surface => _state switch
    {
        ToggleState.On => Surface.Light,
        ToggleState.Blocked => Surface.Red,
        ToggleState.Off when IsPrivacy => Surface.Red,
        _ => Surface.Dark,
    };

    private string StateWord => _state switch
    {
        ToggleState.On => "On",
        ToggleState.Unavailable => "Unavailable",
        ToggleState.Blocked => "Blocked",
        _ => IsPrivacy ? "Blocked" : "Off",
    };

    public void Configure(WidgetConfig? config)
    {
        var id = WidgetChoices.Normalize(WidgetKind.Toggle, config?.Choice) ?? "wifi";
        if (_backend is not null && id == _id) return;
        _id = id;
        _backend?.Dispose();
        _backend = _backendFor(id);
        _backend.Changed += OnBackendChanged;
        _ = RefreshAsync();
    }

    public FrameworkElement Build(WidgetSize size)
    {
        _built = _state;
        var ink = _state == ToggleState.Unavailable ? WidgetCard.Dim(Surface) : WidgetCard.Ink(Surface);
        var slashed = IsPrivacy && _state != ToggleState.On;
        FrameworkElement content;
        if (size.W == 1)
        {
            content = Icon(40, ink, slashed, HorizontalAlignment.Center);
        }
        else
        {
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
                Children =
                {
                    new TextBlock { Text = ChoicePicker.ToggleName(_id), FontFamily = TileChrome.Font, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = ink },
                    new TextBlock { Text = StateWord, FontFamily = TileChrome.Mono, FontSize = 11, Foreground = WidgetCard.Dim(Surface), Margin = new Thickness(0, 2, 0, 0) },
                },
            };
            content = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0),
                Children = { Icon(30, ink, slashed, HorizontalAlignment.Left), text },
            };
        }
        var hit = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { content } };
        hit.MouseLeftButtonUp += (_, e) => { e.Handled = true; Click(); };
        return hit;
    }

    private FrameworkElement Icon(double size, Brush ink, bool slashed, HorizontalAlignment align)
    {
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(TileChrome.Icon(Icons[_id], size, ink));
        if (slashed) grid.Children.Add(TileChrome.Icon("slash", size, ink));
        return grid;
    }

    /// <summary>A click on the tile (outside edit mode).</summary>
    internal void Click() => _ = ClickAsync();

    private async Task ClickAsync()
    {
        if (_backend is not { } backend || _busy) return;
        if (_state is ToggleState.Unavailable or ToggleState.Blocked)
        {
            _launch(backend.SettingsUri);
            return;
        }
        _busy = true;
        try
        {
            await backend.FlipAsync();
            await RefreshAsync();
            Log.Info($"toggle {_id} -> {_state}");
        }
        catch (Exception e)
        {
            Log.Error($"toggle {_id} failed", e);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnBackendChanged()
    {
        if (_dispatcher.CheckAccess()) _ = RefreshAsync();
        else _dispatcher.BeginInvoke(() => _ = RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        var backend = _backend;
        if (backend is null) return;
        ToggleState state;
        try { state = await backend.ReadAsync(); }
        catch (Exception e)
        {
            Log.Error($"toggle {_id} read failed", e);
            state = ToggleState.Unavailable;
        }
        if (!ReferenceEquals(backend, _backend)) return; // the choice changed meanwhile
        _state = state;
        if (_built is { } built && built != state) CardChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_backend is null) return;
        _backend.Changed -= OnBackendChanged;
        _backend.Dispose();
        _backend = null;
    }
}

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NothingShell.Core;
using NothingShell.Core.Notch;
using NothingShell.Native;

namespace NothingShell.Notch;

/// <summary>
/// The notch (spec §6.4): a black pill at the top centre of the primary display, 8 DIP from the top, that never takes
/// focus. The window is a fixed transparent band; the pill inside animates between the state sizes. Hidden while a
/// full-screen app is in front (the controller decides).
/// </summary>
public partial class NotchWindow : Window
{
    private const double BandWidth = 480, BandHeight = 112; // DIPs: room for the 420x64 pill plus its shadow
    private static readonly Duration Resize = new(TimeSpan.FromMilliseconds(300));
    private static readonly KeySpline Ease = new(0.2, 0.8, 0.2, 1);
    private static readonly TimeSpan FillDelay = TimeSpan.FromMilliseconds(300), FillStep = TimeSpan.FromMilliseconds(70);
    private static readonly Brush Lit = Frozen(0xF2, 0xF2, 0xF2), Red = Frozen(0xE8, 0x23, 0x1F), Off = Frozen(0x22, 0x22, 0x22);
    private readonly DispatcherTimer _eq;
    private readonly DispatcherTimer _fill;
    private readonly SolidColorBrush _accent = new(Color.FromRgb(0xE8, 0x23, 0x1F)); // animated: pulses while charging
    private int _fillTarget;
    private bool _charging;
    private readonly Random _rng = new();
    private nint _hwnd;
    private NotchState _shown = NotchState.Resting;

    public NotchWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            DockWindowNative.ApplyDockStyles(hwnd);
            // Plan 3's notch has no click or hover behaviour: make the whole band click-through (WS_EX_TRANSPARENT),
            // so the pill's soft shadow never swallows clicks on the title bars below. Plan 4 (hover dashboard) revisits.
            DockWindowNative.AddExStyle(hwnd, 0x00000020);
        };
        _eq = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(125) };
        _eq.Tick += (_, _) => DrawEqualizer();
        _fill = new DispatcherTimer(DispatcherPriority.Render) { Interval = FillStep };
        _fill.Tick += (_, _) =>
        {
            _fill.Interval = FillStep;
            if (PowerBar.Lit >= _fillTarget) { _fill.Stop(); return; }
            PowerBar.Lit++;
        };
        PowerBar.On = Lit;
        PowerBar.Off = Frozen(0x2A, 0x2A, 0x2A);
        PowerBar.Accent = _accent;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
    }

    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        Place();
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Place);

    private void Place()
    {
        if (_hwnd == 0) return;
        var (mon, dpi) = DockWindowNative.GetPrimaryMonitor();
        var s = dpi / 96.0;
        var w = (int)Math.Round(BandWidth * s);
        var h = (int)Math.Round(BandHeight * s);
        var x = mon.Left + (mon.Right - mon.Left - w) / 2;
        DockWindowNative.PlaceTopmost(_hwnd, new RectI(x, mon.Top, x + w, mon.Top + h));
    }

    // ---- content ----

    public void SetClock(string hhmm) => Clock.Text = hhmm;

    public void SetBattery(bool hasBattery, int percent, bool charging)
    {
        BatteryPanel.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;
        BatteryText.Text = $"{Math.Clamp(percent, 0, 100)}%";
        BatteryFill.Width = 12 * Math.Clamp(percent, 0, 100) / 100.0;
        var low = percent < 20 && !charging;
        BatteryFill.Background = low ? Red : Frozen(0xDD, 0xDD, 0xDD);
        Bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetOpening(string name) => OpeningName.Text = name;

    /// <summary>
    /// Fills the plugged-in / unplugged card (<paramref name="charging"/> = the charger is in). Plugging in fills the
    /// level dots one by one (the last lit dot red and pulsing); unplugging shows the level at once with a grey bolt.
    /// </summary>
    public void SetPower(bool charging, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        _charging = charging;
        _fillTarget = percent == 0 ? 0 : (int)Math.Ceiling(percent / 10.0);
        PowerLabel.Text = !charging ? "On battery" : percent >= 100 ? "Fully charged" : "Charging";
        PowerBolt.Foreground = charging ? _accent : Frozen(0x6A, 0x6A, 0x6A);
        PowerPercent.Text = percent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _fill.Stop();
        if (charging)
        {
            PowerBar.Lit = 0;
            _fill.Interval = FillDelay; // the first dot lights once the pill has widened and the card faded in
            _fill.Start();
        }
        else PowerBar.Lit = _fillTarget;
    }

    public void SetMusicArt(ImageSource? art)
    {
        Art.Source = art;
        Art.Visibility = art is null ? Visibility.Collapsed : Visibility.Visible; // placeholder: the grey box itself
    }

    public void SetNotification(NotchNotification n, ImageSource? icon)
    {
        NotifyApp.Text = n.AppName;
        NotifyText.Text = n.Text;
        NotifyIcon.Source = icon;
    }

    /// <summary>Shows <paramref name="state"/>: resizes the pill and cross-fades the layers (Hidden hides the window).</summary>
    public void ShowState(NotchState state)
    {
        if (state == NotchState.Hidden)
        {
            _eq.Stop();
            StopPower();
            Hide();
            _shown = state;
            return;
        }
        if (!IsVisible) Show(); // ShowActivated=false: never steals focus
        if (state == _shown) return;
        _shown = state;

        var (w, h, r) = state switch
        {
            NotchState.Music => (300.0, 34.0, 20.0),
            NotchState.Timer => (270.0, 34.0, 20.0),
            NotchState.Opening => (300.0, 34.0, 20.0),
            NotchState.Power => (300.0, 34.0, 20.0),
            NotchState.Notification => (420.0, 64.0, 30.0),
            _ => (196.0, 34.0, 20.0),
        };
        Animate(Pill, WidthProperty, w);
        Animate(Pill, HeightProperty, h);
        Pill.CornerRadius = new CornerRadius(r);

        Fade(RestLayer, state is NotchState.Resting or NotchState.Timer);
        Fade(OpeningLayer, state == NotchState.Opening);
        Fade(MusicLayer, state == NotchState.Music);
        Fade(NotifyLayer, state == NotchState.Notification);
        Fade(PowerLayer, state == NotchState.Power);

        if (state == NotchState.Power && _charging)
            _accent.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation(1, 0.35, new Duration(TimeSpan.FromMilliseconds(600)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
        else if (state != NotchState.Power) StopPower(); // nothing animates once the card is gone (spec §7)

        if (state == NotchState.Music) { DrawEqualizer(); _eq.Start(); }
        else _eq.Stop(); // the equalizer only runs while it is visible (spec §7)
    }

    private void StopPower()
    {
        _fill.Stop();
        PowerBar.Lit = _fillTarget;
        _accent.BeginAnimation(SolidColorBrush.OpacityProperty, null);
    }

    private static void Animate(FrameworkElement el, DependencyProperty p, double to) =>
        el.BeginAnimation(p, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames = { new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(Resize.TimeSpan), Ease) },
        });

    private static void Fade(UIElement el, bool on) =>
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, new Duration(TimeSpan.FromMilliseconds(160)))
        {
            BeginTime = on ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero,
        });

    // 10 columns x 4 rows of 3 px dots on a 5 px pitch; lit from the bottom, the top lit dot red (mockup eqSVG).
    private void DrawEqualizer()
    {
        const int cols = 10, rows = 4;
        const double d = 3, pitch = 5;
        Equalizer.Children.Clear();
        for (var c = 0; c < cols; c++)
        {
            var hgt = 1 + _rng.Next(rows);
            for (var row = 0; row < rows; row++)
            {
                var lit = row >= rows - hgt;
                var dot = new Ellipse { Width = d, Height = d, Fill = lit ? (row == rows - hgt ? Red : Lit) : Off };
                System.Windows.Controls.Canvas.SetLeft(dot, c * pitch);
                System.Windows.Controls.Canvas.SetTop(dot, row * pitch);
                Equalizer.Children.Add(dot);
            }
        }
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}

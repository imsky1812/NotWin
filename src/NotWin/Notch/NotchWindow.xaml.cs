using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using NotWin.Core;
using NotWin.Core.Notch;
using NotWin.Native;

namespace NotWin.Notch;

/// <summary>
/// The notch (spec §6.4): a black pill at the top centre of the primary display, 8 DIP from the top, that never takes
/// focus. The window is a fixed transparent band; the pill inside animates between the state sizes. Hidden while a
/// full-screen app is in front (the controller decides).
/// </summary>
public partial class NotchWindow : Window
{
    // DIPs: room for the open 770x326 pill (3 rows) plus its shadow. Click-through except while open, so the big band
    // costs nothing when closed.
    private const double BandWidth = 800, BandHeight = 360, PillTop = 8, DashboardWidth = 770;
    private const double SmallBandWidth = 480, SmallBandHeight = 112; // closed states (420x64 notification + shadow)
    private bool _bigBand;
    private readonly DispatcherTimer _shrink = new() { Interval = TimeSpan.FromMilliseconds(360) };
    private const int WsExTransparent = 0x00000020;
    private static readonly Duration Resize = new(TimeSpan.FromMilliseconds(240)); // quicker (Plan 13 follow-up)
    private static readonly KeySpline Ease = new(0.2, 0.8, 0.2, 1);
    private static readonly TimeSpan FillDelay = TimeSpan.FromMilliseconds(300), FillStep = TimeSpan.FromMilliseconds(70);
    private static readonly Brush Lit = Frozen(0xE8, 0xE8, 0xE8), Red = Frozen(0xD7, 0x19, 0x21), Off = Frozen(0x22, 0x22, 0x22);
    private static readonly Brush LitLight = Frozen(0x10, 0x10, 0x10), OffLight = Frozen(0xDD, 0xDD, 0xDD);
    private bool _batteryLow;
    private readonly DispatcherTimer _eq;
    private readonly DispatcherTimer _fill;
    private readonly SolidColorBrush _accent = new(Color.FromRgb(0xD7, 0x19, 0x21)); // animated: pulses while charging
    private int _fillTarget;
    private bool _charging;
    private readonly Random _rng = new();
    private readonly EqualizerModel _eqModel = new();
    private readonly AudioPeakMeter _meter = new();
    private double _fakeLevel; // only when the meter is unavailable
    private long _nextBind;
    private nint _hwnd;
    private NotchState _shown = NotchState.Resting;
    private RectI _band;
    private double _scale = 1;
    private double _pillW = 196, _pillH = 34;
    private double _openHeight = 232;
    private bool _typing;
    private nint _typingFrom; // the window that had the keyboard before Notes took it (final review #1)
    private readonly Dashboard.GridTimerView _timerGrid = new(22, 5);

    /// <summary>Right-click on the open notch (spec §6.7: Settings).</summary>
    public event Action? SettingsRequested;

    /// <summary>Esc while the notch has keyboard focus.</summary>
    public event Action? EscPressed;

    /// <summary>The window lost activation while typing (click elsewhere): the controller releases the "text" hold.</summary>
    public event Action? TypingEnded;

    /// <summary>Sideways scrolling over the open notch (WM_MOUSEHWHEEL: two-finger touchpad swipe, tilt wheel).</summary>
    public event Action<int>? HorizontalScroll;

    /// <summary>Files were dropped on the notch (WM_DROPFILES; works across UIPI from Explorer).</summary>
    public event Action<string[]>? FilesDropped;

    public NotchWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            DockWindowNative.ApplyDockStyles(hwnd);
            // Click-through until the dashboard opens, so the pill's soft shadow never swallows clicks below it.
            DockWindowNative.AddExStyle(hwnd, WsExTransparent);
            // File drops from Explorer (Medium) into this uiAccess (High) process: WM_DROPFILES, not OLE (ND.9).
            FileDropNative.Enable(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(DropHook);
        };
        Pill.MouseRightButtonUp += (_, e) =>
        {
            if (_shown != NotchState.Open) return;
            e.Handled = true;
            SettingsRequested?.Invoke();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            EscPressed?.Invoke();
        };
        Deactivated += (_, _) => { if (_typing) TypingEnded?.Invoke(); };

        // 20 fps while the music state is visible, off otherwise (spec §7).
        _eq = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _eq.Tick += (_, _) => StepEqualizer();
        Equalizer.Accent = Red;
        _fill = new DispatcherTimer(DispatcherPriority.Render) { Interval = FillStep };
        _fill.Tick += (_, _) =>
        {
            _fill.Interval = FillStep;
            if (PowerBar.Lit >= _fillTarget) { _fill.Stop(); return; }
            PowerBar.Lit++;
        };
        TimerGridHost.Child = _timerGrid;
        _shrink.Tick += (_, _) =>
        {
            _shrink.Stop();
            if (_shown == NotchState.Open || !_bigBand) return;
            _bigBand = false;
            Place();
        };
        TrackClip.SizeChanged += (_, _) => UpdateMarquee();
        PowerBar.Accent = _accent;
        ApplyTheme();
        Theme.ShellTheme.Changed += ApplyTheme; // light / dark (user 2026-11-26)
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _eq.Stop();
            _meter.Dispose();
        };
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
        // The big band only while the dashboard is open: a layered window redraws its whole surface on every frame
        // (equalizer, scrolling title), so the closed states keep the small one.
        var w = (int)Math.Round((_bigBand ? BandWidth : SmallBandWidth) * s);
        var h = (int)Math.Round((_bigBand ? BandHeight : SmallBandHeight) * s);
        var x = mon.Left + (mon.Right - mon.Left - w) / 2;
        _band = new RectI(x, mon.Top, x + w, mon.Top + h);
        _scale = s;
        DockWindowNative.PlaceTopmost(_hwnd, _band);
    }

    // ---- hit areas (physical pixels, for the global mouse feed) ----

    /// <summary>
    /// The pill of the current state, extended up to the top of the screen (pushing the mouse against the top edge
    /// counts as resting on the notch), in physical pixels.
    /// </summary>
    public bool OverPill(PointI p) => Inside(p, _pillW, _pillH);

    /// <summary>The open dashboard's area (physical pixels), also extended to the top edge.</summary>
    public bool OverOpenArea(PointI p) => _shown == NotchState.Open && Inside(p, DashboardWidth, _openHeight);

    private bool Inside(PointI p, double w, double h)
    {
        if (_band.Right <= _band.Left) return false;
        var cx = (_band.Left + _band.Right) / 2.0;
        var half = w * _scale / 2;
        var bottom = _band.Top + (PillTop + h) * _scale;
        return p.X >= cx - half && p.X < cx + half && p.Y >= _band.Top && p.Y < bottom;
    }

    // ---- open dashboard ----

    /// <summary>Sets the open layer's content and height (the pill animates to it while open).</summary>
    public void SetOpenContent(FrameworkElement root, double height)
    {
        if (!ReferenceEquals(OpenLayer.Content, root)) OpenLayer.Content = root;
        OpenLayer.Width = DashboardWidth;
        _openHeight = height;
        if (_shown != NotchState.Open) return;
        _pillH = height;
        Animate(Pill, HeightProperty, height);
    }

    /// <summary>Lets a text box in the dashboard take the keyboard: the window becomes activatable and comes to front.</summary>
    public void ActivateForTyping()
    {
        if (_hwnd == 0 || _typing) return;
        _typing = true;
        _typingFrom = ForegroundNative.Current();
        DockWindowNative.RemoveExStyle(_hwnd, DockWindowNative.WsExNoActivate);
        Focusable = true;
        ForegroundNative.Activate(_hwnd);
        Activate();
    }

    /// <summary>Back to a no-activate window (typing ended or the notch closed).</summary>
    public void EndTyping()
    {
        if (_hwnd == 0 || !_typing) return;
        _typing = false;
        System.Windows.Input.Keyboard.ClearFocus();
        Focusable = false;
        DockWindowNative.AddExStyle(_hwnd, DockWindowNative.WsExNoActivate);
        // Esc / closing must not leave the keyboard on the notch: give it back to the app the user was in.
        if (Core.Shell.ForegroundHandBack.Target(_typingFrom, ForegroundNative.Current(), _hwnd, ForegroundNative.Exists) is { } back)
            ForegroundNative.Activate(back);
        _typingFrom = 0;
    }

    // ---- content ----

    public void SetClock(string hhmm) => Clock.Text = hhmm;

    public void SetBattery(bool hasBattery, int percent, bool charging)
    {
        BatteryPanel.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;
        BatteryText.Text = $"{Math.Clamp(percent, 0, 100)}%";
        BatteryFill.Width = 12 * Math.Clamp(percent, 0, 100) / 100.0;
        var low = percent < 20 && !charging;
        _batteryLow = low;
        if (low) BatteryFill.Background = Red;
        else BatteryFill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Shell.Soft");
        Bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetOpening(string name) => OpeningName.Text = name;

    /// <summary>The live Timer state's content (270x34: mini grid + dot countdown).</summary>
    public void SetTimer(double fraction, string text, bool running, bool flash)
    {
        _timerGrid.Show(fraction, running, flash);
        TimerText.Text = text;
    }

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

    private static readonly Brush ArtPlaceholder = Frozen(0x3A, 0x3A, 0x3A);

    /// <summary>The music state's art (a circle; grey without art) and "title · artist" (title only if no artist).</summary>
    public void SetMusic(ImageSource? art, string? title, string? artist)
    {
        ArtCircle.Fill = art is null ? ArtPlaceholder : new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        var t = title ?? "";
        var a = artist ?? "";
        if (TrackTitle.Text == t && TrackArtist.Text == a) return;
        TrackTitle.Text = t;
        TrackDot.Text = t.Length > 0 && a.Length > 0 ? "  ·  " : "";
        TrackArtist.Text = a;
        UpdateMarquee();
    }

    // Long "title · artist" drifts left and back with pauses, only while the music state shows (30 fps cap).
    private void UpdateMarquee()
    {
        TrackShift.BeginAnimation(TranslateTransform.XProperty, null);
        TrackShift.X = 0;
        if (_shown != NotchState.Music) return;
        TrackText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overflow = TrackText.DesiredSize.Width - TrackClip.ActualWidth;
        if (overflow <= 2 || TrackClip.ActualWidth <= 0) return;
        var travel = TimeSpan.FromSeconds(overflow / 25); // 25 DIP/s
        var hold = TimeSpan.FromSeconds(2);
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(hold)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(hold + travel)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(hold + travel + hold)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(hold + travel + hold + travel)));
        Timeline.SetDesiredFrameRate(anim, 30);
        TrackShift.BeginAnimation(TranslateTransform.XProperty, anim);
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
            SetClickThrough(true);
            EndTyping();
            StopEqualizer();
            StopPower();
            Hide();
            _shown = state;
            return;
        }
        if (!IsVisible) Show(); // ShowActivated=false: never steals focus
        if (state == _shown) return;
        _shown = state;
        if (state == NotchState.Open && !_bigBand) { _shrink.Stop(); _bigBand = true; Place(); }
        else if (state != NotchState.Open && _bigBand) { _shrink.Stop(); _shrink.Start(); } // after the close animation

        var (w, h, r) = state switch
        {
            NotchState.Music => (340.0, 34.0, 20.0),
            NotchState.Timer => (270.0, 34.0, 20.0),
            NotchState.Opening => (300.0, 34.0, 20.0),
            NotchState.Power => (300.0, 34.0, 20.0),
            NotchState.Notification => (420.0, 64.0, 30.0),
            NotchState.Open => (DashboardWidth, _openHeight, 34.0),
            _ => (196.0, 34.0, 20.0),
        };
        _pillW = w;
        _pillH = h;
        SetClickThrough(state != NotchState.Open || _clickThroughHolds > 0);
        if (state != NotchState.Open) EndTyping();
        OpenLayer.IsHitTestVisible = state == NotchState.Open;
        Animate(Pill, WidthProperty, w);
        Animate(Pill, HeightProperty, h);
        Pill.CornerRadius = new CornerRadius(r);

        Fade(RestLayer, state == NotchState.Resting);
        Fade(TimerLayer, state == NotchState.Timer);
        Fade(OpeningLayer, state == NotchState.Opening);
        Fade(MusicLayer, state == NotchState.Music);
        Fade(NotifyLayer, state == NotchState.Notification);
        Fade(PowerLayer, state == NotchState.Power);
        Fade(OpenLayer, state == NotchState.Open);

        if (state == NotchState.Power && _charging)
            _accent.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation(1, 0.35, new Duration(TimeSpan.FromMilliseconds(600)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
        else if (state != NotchState.Power) StopPower(); // nothing animates once the card is gone (spec §7)

        if (state == NotchState.Music) StartEqualizer();
        else StopEqualizer(); // the equalizer and its meter only run while it is visible (spec §7)
        UpdateMarquee();
    }

    private nint DropHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WmMouseHWheel = 0x020E;
        if (msg == WmMouseHWheel)
        {
            if (_shown == NotchState.Open) HorizontalScroll?.Invoke((short)((long)wParam >> 16 & 0xFFFF));
            handled = true;
            return 0;
        }
        if (msg != FileDropNative.WmDropFiles) return 0;
        handled = true;
        var paths = FileDropNative.Read(wParam);
        if (paths.Length > 0) FilesDropped?.Invoke(paths);
        return 0;
    }

    private int _clickThroughHolds;

    /// <summary>
    /// Holds the notch click-through (true) or lets go of a hold (false) while the drag helper takes a shelf drag over;
    /// with no hold left the notch is click-through exactly when its state says so (review, Plan 8: a blind restore
    /// made the resting pill swallow clicks).
    /// </summary>
    public void HoldClickThrough(bool hold)
    {
        _clickThroughHolds = Math.Max(0, _clickThroughHolds + (hold ? 1 : -1));
        SetClickThrough(_clickThroughHolds > 0 || _shown != NotchState.Open);
    }

    private void SetClickThrough(bool on)
    {
        if (_hwnd == 0) return;
        if (!on) FileDropNative.RevokeOle(_hwnd); // opening: make sure no OLE target shadows WM_DROPFILES
        if (on) DockWindowNative.AddExStyle(_hwnd, WsExTransparent);
        else DockWindowNative.RemoveExStyle(_hwnd, WsExTransparent);
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

    /// <summary>The default playback device changed: follow it if the equalizer is running.</summary>
    public void RebindMeter()
    {
        if (_eq.IsEnabled) _meter.Bind();
    }

    private void StartEqualizer()
    {
        _meter.Bind();
        StepEqualizer();
        _eq.Start();
    }

    private void StopEqualizer()
    {
        _eq.Stop();
        _meter.Unbind();
    }

    // Real output level from Core Audio; without a meter, a smoothed random level so it still looks alive.
    private void StepEqualizer()
    {
        var peak = _meter.Peak();
        if (peak is null && _eq.IsEnabled && Environment.TickCount64 >= _nextBind)
        {
            _nextBind = Environment.TickCount64 + 1000; // no device / device went away: retry at most once a second
            if (_meter.Bind()) peak = _meter.Peak();
        }
        if (peak is null)
        {
            _fakeLevel += (_rng.NextDouble() - _fakeLevel) * 0.35;
            peak = (float)Math.Pow(10, (_fakeLevel * 36 - 36) / 20);
        }
        _eqModel.Step(peak.Value);
        Equalizer.Show(_eqModel);
    }

    // The dots that are set in code (equalizer, power bar) follow the light / dark theme; the rest of the pill uses
    // DynamicResources (Shell.*).
    private void ApplyTheme()
    {
        var light = Theme.ShellTheme.Light;
        Equalizer.On = light ? LitLight : Lit;
        Equalizer.Off = light ? OffLight : Off;
        PowerBar.On = light ? LitLight : Lit;
        PowerBar.Off = light ? OffLight : Frozen(0x2A, 0x2A, 0x2A);
        Equalizer.InvalidateVisual();
        PowerBar.InvalidateVisual();
        if (!_batteryLow) BatteryFill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Shell.Soft");
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}

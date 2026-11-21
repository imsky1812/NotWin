using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Core.Alarms;
using NothingShell.Core.Widgets;
using NothingShell.Native;
using NothingShell.Notch.Dashboard;
using NothingShell.Widgets;

namespace NothingShell.Alarms;

internal static class AlarmLook
{
    public static string Time(Alarm a) => string.Create(CultureInfo.InvariantCulture, $"{a.Hour:00}:{a.Minute:00}");

    public static Border Pill(string text, bool primary, Action click)
    {
        var b = new Border
        {
            Background = primary ? TileChrome.Text : TileChrome.Frozen(0x2A, 0x2A, 0x2A), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 8, 16, 8), Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 4, 0),
            Child = new TextBlock
            {
                Text = text, FontFamily = TileChrome.Font, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = primary ? TileChrome.Dark : TileChrome.Text,
            },
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }
}

/// <summary>
/// The ringing card (Plan 9): topmost at the top centre under the notch, the time and Snooze 5 min / Stop. Never takes
/// the focus; the app shows it only outside full screen.
/// </summary>
public sealed class AlarmRingWindow : Window
{
    public AlarmRingWindow(Alarm alarm, Action snooze, Action stop)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0; // shown once it sits in its place (Plan 10: it used to appear elsewhere and jump)
        Left = -32000;
        Top = -32000;
        var time = TileChrome.Body(AlarmLook.Time(alarm), 34, FontWeights.SemiBold);
        time.Margin = new Thickness(0, 2, 0, 12);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        var label = TileChrome.Label("ALARM", TileChrome.Red);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(AlarmLook.Pill("Snooze 5 min", false, snooze));
        buttons.Children.Add(AlarmLook.Pill("Stop", true, stop));
        Content = new Border
        {
            Background = WidgetCard.CardBrush, BorderBrush = TileChrome.Red, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(26), Padding = new Thickness(22, 16, 22, 18), MinWidth = 260,
            Child = new StackPanel { Children = { label, time, buttons } },
        };
        SourceInitialized += (_, _) => DockWindowNative.AddExStyle(new WindowInteropHelper(this).Handle, 0x08000000 | 0x80); // no-activate, tool
        Loaded += (_, _) =>
        {
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var (mon, _) = DockWindowNative.GetPrimaryMonitor();
            int w = (int)Math.Ceiling(ActualWidth * scale), h = (int)Math.Ceiling(ActualHeight * scale);
            var left = (mon.Left + mon.Right) / 2 - w / 2;
            var top = mon.Top + (int)Math.Round(64 * scale);
            DockWindowNative.PlaceTopmost(new WindowInteropHelper(this).Handle, new RectI(left, top, left + w, top + h));
            Opacity = 1;
        };
    }
}

/// <summary>
/// Alarm widget (Plan 9): 2×1 "ALARM", the next time big and "IN 8 H · WEEKDAYS" (or "NO ALARMS"); 1×1 the time only.
/// Flashes red while ringing (a click then stops it); otherwise a click opens <see cref="AlarmEditorPopup"/>.
/// </summary>
public sealed class AlarmWidget : IDesktopWidget
{
    private readonly AlarmService _alarms;
    private readonly DispatcherTimer _minute = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _flash = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private Border? _host, _glow;
    private WidgetSize _size;
    private RectI _screen;
    private AlarmEditorPopup? _popup;

    public AlarmWidget(AlarmService alarms)
    {
        _alarms = alarms;
        _alarms.Changed += Fill;
        _minute.Tick += (_, _) => Fill();
        _flash.Tick += (_, _) => { if (_glow is not null) _glow.Opacity = _glow.Opacity > 0.5 ? 0.15 : 0.9; };
    }

    public WidgetKind Kind => WidgetKind.Alarm;

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _host.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_alarms.RingingNow is not null) { _alarms.Stop(); return; }
            _popup?.CloseIfOpen();
            _popup = new AlarmEditorPopup(_alarms);
            _popup.ShowNextTo(_screen);
        };
        Fill();
        _minute.Start();
        return _host;
    }

    public void Placed(RectI screen, RectI monitor) => _screen = screen;

    public void SetPaused(bool paused)
    {
        if (paused) _minute.Stop();
        else { _minute.Start(); Fill(); }
    }

    private void Fill()
    {
        if (_host is null) return;
        var now = DateTime.Now;
        var next = AlarmRules.Next(_alarms.Alarms, now);
        var big = TileChrome.Body(next is { } n ? AlarmLook.Time(n.Alarm) : "--:--", _size.W == 1 ? 30 : 34, FontWeights.SemiBold,
            next is null ? TileChrome.Dim : null);
        FrameworkElement content;
        if (_size.W == 1)
        {
            big.HorizontalAlignment = HorizontalAlignment.Center;
            big.VerticalAlignment = VerticalAlignment.Center;
            content = big;
        }
        else
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(TileChrome.Label(_alarms.RingingNow is not null ? "ALARM · TAP TO STOP" : "ALARM",
                _alarms.RingingNow is not null ? TileChrome.Red : null));
            big.Margin = new Thickness(0, 4, 0, 2);
            stack.Children.Add(big);
            var snoozed = _alarms.SnoozedUntil;
            stack.Children.Add(TileChrome.Label(
                snoozed is { } z ? "SNOOZED · " + AlarmRules.Until(z, now)
                : next is { } x ? $"{AlarmRules.Until(x.At, now)} · {AlarmRules.DaysLabel(x.Alarm.Days)}"
                : "NO ALARMS · TAP TO ADD"));
            content = stack;
        }
        _glow = new Border { BorderBrush = TileChrome.Red, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(18), Margin = new Thickness(-10), Opacity = 0 };
        _host.Child = new Grid { Children = { content, _glow } };
        if (_alarms.RingingNow is not null) { _glow.Opacity = 0.9; _flash.Start(); }
        else _flash.Stop();
    }

    public void Dispose()
    {
        _minute.Stop();
        _flash.Stop();
        _alarms.Changed -= Fill;
        _popup?.CloseIfOpen();
    }
}

/// <summary>
/// The alarm editor (Plan 9): a row per alarm (time box — red when it can't be read —, S M T W T F S chips, on/off, ✕)
/// and ＋ Add (the next full hour, once, on). Every change is saved at once, starting from the service's current list
/// (review: never a copy taken when the editor opened, which would undo a ring or a sync).
/// </summary>
public sealed class AlarmEditorPopup : EditorPopupWindow
{
    private static readonly string[] DayLetters = ["S", "M", "T", "W", "T", "F", "S"];
    private readonly AlarmService _alarms;
    private readonly StackPanel _rows;

    public AlarmEditorPopup(AlarmService alarms) : this(alarms, new StackPanel()) { }

    private AlarmEditorPopup(AlarmService alarms, StackPanel rows) : base(Layout(rows, out var add), 380)
    {
        _alarms = alarms;
        _rows = rows;
        add.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var at = DateTime.Now.AddHours(1);
            _alarms.Set([.. _alarms.Alarms, new Alarm(Guid.NewGuid().ToString("N"), at.Hour, 0, 0, true)]);
            Rebuild();
        };
        Rebuild();
    }

    private static FrameworkElement Layout(StackPanel rows, out Border add)
    {
        var head = TileChrome.Label("ALARMS · RING WHILE THE PC IS AWAKE");
        head.Margin = new Thickness(0, 0, 0, 10);
        add = new Border
        {
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 6, 0, 0),
            Child = TileChrome.Body("＋ Add", 13, FontWeights.SemiBold),
        };
        return new StackPanel { Children = { head, rows, add } };
    }

    // One change to the alarm with this id, applied to the current list and saved; false when it is gone.
    private bool Edit(string id, Func<Alarm, Alarm> change)
    {
        var list = _alarms.Alarms.ToList();
        var i = list.FindIndex(x => x.Id == id);
        if (i < 0) return false;
        list[i] = change(list[i]);
        _alarms.Set(list);
        return true;
    }

    private Alarm? Current(string id) => _alarms.Alarms.FirstOrDefault(x => x.Id == id);

    /// <summary>Sets the time from what was typed; false (nothing saved) when it can't be read.</summary>
    public bool SetTime(string id, string text) =>
        AlarmRules.ParseTime(text) is { } t && Edit(id, a => a with { Hour = t.H, Minute = t.M });

    private void Rebuild()
    {
        _rows.Children.Clear();
        if (_alarms.Alarms.Count == 0) _rows.Children.Add(TileChrome.Body("No alarms yet.", 13, FontWeights.Normal, TileChrome.Dim));
        foreach (var alarm in _alarms.Alarms) _rows.Children.Add(Row(alarm));
    }

    private FrameworkElement Row(Alarm alarm)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var box = new TextBox
        {
            Text = AlarmLook.Time(alarm), Width = 64, FontFamily = TileChrome.Font, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = TileChrome.Text, CaretBrush = TileChrome.Text, Background = TileChrome.Frozen(0x22, 0x22, 0x22),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var bad = TileChrome.FrozenArgb(0x40, 0xD7, 0x19, 0x21);
        var normal = box.Background;
        box.TextChanged += (_, _) =>
        {
            // Red text on a red tint while it can't be read (the focused border colour is the template's, not ours).
            var ok = SetTime(alarm.Id, box.Text);
            box.Foreground = ok ? TileChrome.Text : TileChrome.Red;
            box.Background = ok ? normal : bad;
        };
        DockPanel.SetDock(box, System.Windows.Controls.Dock.Left);
        row.Children.Add(box);

        var remove = new TextBlock { Text = "✕", FontSize = 12, Foreground = TileChrome.Dim, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _alarms.Set([.. _alarms.Alarms.Where(x => x.Id != alarm.Id)]);
            Rebuild();
        };
        DockPanel.SetDock(remove, System.Windows.Controls.Dock.Right);
        row.Children.Add(remove);

        var toggle = new Border { Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = TileChrome.Toggle(alarm.On) };
        toggle.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (Edit(alarm.Id, a => a with { On = !a.On }) && Current(alarm.Id) is { } now) toggle.Child = TileChrome.Toggle(now.On);
        };
        DockPanel.SetDock(toggle, System.Windows.Controls.Dock.Right);
        row.Children.Add(toggle);

        var days = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        for (var d = 0; d < 7; d++)
        {
            var bit = 1 << d;
            var chip = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand };
            var letter = new TextBlock { Text = DayLetters[d], FontFamily = TileChrome.Font, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            chip.Child = letter;
            void Paint(bool on)
            {
                chip.Background = on ? TileChrome.Text : TileChrome.Frozen(0x2A, 0x2A, 0x2A);
                letter.Foreground = on ? TileChrome.Dark : TileChrome.Dim;
            }
            Paint((alarm.Days & bit) != 0);
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (Edit(alarm.Id, a => a with { Days = a.Days ^ bit }) && Current(alarm.Id) is { } now) Paint((now.Days & bit) != 0);
            };
            days.Children.Add(chip);
        }
        row.Children.Add(days);
        return row;
    }
}

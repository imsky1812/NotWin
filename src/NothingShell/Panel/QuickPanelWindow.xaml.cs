using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NothingShell.Native;

namespace NothingShell.Panel;

/// <summary>
/// Quick settings + notifications (spec §6.3): bottom-right above the dock, 400 wide, activatable, topmost. Closes on
/// Esc or when it loses focus. Sections live in <see cref="TilesHost"/>, <see cref="SlidersHost"/> and
/// <see cref="NotificationsHost"/> and are filled by their controllers.
/// </summary>
public partial class QuickPanelWindow : Window
{
    private const double RightGap = 24, BottomGap = 84; // DIPs: mockup .qsp right 24 / bottom 84 (above the dock)
    private bool _hiding;
    private bool _allowClose;

    /// <summary>Esc, focus lost, or Alt+F4: the panel should close.</summary>
    public event Action? Dismissed;

    public QuickPanelWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Dismissed?.Invoke(); } };
        Deactivated += (_, _) => { if (IsVisible && !_hiding) Dismissed?.Invoke(); };
        Closing += (_, e) => { if (_allowClose) return; e.Cancel = true; Dismissed?.Invoke(); }; // Alt+F4 = dismiss
        SizeChanged += (_, _) => PlaceBottomRight();
    }

    public void ShowAndFocus()
    {
        _hiding = false;
        Opacity = 0;
        PanelShift.Y = 8;
        Show();
        PlaceBottomRight();
        ForegroundNative.Activate(new WindowInteropHelper(this).Handle);
        Activate();
        var ease = new KeySpline(0.2, 0.8, 0.2, 1);
        var dur = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200));
        BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames { KeyFrames = { new SplineDoubleKeyFrame(1, dur, ease) } });
        PanelShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimationUsingKeyFrames { KeyFrames = { new SplineDoubleKeyFrame(0, dur, ease) } });
    }

    public void HideNow()
    {
        _hiding = true;
        BeginAnimation(OpacityProperty, null);
        PanelShift.BeginAnimation(TranslateTransform.YProperty, null);
        Hide();
    }

    public void CloseForGood()
    {
        _allowClose = true;
        Close();
    }

    private void PlaceBottomRight()
    {
        Left = SystemParameters.PrimaryScreenWidth - Width - RightGap;
        Top = SystemParameters.PrimaryScreenHeight - BottomGap - ActualHeight;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NotWin.Panel;

/// <summary>
/// The panel's rounded slider (mockup .track): a 28 px #262626 track with a #F2F2F2 fill. Drag or click to set 0-100.
/// <see cref="ValueChanged"/> fires while dragging (live) only for user input, not for <see cref="SetValueSilently"/>.
/// </summary>
public sealed class PanelSlider : Border
{
    private readonly Border _fill;
    private int _value;
    private bool _dragging;

    public event Action<int>? ValueChanged;

    public PanelSlider()
    {
        Height = 28;
        CornerRadius = new CornerRadius(14);
        Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        _fill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            IsHitTestVisible = false,
        };
        Child = _fill;
        SizeChanged += (_, _) => Redraw();
        MouseLeftButtonDown += (_, e) => { _dragging = CaptureMouse(); FromMouse(e); e.Handled = true; };
        MouseMove += (_, e) => { if (_dragging) FromMouse(e); };
        MouseLeftButtonUp += (_, _) => { _dragging = false; ReleaseMouseCapture(); };
        LostMouseCapture += (_, _) => _dragging = false;
    }

    public int Value => _value;

    /// <summary>Updates the fill from outside (e.g. the system volume changed) without raising <see cref="ValueChanged"/>.</summary>
    public void SetValueSilently(int value)
    {
        if (_dragging) return; // the user's drag wins
        _value = Math.Clamp(value, 0, 100);
        Redraw();
    }

    private void FromMouse(MouseEventArgs e)
    {
        if (ActualWidth <= 0) return;
        var v = (int)Math.Round(Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1) * 100);
        if (v == _value) return;
        _value = v;
        Redraw();
        ValueChanged?.Invoke(v);
    }

    private void Redraw() => _fill.Width = ActualWidth * _value / 100.0;
}

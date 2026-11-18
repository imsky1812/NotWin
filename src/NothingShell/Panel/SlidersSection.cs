using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NothingShell.Core.Panel;
using NothingShell.Dock;

namespace NothingShell.Panel;

/// <summary>
/// The panel's slider rows (mockup .sl): volume + output chip, and brightness (when the display supports it).
/// Rows are built once; values refresh each time the panel opens and follow external changes while it is open.
/// </summary>
public sealed class SlidersSection
{
    private readonly TrayStatusService _tray;
    private readonly AudioOutputService _outputs = new();
    private readonly PanelSlider _volume = new();
    private readonly Border _chip;
    private readonly TextBlock _chipText;
    private readonly FrameworkElement _brightnessRow;
    private readonly PanelSlider _brightness = new();
    private readonly BrightnessService _brightnessService;

    public SlidersSection(StackPanel host, TrayStatusService tray, BrightnessService brightness)
    {
        _tray = tray;
        _brightnessService = brightness;

        _chipText = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("Font.GeistMono"),
            FontWeight = FontWeights.Medium, FontSize = 10, Foreground = (Brush)Application.Current.FindResource("Brush.Text"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _chip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 6, 10, 6), Child = _chipText, Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Switch output device",
        };
        _chip.MouseLeftButtonUp += (_, _) => SwitchOutput();

        host.Children.Add(Row(SpeakerGlyph(), _volume, _chip));
        _brightnessRow = Row(SunGlyph(), _brightness, null);
        host.Children.Add(_brightnessRow);

        _volume.ValueChanged += v => _tray.SetVolume(v);
        _brightness.ValueChanged += v => _brightnessService.Set(v);
        _tray.PropertyChanged += OnTrayChanged;
        // Headphones plugged in/out while the panel is open: re-read the devices so the chip never shows a stale one.
        _tray.DefaultDeviceChanged += () => { _outputs.Refresh(); UpdateChip(); };
    }

    /// <summary>Called when the panel opens: re-read devices, volume and brightness.</summary>
    public void Refresh()
    {
        _volume.SetValueSilently(_tray.VolumePercent);
        _outputs.Refresh();
        UpdateChip();
        _ = RefreshBrightnessAsync();
    }

    private async Task RefreshBrightnessAsync()
    {
        var level = await _brightnessService.GetAsync();
        _brightnessRow.Visibility = level is null ? Visibility.Collapsed : Visibility.Visible;
        if (level is { } l) _brightness.SetValueSilently(l);
    }

    private void OnTrayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayStatusService.VolumePercent)) _volume.SetValueSilently(_tray.VolumePercent);
    }

    private void SwitchOutput()
    {
        _outputs.SwitchToNext();
        UpdateChip();
    }

    private void UpdateChip()
    {
        var current = _outputs.Current;
        _chipText.Text = AudioOutputs.ShortName(current?.Name ?? "Output").ToUpperInvariant();
        var canSwitch = _outputs.Outputs.Count > 1;
        _chip.Opacity = canSwitch ? 1 : 0.55;
        _chip.IsHitTestVisible = canSwitch;
    }

    private static Grid Row(FrameworkElement icon, PanelSlider slider, FrameworkElement? chip)
    {
        var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        icon.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(slider, 1);
        g.Children.Add(icon);
        g.Children.Add(slider);
        if (chip is not null)
        {
            chip.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(chip, 2);
            g.Children.Add(chip);
        }
        return g;
    }

    private static FrameworkElement Glyph(string data) => new Viewbox
    {
        Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center,
        Child = new Canvas
        {
            Width = 24, Height = 24,
            Children =
            {
                new Path
                {
                    Data = Geometry.Parse(data), Stroke = (Brush)Application.Current.FindResource("Brush.Text"), StrokeThickness = 1.8,
                    StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                },
            },
        },
    };

    private static FrameworkElement SpeakerGlyph() => Glyph("M4,9 L8,9 L13,5 L13,19 L8,15 L4,15 Z M16.5,8.5 A5,5 0 0 1 16.5,15.5 M19,6 A8.5,8.5 0 0 1 19,18");

    private static FrameworkElement SunGlyph() => Glyph("M12,8 A4,4 0 1 1 11.99,8 M12,2 L12,4 M12,20 L12,22 M2,12 L4,12 M20,12 L22,12 M4.9,4.9 L6.3,6.3 M17.7,17.7 L19.1,19.1 M4.9,19.1 L6.3,17.7 M17.7,6.3 L19.1,4.9");
}

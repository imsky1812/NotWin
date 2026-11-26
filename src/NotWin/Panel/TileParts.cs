using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NotWin.Panel;

/// <summary>
/// A quick-settings tile (mockup .tile): 60 tall, radius 20, Shell.Tile (on: Shell.OnBg — light in dark mode, ink in light). The main area
/// toggles; the ▸ area on the right expands the tile's list.
/// </summary>
public sealed class Tile : Border
{
    private readonly TextBlock _title, _sub;
    private readonly Path _glyph, _chevron;

    public event Action? Toggled;
    public event Action? Expanded;

    public Tile(string title, string glyphData)
    {
        Height = 60;
        CornerRadius = new CornerRadius(20);
        ClipToBounds = true;

        _glyph = new Path { Data = Geometry.Parse(glyphData), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        _title = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Medium };
        _sub = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"), FontSize = 11, FontWeight = FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _sub } };
        var icon = new Viewbox { Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0), Child = new Canvas { Width = 24, Height = 24, Children = { _glyph } } };
        var main = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        main.Children.Add(icon);
        main.Children.Add(text);
        main.Margin = new Thickness(14, 0, 6, 0);
        main.MouseLeftButtonUp += (_, _) => Toggled?.Invoke();

        _chevron = new Path { Data = Geometry.Parse("M9,6 L15,12 L9,18"), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var more = new Border
        {
            Width = 34, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Show list",
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)), BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new Viewbox { Width = 14, Height = 14, Child = new Canvas { Width = 24, Height = 24, Children = { _chevron } } },
        };
        more.MouseLeftButtonUp += (_, _) => Expanded?.Invoke();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(more, 1);
        grid.Children.Add(main);
        grid.Children.Add(more);
        Child = grid;
        SetOn(false, "");
    }

    // Colours are theme resources (Shell.*, user 2026-11-26), so an open panel follows light / dark mode at once.
    public void SetOn(bool on, string subline)
    {
        SetResourceReference(BackgroundProperty, on ? "Shell.OnBg" : "Shell.Tile");
        var fg = on ? "Shell.OnText" : "Shell.Text";
        _title.SetResourceReference(TextBlock.ForegroundProperty, fg);
        _sub.SetResourceReference(TextBlock.ForegroundProperty, on ? "Shell.OnDim" : "Shell.Dim");
        _sub.Text = subline.ToUpperInvariant();
        _glyph.SetResourceReference(Shape.StrokeProperty, fg);
        _chevron.SetResourceReference(Shape.StrokeProperty, fg);
    }
}

/// <summary>A tile's expanded list (mockup .list): rounded #0D FFFFFF box, rows with a right-hand action label.</summary>
public sealed class TileList : Border
{
    private readonly StackPanel _rows = new();

    public TileList()
    {
        SetResourceReference(BackgroundProperty, "Shell.Card");
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(6);
        Margin = new Thickness(0, 0, 0, 8);
        Child = _rows;
        Visibility = Visibility.Collapsed;
    }

    public void Clear() => _rows.Children.Clear();

    public void AddRow(string text, string? action, Action? onClick, bool dim = false)
    {
        var label = new TextBlock
        {
            Text = action?.ToUpperInvariant() ?? "", FontFamily = (FontFamily)Application.Current.FindResource("Font.Mono"),
            FontSize = 11, FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Shell.Dim");
        DockPanel.SetDock(label, System.Windows.Controls.Dock.Right);
        var name = new TextBlock
        {
            Text = text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, dim ? "Shell.Dim" : "Shell.Text");
        var row = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 9, 10, 9), Background = Brushes.Transparent,
            Cursor = onClick is null ? Cursors.Arrow : Cursors.Hand, Child = new DockPanel { Children = { label, name } },
        };
        row.MouseEnter += (_, _) => { if (onClick is not null) row.SetResourceReference(BackgroundProperty, "Shell.Hover"); };
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        if (onClick is not null) row.MouseLeftButtonUp += (_, _) => onClick();
        _rows.Children.Add(row);
    }
}

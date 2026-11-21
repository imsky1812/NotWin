using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotWin.Core.Notch;

namespace NotWin.Notch.Dashboard.Tiles;

/// <summary>
/// A tool button (mockup <c>NR.tool</c>): 1×1 icon 20 + mono label; 2×1 a 36 px #222 circle with the icon and the name
/// (13 SemiBold). A click runs the tool.
/// </summary>
public sealed class ToolTile(TileKind kind, Action<TileKind> run) : ITileView
{
    public TileKind Kind => kind;

    public FrameworkElement Build(TileSize size)
    {
        var title = PlaceholderTile.Title(kind);
        var icon = PlaceholderTile.IconName(kind);
        Border tile;
        if (size == TileSize.Wide)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = TileChrome.Frozen(0x22, 0x22, 0x22),
                Margin = new Thickness(0, 0, 12, 0), Child = TileChrome.Icon(icon, 20),
            });
            var name = TileChrome.Body(title, 13, FontWeights.SemiBold);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            tile = TileChrome.Tile(row);
        }
        else
        {
            var stack = new StackPanel();
            var ic = TileChrome.Icon(icon, 20);
            ic.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(ic);
            var label = TileChrome.Label(title);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            tile = TileChrome.Tile(stack, center: true);
        }
        tile.Cursor = Cursors.Hand;
        tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(kind); };
        return tile;
    }

    public void Opened() { }
    public void Closed() { }
    public void Dispose() { }
}

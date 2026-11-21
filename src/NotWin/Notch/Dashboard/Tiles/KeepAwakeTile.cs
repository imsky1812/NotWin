using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotWin.Core.Notch;
using NotWin.Power;

namespace NotWin.Notch.Dashboard.Tiles;

/// <summary>
/// Keep awake tile (mockup <c>NR.awake</c>): 1×1 on-state fill #F2F2F2 with a dark cup and "AWAKE", else a white cup
/// and "SLEEP OK"; 2×1 cup, "Keep awake" + ON/OFF and a toggle. A click anywhere on the tile toggles.
/// </summary>
public sealed class KeepAwakeTile : ITileView
{
    private readonly KeepAwakeService _awake;
    private TileSize _size;
    private Border? _tile;

    public KeepAwakeTile(KeepAwakeService awake)
    {
        _awake = awake;
        _awake.Changed += Fill;
    }

    public TileKind Kind => TileKind.KeepAwake;

    public FrameworkElement Build(TileSize size)
    {
        _size = size;
        var host = new Border { Cursor = Cursors.Hand };
        host.MouseLeftButtonUp += (_, e) => { e.Handled = true; _awake.Set(!_awake.On); };
        _tile = host;
        Fill();
        return host;
    }

    private void Fill()
    {
        if (_tile is null) return;
        var on = _awake.On;
        if (_size == TileSize.One)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = TileChrome.Icon("cup", 20, on ? TileChrome.Dark : TileChrome.Text);
            icon.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(icon);
            var label = TileChrome.Label(on ? "Awake" : "Sleep ok", on ? TileChrome.Dark : null);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            _tile.Child = TileChrome.Tile(stack, center: true, background: on ? TileChrome.Text : null);
            return;
        }
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var cup = TileChrome.Icon("cup", 20);
        cup.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(cup, System.Windows.Controls.Dock.Left);
        row.Children.Add(cup);
        var toggle = TileChrome.Toggle(on);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(toggle, System.Windows.Controls.Dock.Right);
        row.Children.Add(toggle);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(TileChrome.Body("Keep awake", 13, FontWeights.SemiBold));
        text.Children.Add(TileChrome.Label(on ? "On" : "Off"));
        row.Children.Add(text);
        _tile.Child = TileChrome.Tile(row);
    }

    public void Opened() => Fill();
    public void Closed() { }
    public void Dispose() => _awake.Changed -= Fill;
}

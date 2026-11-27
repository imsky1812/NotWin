using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotWin.Core.Notch;
using NotWin.Power;

namespace NotWin.Notch.Dashboard.Tiles;

/// <summary>
/// Keep awake tile (mockup <c>NR.awake</c>): 1×1 on-state red (Plan 13) with a white cup and "AWAKE", else a white cup
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
            // On is red, like the desktop toggle tiles (user 2026-11-26: "when a toggle is on mark it red").
            var white = DashChrome.Frozen(0xFF, 0xFF, 0xFF);
            var icon = DashChrome.Icon("cup", 20, on ? white : DashChrome.Text);
            icon.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(icon);
            var label = DashChrome.Label(on ? "Awake" : "Sleep ok", on ? white : null);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            _tile.Child = DashChrome.Tile(stack, center: true, background: on ? DashChrome.Red : null);
            return;
        }
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var cup = DashChrome.Icon("cup", 20);
        cup.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(cup, System.Windows.Controls.Dock.Left);
        row.Children.Add(cup);
        var toggle = DashChrome.Toggle(on);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(toggle, System.Windows.Controls.Dock.Right);
        row.Children.Add(toggle);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(DashChrome.Body("Keep awake", 13, FontWeights.SemiBold));
        text.Children.Add(DashChrome.Label(on ? "On" : "Off"));
        row.Children.Add(text);
        _tile.Child = DashChrome.Tile(row);
    }

    public void Opened() => Fill();
    public void Closed() { }
    public void Dispose() => _awake.Changed -= Fill;
}

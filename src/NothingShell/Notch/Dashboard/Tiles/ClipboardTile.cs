using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NothingShell.Clips;
using NothingShell.Core.Notch;

namespace NothingShell.Notch.Dashboard.Tiles;

/// <summary>
/// Clipboard tile (mockup <c>NR.clip</c>, <c>.clip</c>): 1×1 icon, count in dots and label; 2×1 the newest item;
/// 2×2 up to three items. Clicking an item copies it again. The expand icon replaces the grid with the whole list
/// (✕ per row, CLEAR), which holds the notch open until "‹ Clipboard".
/// </summary>
public sealed class ClipboardTile : ITileView
{
    private static readonly Brush First = TileChrome.Frozen(0x22, 0x22, 0x22), Hover = TileChrome.Frozen(0x26, 0x26, 0x26);
    private static readonly Brush Muted = TileChrome.Frozen(0xCC, 0xCC, 0xCC);
    private readonly ClipboardService _clips;
    private readonly Func<IDashboardHost> _host;
    private TileSize _size;
    private Border? _tile;
    private StackPanel? _list;
    private bool _open;

    public ClipboardTile(ClipboardService clips, Func<IDashboardHost> host)
    {
        _clips = clips;
        _host = host;
        _clips.Changed += OnChanged;
    }

    public TileKind Kind => TileKind.Clipboard;

    public FrameworkElement Build(TileSize size)
    {
        _size = size;
        _tile = TileChrome.Tile(new Grid(), center: size == TileSize.One);
        Fill();
        return _tile;
    }

    private void OnChanged()
    {
        if (!_open) return;
        Fill();
        if (_list is not null) FillList();
    }

    private void Fill()
    {
        if (_tile is null) return;
        var items = _clips.History.Items;
        if (_size == TileSize.One)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = TileChrome.Icon("clip", 20);
            icon.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(icon);
            stack.Children.Add(new DotMatrix
            {
                Text = Math.Min(items.Count, 99).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Dot = 3, Gap = 1, Fill = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center,
            });
            var label = TileChrome.Label("Clipboard");
            label.Margin = new Thickness(0, 4, 0, 0);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            _tile.Child = stack;
            return;
        }
        var panel = new DockPanel();
        var expand = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = TileChrome.Icon("expand", 16, TileChrome.Dim) };
        expand.MouseLeftButtonUp += (_, e) => { e.Handled = true; Expand(); };
        var header = TileChrome.Header("clip", "Clipboard", expand);
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        panel.Children.Add(header);
        var rows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var show = items.Take(_size == TileSize.Wide ? 1 : 3).ToList();
        if (show.Count == 0) rows.Children.Add(TileChrome.Label("Nothing copied yet"));
        for (var i = 0; i < show.Count; i++) rows.Children.Add(Row(show[i], i == 0, null));
        panel.Children.Add(rows);
        _tile.Child = panel;
    }

    // One clipboard line (mockup .clip div): mono 11, one line, newest on #222; click copies it again.
    private Border Row(string text, bool first, Action? remove)
    {
        var line = new TextBlock
        {
            Text = OneLine(text), FontFamily = TileChrome.Mono, FontSize = 11, Foreground = first ? TileChrome.Text : Muted,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        var content = new DockPanel();
        if (remove is not null)
        {
            var x = new TextBlock
            {
                Text = "✕", FontSize = 10, Foreground = TileChrome.Dim, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent,
            };
            x.MouseLeftButtonUp += (_, e) => { e.Handled = true; remove(); };
            DockPanel.SetDock(x, System.Windows.Controls.Dock.Right);
            content.Children.Add(x);
        }
        content.Children.Add(line);
        var row = new Border
        {
            Child = content, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 2),
            Background = first ? First : Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = null,
        };
        var normal = row.Background;
        row.MouseEnter += (_, _) => row.Background = Hover;
        row.MouseLeave += (_, _) => row.Background = normal;
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; _clips.Copy(text); };
        return row;
    }

    private static string OneLine(string text)
    {
        var t = text.Trim();
        var cut = t.IndexOfAny(['\r', '\n']);
        if (cut >= 0) t = t[..cut] + " …";
        return t.Length > 300 ? t[..300] : t;
    }

    private void Expand()
    {
        var root = new DockPanel();
        var back = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        back.Children.Add(TileChrome.Body("‹  Clipboard", 13, FontWeights.SemiBold));
        back.MouseLeftButtonUp += (_, e) => { e.Handled = true; _list = null; _host().CloseOverlay(); };
        var clear = TileChrome.Label("Clear");
        clear.Cursor = Cursors.Hand;
        clear.Background = Brushes.Transparent;
        clear.MouseLeftButtonUp += (_, e) => { e.Handled = true; _clips.Clear(); };
        var head = new DockPanel { Margin = new Thickness(4, 0, 4, 8) };
        DockPanel.SetDock(clear, System.Windows.Controls.Dock.Right);
        head.Children.Add(clear);
        head.Children.Add(back);
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        root.Children.Add(head);
        _list = new StackPanel();
        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (Application.Current.TryFindResource("ScrollBar.Thin") is Style thin) scroll.Resources[typeof(ScrollBar)] = thin;
        root.Children.Add(scroll);
        FillList();
        _host().ShowOverlay(root);
    }

    private void FillList()
    {
        if (_list is null) return;
        _list.Children.Clear();
        var items = _clips.History.Items;
        if (items.Count == 0) _list.Children.Add(TileChrome.Label("Nothing copied yet"));
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            _list.Children.Add(Row(items[i], i == 0, () => _clips.Remove(index)));
        }
    }

    public void Opened()
    {
        _open = true;
        Fill();
    }

    public void Closed()
    {
        _open = false;
        _list = null;
    }

    public void Dispose() => _clips.Changed -= OnChanged;
}

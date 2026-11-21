using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Core.Notch;

namespace NothingShell.Notch.Dashboard.Tiles;

/// <summary>
/// Notes tile (mockup <c>NR.notes</c>, spec §6.4): one plain-text note, saved 500 ms after the last keystroke, when
/// the notch closes and on exit. 1×1 icon + label (click opens a big editor in place of the grid); 2×1 one line;
/// 2×2 multi-line. Clicking a text box makes the notch take the keyboard and hold open; Esc closes it.
/// The note text is never logged.
/// </summary>
public sealed class NotesTile : ITileView
{
    private static readonly Brush PlaceholderBrush = TileChrome.Frozen(0x66, 0x66, 0x66);
    private readonly SharedNotes _notes;
    private readonly Func<IDashboardHost> _host;
    private readonly DispatcherTimer _save;
    private string _text;
    private bool _dirty;

    public NotesTile(SharedNotes notes, Func<IDashboardHost> host)
    {
        _notes = notes;
        _host = host;
        _text = notes.Text;
        _notes.Changed += OnShared;
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _save.Tick += (_, _) => Flush();
    }

    public TileKind Kind => TileKind.Notes;

    public FrameworkElement Build(TileSize size)
    {
        if (size == TileSize.One)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = TileChrome.Icon("notes", 20);
            icon.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(icon);
            var label = TileChrome.Label("Notes");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            var tile = TileChrome.Tile(stack, center: true);
            tile.Cursor = Cursors.Hand;
            tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenEditor(); };
            return tile;
        }
        var panel = new DockPanel();
        var header = TileChrome.Header("notes", "Notes");
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(Editor(multiLine: size == TileSize.Big));
        return TileChrome.Tile(panel);
    }

    private void OpenEditor()
    {
        var root = new DockPanel();
        var back = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 4, 8) };
        back.Children.Add(TileChrome.Body("‹  Notes", 13, FontWeights.SemiBold));
        back.MouseLeftButtonUp += (_, e) => { e.Handled = true; _host().CloseOverlay(); };
        DockPanel.SetDock(back, System.Windows.Controls.Dock.Top);
        root.Children.Add(back);
        var editor = Editor(multiLine: true);
        root.Children.Add(new Border
        {
            Background = TileChrome.TileBrush, CornerRadius = new CornerRadius(22), Padding = new Thickness(14, 6, 14, 10), Child = editor,
        });
        _host().ShowOverlay(root);
    }

    // A borderless text box over a "Write something" placeholder (mockup textarea / input.n).
    private FrameworkElement Editor(bool multiLine)
    {
        var box = new TextBox
        {
            Text = _text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TileChrome.Text,
            CaretBrush = TileChrome.Text,
            SelectionBrush = TileChrome.Frozen(0x55, 0x55, 0x55),
            FontFamily = TileChrome.Font,
            FontSize = 13,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 0),
            AcceptsReturn = multiLine,
            AcceptsTab = false,
            AllowDrop = false, // a WPF drop target would register OLE on the notch and block WM_DROPFILES (shelf)
            TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiLine ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            VerticalAlignment = multiLine ? VerticalAlignment.Stretch : VerticalAlignment.Top,
            MaxLines = multiLine ? int.MaxValue : 1, // 2x1 is a one-line field (mockup input.n), even if the note has more
        };
        var placeholder = new TextBlock
        {
            Text = "Write something", FontFamily = TileChrome.Font, FontSize = 13, Foreground = PlaceholderBrush,
            Margin = new Thickness(2, 8, 0, 0), IsHitTestVisible = false,
            Visibility = _text.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        box.TextChanged += (_, _) =>
        {
            _text = box.Text;
            placeholder.Visibility = _text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _dirty = true;
            _save.Stop();
            _save.Start();
        };
        // The notch never takes focus on its own: ask for the keyboard first, then focus the box.
        box.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (box.IsKeyboardFocused) return;
            _host().BeginTyping();
            box.Dispatcher.BeginInvoke(() => { box.Focus(); Keyboard.Focus(box); }, DispatcherPriority.Input);
        };
        box.LostKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is TextBox) return; // moving between note boxes
            Flush();
            _host().EndTyping();
        };
        return new Grid { Children = { box, placeholder } };
    }

    private void Flush()
    {
        _save.Stop();
        if (!_dirty) return;
        _dirty = false;
        _notes.Set(_text, this); // the desktop Notes widget shows it too (Plan 9)
    }

    public void Opened() { }

    public void Closed() => Flush();

    // Changed somewhere else (the desktop widget, a sync): shown next time the tile is built, unless typing here.
    private void OnShared(object? from)
    {
        if (ReferenceEquals(from, this) || _dirty) return;
        _text = _notes.Text;
    }

    public void Dispose()
    {
        Flush();
        _notes.Changed -= OnShared;
    }
}

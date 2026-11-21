using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Core.Widgets;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// Notes (Plan 9): the notch's note on the desktop. "NOTES" and the text (2×1: three lines; 2×2 / 4×2: as much as
/// fits); "TAP TO WRITE" when empty. A click opens <see cref="NotesEditorPopup"/> next to the widget (widgets cannot
/// take the keyboard). Updates whenever the note changes anywhere.
/// </summary>
public sealed class NotesWidget : IDesktopWidget
{
    private readonly SharedNotes _notes;
    private Border? _host;
    private WidgetSize _size;
    private RectI _screen;
    private NotesEditorPopup? _popup;

    public NotesWidget(SharedNotes notes)
    {
        _notes = notes;
        _notes.Changed += OnChanged;
    }

    public WidgetKind Kind => WidgetKind.Notes;

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _host.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenEditor(); };
        Fill();
        return _host;
    }

    public void Placed(RectI screen, RectI monitor) => _screen = screen;

    private void OnChanged(object? from) => Fill();

    private void Fill()
    {
        if (_host is null) return;
        var panel = new DockPanel();
        var head = TileChrome.Label("NOTES");
        head.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        panel.Children.Add(head);
        var text = _notes.Text.Trim();
        var body = TileChrome.Body(text.Length == 0 ? "TAP TO WRITE" : text, 13, FontWeights.Normal, text.Length == 0 ? TileChrome.Dim : null);
        body.TextWrapping = TextWrapping.Wrap;
        body.TextTrimming = TextTrimming.CharacterEllipsis;
        body.LineHeight = 18;
        body.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        if (_size.H == 1) body.MaxHeight = 3 * 18;
        panel.Children.Add(body);
        _host.Child = new Border { ClipToBounds = true, Child = panel };
    }

    private void OpenEditor()
    {
        _popup?.CloseIfOpen();
        _popup = new NotesEditorPopup(_notes);
        _popup.ShowNextTo(_screen);
    }

    public void Dispose()
    {
        _notes.Changed -= OnChanged;
        _popup?.CloseIfOpen();
    }
}

/// <summary>The Notes widget's editor: a multi-line box that saves 500 ms after typing stops and on closing.</summary>
public sealed class NotesEditorPopup : EditorPopupWindow
{
    private readonly SharedNotes _notes;
    private readonly TextBox _box;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public NotesEditorPopup(SharedNotes notes) : this(notes, MakeBox(notes.Text)) { }

    private NotesEditorPopup(SharedNotes notes, TextBox box) : base(Layout(box), 320)
    {
        _notes = notes;
        _box = box;
        _save.Tick += (_, _) => Save();
        _box.TextChanged += (_, _) => { _save.Stop(); _save.Start(); };
        Closing += (_, _) => Save();
        Loaded += (_, _) => { _box.Focus(); _box.CaretIndex = _box.Text.Length; };
    }

    private void Save()
    {
        _save.Stop();
        _notes.Set(_box.Text, this);
    }

    private static TextBox MakeBox(string text) => new()
    {
        Text = text, Height = 220, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        Foreground = TileChrome.Text, CaretBrush = TileChrome.Text, SelectionBrush = TileChrome.Frozen(0x55, 0x55, 0x55),
        FontFamily = TileChrome.Font, FontSize = 13, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static FrameworkElement Layout(TextBox box)
    {
        var panel = new DockPanel();
        var head = TileChrome.Label("NOTES · ESC TO CLOSE");
        head.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        panel.Children.Add(head);
        panel.Children.Add(box);
        return panel;
    }
}

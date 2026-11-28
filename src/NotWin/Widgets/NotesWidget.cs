using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotWin.Core;
using NotWin.Core.Widgets;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

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
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var pencil = TileChrome.Icon("pencil", 13, WidgetTheme.Dim);
        pencil.Margin = new Thickness(0, 0, 7, 0);
        pencil.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(pencil);
        var headLabel = WidgetTheme.Label("NOTES");
        headLabel.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(headLabel);
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        panel.Children.Add(head);
        var text = _notes.Text.Trim();
        var body = WidgetTheme.Body(text.Length == 0 ? "TAP TO WRITE" : text, 13, FontWeights.Normal, text.Length == 0 ? WidgetTheme.Dim : null);
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
    private bool _dirty, _quiet;

    public NotesEditorPopup(SharedNotes notes) : this(notes, MakeBox(notes.Text)) { }

    private NotesEditorPopup(SharedNotes notes, TextBox box) : base(Layout(box), 320, themed: true)
    {
        _notes = notes;
        _box = box;
        _save.Tick += (_, _) => SaveIfEdited();
        _box.TextChanged += (_, _) =>
        {
            if (_quiet) return;
            _dirty = true;
            _save.Stop();
            _save.Start();
        };
        _notes.Changed += OnShared;
        Closing += (_, _) => SaveIfEdited();
        Closed += (_, _) => _notes.Changed -= OnShared;
        Loaded += (_, _) => { _box.Focus(); _box.CaretIndex = _box.Text.Length; };
    }

    /// <summary>The text in the box (tests).</summary>
    public string EditorText => _box.Text;

    /// <summary>
    /// Saves what was typed. Nothing is written when nothing was typed (review: closing must never put back an old
    /// copy over a note that changed elsewhere, e.g. a sync).
    /// </summary>
    public void SaveIfEdited()
    {
        _save.Stop();
        if (!_dirty) return;
        _dirty = false;
        _notes.Set(_box.Text, this);
    }

    // The note changed elsewhere (the notch tile, a sync): shown here unless something is being typed.
    private void OnShared(object? from)
    {
        if (ReferenceEquals(from, this) || _dirty) return;
        _quiet = true;
        try { _box.Text = _notes.Text; }
        finally { _quiet = false; }
    }

    private static TextBox MakeBox(string text) => new()
    {
        Text = text, Height = 220, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        Foreground = WidgetTheme.Text, CaretBrush = WidgetTheme.Text, SelectionBrush = TileChrome.Frozen(0x88, 0x88, 0x88),
        FontFamily = TileChrome.Font, FontSize = 13, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static FrameworkElement Layout(TextBox box)
    {
        var panel = new DockPanel();
        var head = WidgetTheme.Label("NOTES · ESC TO CLOSE");
        head.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        panel.Children.Add(head);
        panel.Children.Add(box);
        return panel;
    }
}

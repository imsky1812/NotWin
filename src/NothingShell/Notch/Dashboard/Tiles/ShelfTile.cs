using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using Path = System.IO.Path;
using NothingShell.Core.Notch;
using NothingShell.Core.Shelving;
using NothingShell.Dock;

namespace NothingShell.Notch.Dashboard.Tiles;

/// <summary>
/// File shelf tile (mockup <c>NR.shelf</c>, spec §6.4): drop files to hold them (references, not copies); click a
/// file to copy it (then Ctrl+V in WhatsApp, a chat or a folder), or drag it out. 1×1 icon + "SHELF · N"; 2×1 / 2×2 header with the count, then a dashed drop area when empty or the
/// file chips (icon + name, ✕ on hover). Saved to shelf.json; missing files are dropped on load. Names never logged.
/// </summary>
public sealed class ShelfTile : ITileView, IFileDropTarget
{
    private static readonly Brush ChipBrush = TileChrome.Frozen(0x22, 0x22, 0x22);
    private readonly ShelfStore _store;
    private readonly ShelfList _shelf;
    private readonly AppIconCache _icons;
    private TileSize _size;
    private Border? _tile;
    private Point _down;
    private bool _dragged, _prestarted;

    public ShelfTile(ShelfStore store, AppIconCache icons)
    {
        _store = store;
        _icons = icons;
        _shelf = ShelfList.From(store.Load(), p => File.Exists(p) || Directory.Exists(p));
    }

    public TileKind Kind => TileKind.Shelf;

    public FrameworkElement Build(TileSize size)
    {
        _size = size;
        // Drops arrive through the notch window (WM_DROPFILES, see FileDropNative), not WPF drag-and-drop: OLE drops
        // from Explorer cannot reach this High-integrity process.
        _tile = TileChrome.Tile(new Grid(), center: size == TileSize.One);
        Fill();
        return _tile;
    }

    private void Fill()
    {
        if (_tile is null) return;
        var count = _shelf.Paths.Count;
        if (_size == TileSize.One)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = TileChrome.Icon("shelf", 20);
            icon.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(icon);
            var label = TileChrome.Label($"Shelf · {count}");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            _tile.Child = stack;
            return;
        }
        var panel = new DockPanel();
        var header = TileChrome.Header("shelf", "Shelf", TileChrome.Label(count == 1 ? "1 file" : $"{count} files"));
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        panel.Children.Add(header);
        if (count == 0) panel.Children.Add(DropArea());
        else
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            for (var i = 0; i < count; i++) wrap.Children.Add(Chip(i, _shelf.Paths[i]));
            panel.Children.Add(new ScrollViewer { Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
        }
        _tile.Child = panel;
    }

    // Mockup .drop: dashed #3A3A3A, radius 14, "Drop files here" / "DRAG OUT TO USE".
    private static FrameworkElement DropArea()
    {
        var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var line = TileChrome.Body("Drop files here", 12, FontWeights.Normal, TileChrome.Frozen(0xAA, 0xAA, 0xAA));
        line.HorizontalAlignment = HorizontalAlignment.Center;
        text.Children.Add(line);
        var hint = TileChrome.Label("Click to copy · drag out");
        hint.Margin = new Thickness(0, 4, 0, 0);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        text.Children.Add(hint);
        var dashes = new Rectangle
        {
            Stroke = TileChrome.Frozen(0x3A, 0x3A, 0x3A), StrokeThickness = 1.5, StrokeDashArray = [3, 2], RadiusX = 14, RadiusY = 14,
        };
        return new Grid { Margin = new Thickness(0, 8, 0, 0), Children = { dashes, text } };
    }

    private FrameworkElement Chip(int index, string path)
    {
        var name = new TextBlock
        {
            Text = Path.GetFileName(path.TrimEnd('\\')), FontFamily = TileChrome.Font, FontSize = 11, Foreground = TileChrome.Text,
            MaxWidth = 92, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        var x = new TextBlock
        {
            Text = "✕", FontSize = 9, Foreground = TileChrome.Dim, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Hidden,
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent,
        };
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; _shelf.Remove(index); Save(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Image { Source = _icons.Get(path, 48), Width = 24, Height = 24 });
        row.Children.Add(name);
        row.Children.Add(x);
        var chip = new Border
        {
            Background = ChipBrush, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6), Child = row, Cursor = Cursors.Hand, ToolTip = Path.GetFileName(path),
        };
        chip.MouseEnter += (_, _) => x.Visibility = Visibility.Visible;
        chip.MouseLeave += (_, _) => x.Visibility = Visibility.Hidden;
        chip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _down = e.GetPosition(chip);
            _dragged = false;
            // The helper starts now, on the press, and waits: by the time the press becomes a drag it is ready (a
            // quick flick no longer ends before it starts). A click lets it go again without doing anything.
            _prestarted = ShelfDragHandoff.Prestart([path], chip);
        };
        // A click (no drag) copies the file itself: Ctrl+V then pastes it into WhatsApp, a chat, a mail or a folder.
        chip.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragged) return;
            e.Handled = true;
            if (!ShelfClipboard.Copy([path])) return;
            name.Text = "Copied ✓";
            var back = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
            back.Tick += (_, _) => { back.Stop(); name.Text = Path.GetFileName(path.TrimEnd('\\')); };
            back.Start();
        };
        chip.MouseMove += (_, e) =>
        {
            if (_dragged || e.LeftButton != MouseButtonState.Pressed) return; // one drag per press
            var d = e.GetPosition(chip) - _down;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragged = true;
            // The shell runs raised and Windows refuses its drags into normal apps, so a helper at normal level takes
            // the drag over (drops into WhatsApp, browser upload boxes, Explorer). Only without it (dev build): in-process.
            if (_prestarted) { ShelfDragHandoff.Go(chip); return; }
            if (ShelfDragHandoff.Start([path], chip)) return;
            // Drag the reference out (Explorer copies on drop); the shelf keeps holding it.
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            DragDrop.DoDragDrop(chip, data, DragDropEffects.Copy | DragDropEffects.Link);
        };
        return chip;
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var added = paths.Count(_shelf.Add);
        if (added > 0) Save();
        Log.Info($"shelf: {added} added, {_shelf.Paths.Count} held");
    }

    private void Save()
    {
        try { _store.Save(_shelf.Paths); }
        catch (Exception e) { Log.Error($"saving the shelf failed (count={_shelf.Paths.Count})", e); }
        Fill();
    }

    public void Opened() => Fill();
    public void Closed() { }
    public void Dispose() { }
}

/// <summary>
/// Puts shelf files on the clipboard as files (user, 2026-11-20: paste into WhatsApp and the like), marked as a copy
/// so a paste in a folder never moves the original. Names are never logged.
/// </summary>
public static class ShelfClipboard
{
    public static DataObject Data(IReadOnlyList<string> paths)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths.ToArray());
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1))); // DROPEFFECT_COPY
        return data;
    }

    /// <summary>Copies; false (logged without names) when the clipboard is busy.</summary>
    public static bool Copy(IReadOnlyList<string> paths)
    {
        try
        {
            Clipboard.SetDataObject(Data(paths), copy: true);
            Log.Info($"shelf: {paths.Count} copied to the clipboard");
            return true;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Error("shelf: copying to the clipboard failed", e);
            return false;
        }
    }
}

/// <summary>
/// Hands a shelf drag to NothingShell.DragHelper (user, 2026-11-20): started through Explorer so it runs at normal
/// level, while the button is still down; it then drags the files from the pointer. Names are never logged.
/// </summary>
public static class ShelfDragHandoff
{
    /// <summary>
    /// Plan 8: starts the helper on the press, told to wait on the notch window (see <see cref="Go"/>). False when there
    /// is no helper or it did not start; the drag then starts the helper as before.
    /// </summary>
    public static bool Prestart(IReadOnlyList<string> paths, UIElement from, string? helper = null)
    {
        helper ??= DefaultHelper;
        var hwnd = NotchHandle(from);
        if (!File.Exists(helper) || hwnd == 0) return false;
        AllowSetForegroundWindow(-1 /* ASFW_ANY */);
        return NothingShell.Native.UnelevatedShell.Open(helper, NothingShell.Core.DragArgs.CommandLine(paths, hwnd), Path.GetDirectoryName(helper),
            (what, e) => Log.Error(what, e));
    }

    /// <summary>The press became a drag: the notch lets clicks through, which tells the waiting helper to take over.</summary>
    public static void Go(UIElement from)
    {
        from.ReleaseMouseCapture();
        Mouse.Capture(null);
        AllowSetForegroundWindow(-1 /* ASFW_ANY */); // again: moving the mouse since the press may have used it up
        var hwnd = NotchHandle(from);
        if (hwnd != 0) HoldClickThrough(hwnd);
        Log.Info("shelf: drag handed to the waiting helper");
    }

    private static nint NotchHandle(UIElement from) =>
        Window.GetWindow(from) is { } w ? new System.Windows.Interop.WindowInteropHelper(w).Handle : 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public static string DefaultHelper => Path.Combine(AppContext.BaseDirectory, "NothingShell.DragHelper.exe");

    /// <summary>True when the helper was started (the shell must not drag itself); false = do the old in-process drag.</summary>
    public static bool Start(IReadOnlyList<string> paths, UIElement from, string? helper = null)
    {
        helper ??= DefaultHelper;
        if (!File.Exists(helper)) return false;
        from.ReleaseMouseCapture();
        Mouse.Capture(null); // the pointer is the helper's now
        // The helper must become the foreground window to take the mouse; let the next process do so.
        AllowSetForegroundWindow(-1 /* ASFW_ANY */);
        var window = Window.GetWindow(from);
        var hwnd = window is null ? 0 : new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var ok = NothingShell.Native.UnelevatedShell.Open(helper, NothingShell.Core.DragArgs.CommandLine(paths), Path.GetDirectoryName(helper),
            (what, e) => Log.Error(what, e));
        Log.Info($"shelf: drag of {paths.Count} handed to the helper ok={ok}");
        if (ok && hwnd != 0) HoldClickThrough(hwnd);
        return ok;
    }

    private const int ExTransparent = 0x20, ExLayered = 0x80000, VkLButton = 0x01, VkRButton = 0x02, SmSwapButton = 23;

    // The notch lets clicks through until the drag is over (NothingShell.Core.Shelving.DragHandoffHold says why): the
    // helper's grip is under it and must take the re-sent press. Only a style this added is taken off again.
    private static void HoldClickThrough(nint hwnd)
    {
        var before = NothingShell.Native.DockWindowNative.GetExStyle(hwnd);
        if ((before & ExTransparent) != 0) return; // already click-through: nothing to hold or restore
        NothingShell.Native.DockWindowNative.AddExStyle(hwnd, ExTransparent | ExLayered);
        var addedLayered = (before & ExLayered) == 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var released = 0;
        var poll = new System.Windows.Threading.DispatcherTimer { Interval = NothingShell.Core.Shelving.DragHandoffHold.Poll };
        poll.Tick += (_, _) =>
        {
            released = PrimaryButtonDown() ? 0 : released + 1;
            if (!NothingShell.Core.Shelving.DragHandoffHold.ShouldRestore(clock.Elapsed, released)) return;
            poll.Stop();
            NothingShell.Native.DockWindowNative.RemoveExStyle(hwnd, ExTransparent | (addedLayered ? ExLayered : 0));
            Log.Info($"shelf: notch clickable again after {clock.ElapsedMilliseconds} ms");
        };
        poll.Start();
    }

    private static bool PrimaryButtonDown() =>
        (GetAsyncKeyState(GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton) & 0x8000) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

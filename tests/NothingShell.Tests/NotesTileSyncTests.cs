using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Core.Notch;
using NothingShell.Notch.Dashboard;
using NothingShell.Notch.Dashboard.Tiles;

namespace NothingShell.Tests;

// Plan 10: the notch's Notes box, already open, shows a note that changed elsewhere (the widget, a sync) unless
// something is being typed in it.
public sealed class NotesTileSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-notestile-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException("on STA: " + error);
    }

    private static TextBox Box(DependencyObject root)
    {
        if (root is TextBox t) return t;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FindOrNull(child) is { } found) return found;
        throw new InvalidOperationException("no text box");
    }

    private static TextBox? FindOrNull(DependencyObject d)
    {
        try { return Box(d); } catch (InvalidOperationException) { return null; }
    }

    [Fact]
    public void OpenBox_FollowsAChangeMadeElsewhere() => OnSta(() =>
    {
        var notes = new SharedNotes(new NotesStore(_dir));
        notes.Set("old", null);
        var tile = new NotesTile(notes, () => null!);
        var box = Box(tile.Build(TileSize.Big));
        Assert.Equal("old", box.Text);
        notes.Set("from the widget", new object());
        Assert.Equal("from the widget", box.Text);
        tile.Dispose();
        Assert.Equal("from the widget", notes.Text); // showing it did not count as typing
    });
}

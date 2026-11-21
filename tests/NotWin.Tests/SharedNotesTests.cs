using System.IO;
using NotWin.Core;
using NotWin.Notch.Dashboard;

namespace NotWin.Tests;

// Plan 9: the notch's Notes tile and the Notes widget show the same text; an edit in one place shows in the other.
public sealed class SharedNotesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-notes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Set_SavesAndTellsTheOthers()
    {
        var notes = new SharedNotes(new NotesStore(_dir));
        var sender = new object();
        (string Text, object? From)? heard = null;
        notes.Changed += from => heard = (notes.Text, from);
        notes.Set("milk, eggs", sender);
        Assert.Equal(("milk, eggs", sender), heard);
        Assert.Equal("milk, eggs", new NotesStore(_dir).Load());
    }

    [Fact]
    public void Reload_PicksUpTheFile()
    {
        var notes = new SharedNotes(new NotesStore(_dir));
        new NotesStore(_dir).Save("from the other PC");
        object? from = "unset";
        notes.Changed += f => from = f;
        notes.Reload();
        Assert.Equal("from the other PC", notes.Text);
        Assert.Null(from);
    }

    [Fact]
    public void Set_SameText_SaysNothing()
    {
        var notes = new SharedNotes(new NotesStore(_dir));
        notes.Set("a", null);
        var calls = 0;
        notes.Changed += _ => calls++;
        notes.Set("a", null);
        Assert.Equal(0, calls);
    }
}

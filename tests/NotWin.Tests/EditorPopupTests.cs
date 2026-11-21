using System.IO;
using NotWin.Alarms;
using NotWin.Core;
using NotWin.Core.Alarms;
using NotWin.Notch.Dashboard;
using NotWin.Widgets;

namespace NotWin.Tests;

// Review (Plan 9): the editors never write back a stale copy over a change made elsewhere (a sync, a ring).
public sealed class EditorPopupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-editors-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void NotesPopup_NoTyping_DoesNotOverwriteASyncedNote() => OnSta(() =>
    {
        var notes = new SharedNotes(new NotesStore(_dir));
        notes.Set("old", null);
        var popup = new NotesEditorPopup(notes);
        new NotesStore(_dir).Save("from the other PC");
        notes.Reload();
        Assert.Equal("from the other PC", popup.EditorText); // the open editor shows it
        popup.SaveIfEdited();                                // what closing does
        Assert.Equal("from the other PC", notes.Text);
    });

    [Fact]
    public void AlarmEditor_EditsTheCurrentList_NotAStaleCopy() => OnSta(() =>
    {
        var store = new AlarmStore(_dir);
        store.Save([new Alarm("a", 7, 0, 0, true), new Alarm("b", 8, 0, 0, true)]);
        var alarms = new AlarmService(store, () => new DateTime(2026, 11, 21, 6, 0, 0));
        var editor = new AlarmEditorPopup(alarms);
        // "a" rang and turned itself off while the editor was open.
        alarms.Set([new Alarm("a", 7, 0, 0, false), new Alarm("b", 8, 0, 0, true)]);
        Assert.True(editor.SetTime("b", "08:30"));
        Assert.False(alarms.Alarms.Single(x => x.Id == "a").On);
        Assert.Equal((8, 30), (alarms.Alarms.Single(x => x.Id == "b").Hour, alarms.Alarms.Single(x => x.Id == "b").Minute));
        Assert.False(editor.SetTime("b", "25:00")); // refused, nothing saved
        Assert.Equal(8, alarms.Alarms.Single(x => x.Id == "b").Hour);
    });
}

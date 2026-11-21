using NothingShell.Core;

namespace NothingShell.Notch.Dashboard;

/// <summary>
/// The one note shared by the notch's Notes tile and the desktop Notes widget (Plan 9): an edit in one place shows in
/// the other. <see cref="Changed"/> tells everyone but the sender (it passes itself as <c>from</c>). The text is never
/// logged.
/// </summary>
public sealed class SharedNotes(NotesStore store)
{
    public string Text { get; private set; } = store.Load();

    /// <summary>Raised with whoever changed the text (null: the file changed, e.g. a sync from another PC).</summary>
    public event Action<object?>? Changed;

    public void Set(string text, object? from)
    {
        if (text == Text) return;
        Text = text;
        try { store.Save(text); }
        catch (Exception e) { Log.Error($"saving the note failed (length={text.Length})", e); }
        Changed?.Invoke(from);
    }

    /// <summary>Re-reads the file (after a sync pulled a newer copy).</summary>
    public void Reload()
    {
        Text = store.Load();
        Changed?.Invoke(null);
    }
}

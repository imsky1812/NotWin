namespace NothingShell.Core.Clips;

/// <summary>
/// The notch's clipboard history (spec §6.4, §7): text only, newest first, a repeat moves to the top, capped at 50.
/// Blank text and text over 10,000 characters are not kept.
/// </summary>
public sealed class ClipboardHistory(int cap = 50)
{
    public const int MaxLength = 10_000;
    private readonly List<string> _items = [];

    public int Cap => cap;
    public IReadOnlyList<string> Items => _items;

    /// <summary>Adds <paramref name="text"/> at the top; false when it is ignored (blank or too long).</summary>
    public bool Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) return false;
        _items.Remove(text);
        _items.Insert(0, text);
        if (_items.Count > cap) _items.RemoveRange(cap, _items.Count - cap);
        return true;
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _items.Count) _items.RemoveAt(index);
    }

    public void Clear() => _items.Clear();

    /// <summary>From a saved list (newest first); blanks and repeats dropped, cap applied.</summary>
    public static ClipboardHistory From(IEnumerable<string>? saved, int cap = 50)
    {
        var h = new ClipboardHistory(cap);
        if (saved is null) return h;
        foreach (var s in saved.Reverse()) h.Add(s);
        return h;
    }
}

/// <summary>Which clipboard contents may be recorded.</summary>
public static class ClipboardRules
{
    /// <summary>
    /// False when the copy is marked private: password managers and other apps set
    /// "ExcludeClipboardContentFromMonitorProcessing" or "Clipboard Viewer Ignore", or "CanIncludeInClipboardHistory" = 0
    /// (the same markers Windows' own clipboard history honours).
    /// </summary>
    public static bool ShouldRecord(IReadOnlyCollection<string> formatNames, uint? canIncludeInHistory)
    {
        foreach (var f in formatNames)
            if (string.Equals(f, "ExcludeClipboardContentFromMonitorProcessing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(f, "Clipboard Viewer Ignore", StringComparison.OrdinalIgnoreCase))
                return false;
        return canIncludeInHistory != 0;
    }
}

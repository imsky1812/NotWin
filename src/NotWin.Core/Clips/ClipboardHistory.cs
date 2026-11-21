namespace NotWin.Core.Clips;

/// <summary>
/// One clipboard history entry: text, or an image kept as a PNG named by <see cref="ImageId"/> (a content hash) with
/// its pixel size. Images were added at the user's request (2026-11-19: screenshots taken by hand should show).
/// </summary>
public sealed record ClipItem(string? Text, string? ImageId, int Width = 0, int Height = 0)
{
    public bool IsImage => ImageId is not null;

    public static ClipItem OfText(string text) => new(text, null);

    public static ClipItem OfImage(string id, int width, int height) => new(null, id, width, height);
}

/// <summary>
/// The notch's clipboard history (spec §6.4, §7 + images): newest first, a repeat moves to the top, capped at 50
/// entries and 20 images. Blank text and text over 10,000 characters are not kept. Image entries that leave the
/// history are reported through <see cref="TakeDropped"/> so the app can delete their files.
/// </summary>
public sealed class ClipboardHistory(int cap = 50, int imageCap = 20)
{
    public const int MaxLength = 10_000;
    private readonly List<ClipItem> _items = [];
    private readonly List<ClipItem> _dropped = [];

    public int Cap => cap;
    public IReadOnlyList<ClipItem> Items => _items;

    /// <summary>Adds text at the top; false when it is ignored (blank or too long).</summary>
    public bool Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) return false;
        Put(ClipItem.OfText(text));
        return true;
    }

    /// <summary>Adds an image (already saved as <paramref name="id"/>.png) at the top; a repeat moves up.</summary>
    public bool AddImage(string id, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        Put(ClipItem.OfImage(id, width, height));
        return true;
    }

    private void Put(ClipItem item)
    {
        _items.RemoveAll(i => i.IsImage ? i.ImageId == item.ImageId : !item.IsImage && i.Text == item.Text);
        _items.Insert(0, item);
        while (_items.Count(i => i.IsImage) > imageCap) Drop(_items.FindLastIndex(i => i.IsImage));
        while (_items.Count > cap) Drop(_items.Count - 1);
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _items.Count) Drop(index);
    }

    public void Clear()
    {
        while (_items.Count > 0) Drop(_items.Count - 1);
    }

    /// <summary>Image entries removed since the last call (their files can go).</summary>
    public IReadOnlyList<ClipItem> TakeDropped()
    {
        var d = _dropped.ToList();
        _dropped.Clear();
        return d;
    }

    private void Drop(int index)
    {
        if (_items[index].IsImage) _dropped.Add(_items[index]);
        _items.RemoveAt(index);
    }

    /// <summary>From a saved list (newest first); blanks and repeats dropped, caps applied.</summary>
    public static ClipboardHistory From(IEnumerable<ClipItem>? saved, int cap = 50, int imageCap = 20)
    {
        var h = new ClipboardHistory(cap, imageCap);
        if (saved is null) return h;
        foreach (var item in saved.Reverse())
        {
            if (item.IsImage) h.AddImage(item.ImageId!, item.Width, item.Height);
            else if (item.Text is { } t) h.Add(t);
        }
        h._dropped.Clear(); // loading never deletes files
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

namespace NothingShell.Core.Dock;

/// <summary>Dock window previews (design 2026-11-20): thumbnail size inside 220 × 140 DIP, aspect kept.</summary>
public static class PreviewRules
{
    public const double MaxW = 220, MaxH = 140;

    public static (double W, double H) Fit(double sourceW, double sourceH)
    {
        if (sourceW <= 0 || sourceH <= 0) return (MaxW, MaxH);
        var k = Math.Min(MaxW / sourceW, MaxH / sourceH);
        return (sourceW * k, sourceH * k);
    }
}

/// <summary>
/// When the preview card is open (design 2026-11-20): 400 ms on one dock item opens it for that item; moving to the
/// card keeps it; another item switches at once; 300 ms over neither closes it. A click closes it at once, and it does
/// not reopen for that item until the pointer has left it.
/// </summary>
public sealed class PreviewHover
{
    private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(400), CloseDelay = TimeSpan.FromMilliseconds(300);
    private string? _item, _suppressed;
    private TimeSpan _itemSince;
    private TimeSpan? _leftAt;

    public string? OpenFor { get; private set; }

    public void Pointer(string? itemId, bool overCard, TimeSpan now)
    {
        if (itemId != _item)
        {
            _item = itemId;
            _itemSince = now;
            if (_suppressed is not null && itemId != _suppressed) _suppressed = null;
        }
        if (itemId is null && !overCard) _leftAt ??= now;
        else _leftAt = null;
    }

    /// <summary>Advances to <paramref name="now"/>; true when <see cref="OpenFor"/> changed.</summary>
    public bool Update(TimeSpan now)
    {
        if (OpenFor is null)
        {
            if (_item is null || _item == _suppressed || now - _itemSince < OpenDelay) return false;
            OpenFor = _item;
            return true;
        }
        if (_item is not null && _item != OpenFor && _item != _suppressed)
        {
            OpenFor = _item;
            return true;
        }
        if (_leftAt is { } left && now - left >= CloseDelay)
        {
            OpenFor = null;
            return true;
        }
        return false;
    }

    /// <summary>When <see cref="Update"/> should next run, or null when nothing is pending.</summary>
    public TimeSpan? NextCheckAt =>
        OpenFor is null
            ? _item is not null && _item != _suppressed ? _itemSince + OpenDelay : null
            : _leftAt is { } left ? left + CloseDelay : null;

    /// <summary>A click on the dock: close now; the item under the pointer does not reopen it until left.</summary>
    public void Close()
    {
        _suppressed = OpenFor ?? _item;
        OpenFor = null;
        _leftAt = null;
    }
}

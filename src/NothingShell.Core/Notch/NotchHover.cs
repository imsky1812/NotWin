namespace NothingShell.Core.Notch;

/// <summary>
/// When the notch dashboard opens and closes (spec §6.4: opens on hover, closes when the mouse leaves unless editing).
/// Opens after the pointer rests on the pill for <see cref="OpenDwell"/>, so sweeping across it to a browser tab does
/// nothing; closes <see cref="CloseDelay"/> after the pointer leaves the open area. Holds ("edit", "text", "expand")
/// keep it open. Full screen closes it and blocks opening. After <see cref="Close"/> (Esc, a tool starting) it reopens
/// only once the pointer has left the pill and come back. Pure: the caller passes a monotonic clock.
/// </summary>
public sealed class NotchHover
{
    public static readonly TimeSpan OpenDwell = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(300);

    private readonly HashSet<string> _holds = [];
    private TimeSpan? _onPillSince;
    private TimeSpan? _outsideSince;
    private bool _overPill, _inside, _fullscreen, _mustLeave;

    public bool IsOpen { get; private set; }

    public void Pointer(bool overPill, bool overOpenArea, TimeSpan now)
    {
        _overPill = overPill;
        if (!overPill) _mustLeave = false;
        _onPillSince = overPill ? _onPillSince ?? now : null;
        _inside = overPill || overOpenArea;
        if (IsOpen) _outsideSince = _inside ? null : _outsideSince ?? now;
    }

    public void Hold(string reason, bool on, TimeSpan now)
    {
        if (on) { _holds.Add(reason); return; }
        if (_holds.Remove(reason) && _holds.Count == 0 && IsOpen && !_inside) _outsideSince = now;
    }

    public void SetFullscreen(bool fullscreen)
    {
        _fullscreen = fullscreen;
        if (!fullscreen) return;
        IsOpen = false;
        _onPillSince = null;
        _outsideSince = null;
        _holds.Clear();
    }

    public void Close()
    {
        IsOpen = false;
        _holds.Clear();
        _outsideSince = null;
        _onPillSince = null;
        _mustLeave = _overPill;
    }

    /// <summary>Applies the rules at <paramref name="now"/>; true when <see cref="IsOpen"/> changed.</summary>
    public bool Update(TimeSpan now)
    {
        if (_fullscreen) return false;
        if (!IsOpen)
        {
            if (_onPillSince is not { } since || _mustLeave || now - since < OpenDwell) return false;
            IsOpen = true;
            _outsideSince = null;
            return true;
        }
        if (_holds.Count > 0 || _outsideSince is not { } left || now - left < CloseDelay) return false;
        IsOpen = false;
        _outsideSince = null;
        _onPillSince = _overPill ? now : null;
        return true;
    }

    /// <summary>When <see cref="Update"/> could next change something, or null if nothing is pending.</summary>
    public TimeSpan? NextCheckAt =>
        _fullscreen ? null
        : !IsOpen ? (_onPillSince is { } s && !_mustLeave ? s + OpenDwell : null)
        : _holds.Count == 0 && _outsideSince is { } o ? o + CloseDelay : null;
}

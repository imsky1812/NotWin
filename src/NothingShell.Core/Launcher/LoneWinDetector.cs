namespace NothingShell.Core.Launcher;

/// <summary>
/// What the keyboard hook should do with one key event.
/// <see cref="InjectMaskFirst"/>: send the mask key (VK 0xE8) before the Win keyup goes on, so Windows
/// sees "Win + something" and does not open Start. <see cref="Swallow"/> is reserved for Plan 3 (Win+A/N).
/// </summary>
public readonly record struct KeyVerdict(bool Swallow, bool InjectMaskFirst, bool OpenLauncher);

/// <summary>
/// Detects a lone Win tap (press and release with no other key involved) from low-level key events.
/// Not thread-safe: fed from the single hook thread.
/// </summary>
public sealed class LoneWinDetector
{
    private const uint LWin = 0x5B, RWin = 0x5C;
    private static readonly KeyVerdict Nothing = new(false, false, false);

    private readonly HashSet<uint> _held = [];
    private readonly Func<uint, bool>? _isPhysicallyDown;
    private bool _winDown;
    private bool _chorded;

    /// <param name="isPhysicallyDown">
    /// Optional check (GetAsyncKeyState in the app) used to drop keys whose keyup the hook never saw,
    /// e.g. the L of Win+L released on the lock screen. Without it such a key would block lone Win forever.
    /// </param>
    public LoneWinDetector(Func<uint, bool>? isPhysicallyDown = null) => _isPhysicallyDown = isPhysicallyDown;

    public KeyVerdict OnKey(uint vk, bool down, bool injectedByUs)
    {
        if (injectedByUs) return Nothing; // our own mask key

        if (vk is LWin or RWin)
        {
            if (down)
            {
                if (!_winDown)
                {
                    if (_isPhysicallyDown is not null) _held.RemoveWhere(k => !_isPhysicallyDown(k));
                    _winDown = true;
                    _chorded = _held.Count > 0; // another key already held: Win is part of a combo
                }
                return Nothing; // auto-repeat downs change nothing
            }

            var lone = _winDown && !_chorded;
            _winDown = false;
            _chorded = false;
            return lone ? new KeyVerdict(Swallow: false, InjectMaskFirst: true, OpenLauncher: true) : Nothing;
        }

        if (down)
        {
            _held.Add(vk);
            if (_winDown) _chorded = true;
        }
        else
        {
            _held.Remove(vk);
        }
        return Nothing;
    }
}

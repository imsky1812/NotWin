namespace NothingShell.Core.Launcher;

/// <summary>
/// What the keyboard hook should do with one key event.
/// <see cref="InjectMaskFirst"/>: send the mask key (VK 0xE8) before the Win keyup goes on, so Windows
/// sees "Win + something" and does not open Start. <see cref="Swallow"/>: the key never reaches Windows (the A/N of
/// Win+A / Win+N, so Windows' own Quick Settings / Notification Center do not open). <see cref="OpenPanel"/>: open
/// Nothing Shell's quick-settings panel.
/// </summary>
public readonly record struct KeyVerdict(bool Swallow, bool InjectMaskFirst, bool OpenLauncher, bool OpenPanel = false);

/// <summary>
/// Detects a lone Win tap (press and release with no other key involved) from low-level key events.
/// Not thread-safe: fed from the single hook thread.
/// </summary>
public sealed class LoneWinDetector
{
    private const uint LWin = 0x5B, RWin = 0x5C, KeyA = 0x41, KeyN = 0x4E;
    private static readonly KeyVerdict Nothing = new(false, false, false);
    private static readonly KeyVerdict SwallowOnly = new(Swallow: true, InjectMaskFirst: false, OpenLauncher: false);

    private readonly HashSet<uint> _held = [];
    private readonly HashSet<uint> _swallowed = []; // A/N keys swallowed on keydown: their keyups are swallowed too
    private readonly Func<uint, bool>? _isPhysicallyDown;
    private bool _winDown;
    private bool _chorded;
    private bool _panelUsed;

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
                // "Win already down" but Win is not physically down: its keyup was missed (released on the lock
                // screen after Win+L, Ctrl+Alt+Del or a UAC prompt). In an LL hook the async state updates after
                // the hook returns, so a real auto-repeat reads "down" and a fresh press reads "up".
                if (_winDown && _isPhysicallyDown is not null && !_isPhysicallyDown(vk)) _winDown = false;
                if (!_winDown)
                {
                    if (_isPhysicallyDown is not null) _held.RemoveWhere(k => !_isPhysicallyDown(k));
                    _winDown = true;
                    _chorded = _held.Count > 0; // another key already held: Win is part of a combo
                }
                return Nothing; // auto-repeat downs change nothing
            }

            var lone = _winDown && !_chorded;
            var panelUsed = _panelUsed;
            _winDown = false;
            _chorded = false;
            _panelUsed = false;
            // Win+A/N swallowed the letter, so Windows saw Win down + Win up alone: mask it or Start would open.
            if (panelUsed) return new KeyVerdict(Swallow: false, InjectMaskFirst: true, OpenLauncher: false);
            return lone ? new KeyVerdict(Swallow: false, InjectMaskFirst: true, OpenLauncher: true) : Nothing;
        }

        if (vk is KeyA or KeyN)
        {
            if (down && _swallowed.Contains(vk)) return SwallowOnly; // auto-repeat of a swallowed letter
            if (!down && _swallowed.Remove(vk)) return SwallowOnly;  // even if Win was released first
            if (down && _winDown && !ModifierHeld())
            {
                _swallowed.Add(vk);
                _chorded = true;
                _panelUsed = true;
                return new KeyVerdict(Swallow: true, InjectMaskFirst: false, OpenLauncher: false, OpenPanel: true);
            }
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

    // Shift / Ctrl / Alt (generic and left/right codes): Win+Shift+A etc. are other shortcuts, not ours.
    private bool ModifierHeld() => _held.Any(k => k is >= 0x10 and <= 0x12 or >= 0xA0 and <= 0xA5);
}

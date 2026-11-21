namespace NotWin.Core.Shell;

/// <summary>
/// After typing in the notch (Notes) ends with Esc or the notch closing, the notch must not keep the keyboard: give the
/// foreground back to the window that had it before (final review #1) — but only if the notch still has it (the user
/// did not already click somewhere else) and that window still exists.
/// </summary>
public static class ForegroundHandBack
{
    public static nint? Target(nint previous, nint current, nint own, Func<nint, bool> isWindow) =>
        previous != 0 && previous != own && current == own && isWindow(previous) ? previous : null;
}

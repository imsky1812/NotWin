namespace NotWin.Core.Widgets;

/// <summary>A toggle tile's state (Plan 13).</summary>
public enum ToggleState { Off, On, Blocked, Unavailable }

/// <summary>
/// Mic / camera access from Windows' consent store (Plan 13). The user's "let apps use it" switch is
/// HKCU <c>…\ConsentStore\{microphone|webcam}\Value</c> and the desktop-apps switch its <c>NonPackaged\Value</c>; the
/// machine-wide switch (HKLM, admin only) overrides both. A missing value means "Allow", Windows' default.
/// </summary>
public static class PrivacyConsent
{
    public static ToggleState State(string? machine, string? user, string? nonPackaged)
    {
        if (!CanChange(machine)) return ToggleState.Blocked;
        return Allowed(user) && Allowed(nonPackaged) ? ToggleState.On : ToggleState.Off;
    }

    /// <summary>False when the machine-wide switch is off: only Settings (with admin) can change it.</summary>
    public static bool CanChange(string? machine) => Allowed(machine);

    private static bool Allowed(string? value) => value is null || string.Equals(value, "Allow", StringComparison.OrdinalIgnoreCase);
}

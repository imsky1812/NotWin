namespace NotWin.Core;

/// <summary>Pure rules for dock pins: importing a taskbar shortcut, and naming a newly pinned app.</summary>
public static class PinRules
{
    /// <summary>
    /// A pin from a taskbar shortcut, or null to skip it. AppId: the shortcut's AppUserModel.ID, else the lower-case
    /// target exe. A shortcut whose target is gone is still kept when it has an AUMID: it then launches through
    /// <c>shell:AppsFolder\AUMID</c> (the app moved or updated, e.g. a Store app's versioned folder).
    /// </summary>
    public static PinnedApp? FromShortcut(string name, string lnkPath, string? target, bool targetExists, string? aumid)
    {
        var hasTarget = !string.IsNullOrEmpty(target);
        if (hasTarget && !targetExists)
            return aumid is null ? null : new PinnedApp(aumid, name, @"shell:AppsFolder\" + aumid, null);
        var appId = aumid ?? (targetExists ? target!.ToLowerInvariant() : null);
        if (appId is null) return null;
        return new PinnedApp(appId, name, lnkPath, targetExists ? target!.ToLowerInvariant() : null);
    }

    /// <summary>
    /// The name shown for a newly pinned app: the launcher index's name when it knows the app, else for packaged
    /// apps the window-derived name (their exe is a shared host such as ApplicationFrameHost, whose description is
    /// "Application Frame Host"), else the exe's description, else the fallback.
    /// </summary>
    public static string Name(string appId, string? indexedName, string? exeDescription, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(indexedName)) return indexedName.Trim();
        if (IsPackagedAppId(appId)) return fallback;
        return string.IsNullOrWhiteSpace(exeDescription) ? fallback : exeDescription.Trim();
    }

    /// <summary>Packaged-app AUMID "family!app": has '!' and is not a path.</summary>
    public static bool IsPackagedAppId(string id) => id.Contains('!') && !id.Contains('\\') && !id.Contains(':');
}

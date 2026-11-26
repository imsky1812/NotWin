using System.Security.Cryptography;

namespace NotWin.Core.Sync;

/// <summary>One side of a synced file: whether it exists, its content hash and when it was written (UTC).</summary>
public sealed record SyncSide(bool Exists, string? Hash, DateTime WrittenUtc);

public enum SyncAction { None, Push, Pull }

/// <summary>
/// Settings sync through a folder the user picks (Plan 9). For each file, compared with the hash both sides had at the
/// last sync: a side that changed alone is copied to the other; when both changed the newer one wins. On the first
/// sync of a file that both sides have, the folder's copy wins (review: a PC joining must not push its fresh
/// defaults over the others). A file deleted on this PC is never deleted from the folder; one deleted from the folder
/// is put back.
/// </summary>
public static class SyncRule
{
    public static SyncAction Decide(SyncSide local, SyncSide remote, string? lastSyncedHash)
    {
        if (!local.Exists && !remote.Exists) return SyncAction.None;
        if (local.Exists && remote.Exists && local.Hash == remote.Hash) return SyncAction.None;
        if (lastSyncedHash is null)
        {
            if (!remote.Exists) return SyncAction.Push;
            return SyncAction.Pull; // joining: the folder's copy wins
        }
        if (local.Hash != lastSyncedHash && remote.Hash != lastSyncedHash)
        {
            if (!remote.Exists) return SyncAction.Push;
            if (!local.Exists) return SyncAction.Pull;
            return remote.WrittenUtc > local.WrittenUtc ? SyncAction.Pull : SyncAction.Push;
        }
        if (local.Hash != lastSyncedHash) return local.Exists ? SyncAction.Push : SyncAction.None; // only this PC changed
        return remote.Exists ? SyncAction.Pull : SyncAction.Push; // only the folder changed (or lost the file)
    }

    /// <summary>
    /// The other PC's settings, keeping what belongs to this PC: start with Windows, the sync folder, the pin import and
    /// each photo frame's photos (files on this PC; a frame new here starts empty).
    /// </summary>
    /// Pins (security review 2026-11-21): a pin this PC also has keeps this PC's own entry; another pin is taken only
    /// when its launch path and target are <see cref="PinTrust.IsTrusted"/> under <paramref name="trustedRoots"/>, else
    /// dropped. The old one-widget-per-kind layout never comes from another PC.
    public static ShellSettings Merge(ShellSettings mine, ShellSettings theirs, IReadOnlyList<string>? trustedRoots = null)
    {
        trustedRoots ??= [];
        var pins = new List<PinnedApp>();
        foreach (var p in theirs.PinnedApps ?? [])
        {
            if (mine.PinnedApps.FirstOrDefault(m => DockItemsBuilder.SameApp(m.AppId, p.AppId)) is { } own) pins.Add(own);
            else if (PinTrust.IsTrusted(p.LaunchPath, trustedRoots) && (p.TargetExe is null || PinTrust.IsTrusted(p.TargetExe, trustedRoots))) pins.Add(p);
        }
        var merged = theirs with
        {
            StartWithWindows = mine.StartWithWindows, SyncFolder = mine.SyncFolder, PinsImported = mine.PinsImported,
            PinnedApps = pins, Widgets = mine.Widgets,
        };
        if (theirs.WidgetList is not { } layout) return merged;
        var myPhotos = (mine.WidgetList?.Widgets ?? []).Where(p => p.Config?.Photos is not null)
            .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Config!.Photos);
        return merged with
        {
            WidgetList = new Widgets.DesktopLayout([.. layout.Widgets.Select(p => p.Config is null && !myPhotos.ContainsKey(p.Id) ? p
                : p with { Config = (p.Config ?? new Widgets.WidgetConfig()) with { Photos = myPhotos.GetValueOrDefault(p.Id) } })]),
        };
    }

    /// <summary>The settings as every PC shares them: this PC's own fields and the photo lists blanked (for comparing).</summary>
    public static ShellSettings Shared(ShellSettings s) => s with
    {
        StartWithWindows = true, SyncFolder = null, PinsImported = false,
        WidgetList = s.WidgetList is { } l
            ? new Widgets.DesktopLayout([.. l.Widgets.Select(p => p.Config is null ? p : p with { Config = p.Config with { Photos = null } })])
            : null,
    };

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

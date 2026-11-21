using System.Security.Cryptography;

namespace NothingShell.Core.Sync;

/// <summary>One side of a synced file: whether it exists, its content hash and when it was written (UTC).</summary>
public sealed record SyncSide(bool Exists, string? Hash, DateTime WrittenUtc);

public enum SyncAction { None, Push, Pull }

/// <summary>
/// Settings sync through a folder the user picks (Plan 9). For each file, compared with the hash both sides had at the
/// last sync: a side that changed alone is copied to the other; when both changed (or on the first sync) the newer
/// one wins. A file deleted on this PC is never deleted from the folder; one deleted from the folder is put back.
/// </summary>
public static class SyncRule
{
    public static SyncAction Decide(SyncSide local, SyncSide remote, string? lastSyncedHash)
    {
        if (!local.Exists && !remote.Exists) return SyncAction.None;
        if (local.Exists && remote.Exists && local.Hash == remote.Hash) return SyncAction.None;
        if (lastSyncedHash is null || (local.Hash != lastSyncedHash && remote.Hash != lastSyncedHash))
        {
            if (!remote.Exists) return SyncAction.Push;
            if (!local.Exists) return SyncAction.Pull;
            return remote.WrittenUtc > local.WrittenUtc ? SyncAction.Pull : SyncAction.Push;
        }
        if (local.Hash != lastSyncedHash) return local.Exists ? SyncAction.Push : SyncAction.None; // only this PC changed
        return remote.Exists ? SyncAction.Pull : SyncAction.Push; // only the folder changed (or lost the file)
    }

    /// <summary>The other PC's settings, keeping what belongs to this PC: start with Windows, the sync folder, the pin import.</summary>
    public static ShellSettings Merge(ShellSettings mine, ShellSettings theirs) =>
        theirs with { StartWithWindows = mine.StartWithWindows, SyncFolder = mine.SyncFolder, PinsImported = mine.PinsImported };

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

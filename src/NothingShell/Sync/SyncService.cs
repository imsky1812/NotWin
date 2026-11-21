using System.IO;
using System.Text.Json;
using NothingShell.Core;
using NothingShell.Core.Sync;

namespace NothingShell.Sync;

/// <summary>What one sync did: whether the folder was reachable, and which files came in or went out.</summary>
public sealed record SyncOutcome(bool Reached, IReadOnlyList<string> Pulled, IReadOnlyList<string> Pushed);

/// <summary>
/// Settings sync (Plan 9): <c>settings.json</c>, <c>notes.txt</c> and <c>alarms.json</c> kept in
/// <c>&lt;folder&gt;\Nothing Shell sync</c> (a folder the user picks, e.g. in OneDrive, which does the copying between
/// PCs). <see cref="SyncRule"/> decides each file; pulled settings keep this PC's own fields
/// (<see cref="SyncRule.Merge"/>), and settings are compared without those fields so two PCs never copy them back and
/// forth. Every write goes through a temp file. The folder path and the contents are never logged.
/// </summary>
public sealed class SyncService(string dataDir, Func<string?> folder)
{
    public const string SubFolder = "Nothing Shell sync";
    private const string StateFile = "last-sync.json", SettingsFile = "settings.json";
    private static readonly string[] Files = [SettingsFile, "notes.txt", Core.Alarms.AlarmStore.FileName];
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public DateTime? LastSynced { get; private set; }

    /// <summary>
    /// The folder's "Nothing Shell sync" folder to watch, created inside the chosen folder only while that one exists
    /// (review: a folder that went away, e.g. OneDrive unlinked, is never recreated as a plain local one); else null.
    /// </summary>
    public string? WatchDirectory()
    {
        var root = folder();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        var dir = Path.Combine(root, SubFolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public SyncOutcome RunOnce()
    {
        var root = folder();
        if (string.IsNullOrWhiteSpace(root)) return new SyncOutcome(false, [], []);
        try
        {
            if (!Directory.Exists(root)) return new SyncOutcome(false, [], []);
            var remoteDir = Path.Combine(root, SubFolder);
            Directory.CreateDirectory(remoteDir);
            var state = LoadState();
            var before = new Dictionary<string, string>(state);
            List<string> pulled = [], pushed = [];
            foreach (var name in Files)
            {
                var localPath = Path.Combine(dataDir, name);
                var remotePath = Path.Combine(remoteDir, name);
                // An online-only OneDrive file would be downloaded right now, with the shell waiting: next time.
                if (OnlineOnly(remotePath)) continue;
                var local = Side(localPath, name);
                var remote = Side(remotePath, name);
                state.TryGetValue(name, out var last);
                switch (SyncRule.Decide(local, remote, last))
                {
                    case SyncAction.Push:
                        CopyAtomic(localPath, remotePath);
                        state[name] = local.Hash!;
                        pushed.Add(name);
                        break;
                    case SyncAction.Pull:
                        if (!Pull(name, remotePath, localPath)) continue;
                        state[name] = remote.Hash!;
                        pulled.Add(name);
                        break;
                    default:
                        if (local.Exists && remote.Exists && local.Hash == remote.Hash) state[name] = local.Hash!;
                        break;
                }
            }
            if (state.Count != before.Count || state.Any(kv => !before.TryGetValue(kv.Key, out var v) || v != kv.Value)) SaveState(state);
            LastSynced = DateTime.Now;
            if (pulled.Count + pushed.Count > 0) Log.Info($"sync: {pulled.Count} pulled, {pushed.Count} pushed");
            return new SyncOutcome(true, pulled, pushed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info("sync: the folder could not be reached (" + e.GetType().Name + ")");
            return new SyncOutcome(false, [], []);
        }
    }

    // Settings come in merged with this PC's own fields; notes and alarms are copied as they are (alarms only when
    // the file reads, so a half-written copy never replaces them).
    private bool Pull(string name, string remotePath, string localPath)
    {
        if (name == Core.Alarms.AlarmStore.FileName)
        {
            try { _ = JsonSerializer.Deserialize<List<Core.Alarms.Alarm>>(File.ReadAllText(remotePath), Json) ?? throw new JsonException("null"); }
            catch (JsonException) { Log.Info("sync: the folder's alarms could not be read, skipped"); return false; }
        }
        if (name != SettingsFile)
        {
            CopyAtomic(remotePath, localPath);
            return true;
        }
        ShellSettings? theirs;
        try { theirs = JsonSerializer.Deserialize<ShellSettings>(File.ReadAllText(remotePath), Json); }
        catch (JsonException) { theirs = null; }
        if (theirs is null) { Log.Info("sync: the folder's settings could not be read, skipped"); return false; }
        var store = new SettingsStore(dataDir);
        var mine = store.Load();
        store.Save(SyncRule.Merge(mine, theirs, TrustedRoots()));
        return true;
    }

    /// <summary>Where installed apps and their shortcuts live: the only places a synced pin may launch from.</summary>
    public static IReadOnlyList<string> TrustedRoots()
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var local = F(Environment.SpecialFolder.LocalApplicationData);
        return [.. new[]
        {
            F(Environment.SpecialFolder.ProgramFiles), F(Environment.SpecialFolder.ProgramFilesX86), F(Environment.SpecialFolder.Windows),
            local.Length > 0 ? Path.Combine(local, "Programs") : "",
            F(Environment.SpecialFolder.StartMenu), F(Environment.SpecialFolder.CommonStartMenu),
        }.Where(p => p.Length > 0)];
    }

    private static SyncSide Side(string path, string name)
    {
        if (!File.Exists(path)) return new SyncSide(false, null, default);
        var bytes = File.ReadAllBytes(path);
        return new SyncSide(true, name == SettingsFile ? SettingsHash(bytes) : SyncRule.Hash(bytes), File.GetLastWriteTimeUtc(path));
    }

    // The settings without this PC's own fields, so a merged copy matches the folder's.
    private static string SettingsHash(byte[] bytes)
    {
        try
        {
            if (JsonSerializer.Deserialize<ShellSettings>(bytes, Json) is { } s)
                return SyncRule.Hash(JsonSerializer.SerializeToUtf8Bytes(SyncRule.Shared(s), Json));
        }
        catch (JsonException) { }
        return SyncRule.Hash(bytes);
    }

    private static bool OnlineOnly(string path)
    {
        const FileAttributes RecallOnOpen = (FileAttributes)0x00040000, RecallOnDataAccess = (FileAttributes)0x00400000;
        try { return File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    private static void CopyAtomic(string from, string to)
    {
        var temp = to + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(from, temp);
            File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(from));
            File.Move(temp, to, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private Dictionary<string, string> LoadState()
    {
        try
        {
            var path = Path.Combine(dataDir, StateFile);
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (JsonException) { return []; }
    }

    private void SaveState(Dictionary<string, string> state)
    {
        Directory.CreateDirectory(dataDir);
        var path = Path.Combine(dataDir, StateFile);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Move(temp, path, overwrite: true);
    }
}

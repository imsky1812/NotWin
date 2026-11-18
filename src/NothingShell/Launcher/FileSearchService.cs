using System.Data;
using System.Data.OleDb;
using System.IO;
using NothingShell.Core.Launcher;
using NothingShell.Native;

namespace NothingShell.Launcher;

/// <summary>
/// File results for the launcher: Recent items (name match) plus the Windows Search index (name match).
/// Never throws and never blocks the UI: everything runs on the thread pool, a cancelled (stale) query returns
/// nothing, and any provider failure (service off, provider missing, timeout) just means no file rows.
/// </summary>
public sealed class FileSearchService
{
    private const int MaxFiles = 20;
    private readonly Func<string, CancellationToken, IReadOnlyList<LaunchItem>> _indexQuery;
    private readonly string? _recentDir;
    private long _lastErrorLogTicks = long.MinValue;

    /// <param name="indexQuery">Null: the real Windows Search (OleDb) runner. Tests inject one.</param>
    /// <param name="recentDir">The Recent items folder; null skips Recent items.</param>
    public FileSearchService(Func<string, CancellationToken, IReadOnlyList<LaunchItem>>? indexQuery = null, string? recentDir = "")
    {
        _indexQuery = indexQuery ?? QueryIndex;
        _recentDir = recentDir == "" ? Environment.GetFolderPath(Environment.SpecialFolder.Recent) : recentDir;
    }

    public Task<IReadOnlyList<LaunchItem>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult<IReadOnlyList<LaunchItem>>([]);
        return Task.Run(() =>
        {
            try
            {
                var recent = Recent(query.Trim(), ct);
                ct.ThrowIfCancellationRequested();
                var indexed = _indexQuery(query, ct);
                ct.ThrowIfCancellationRequested();
                return FileSearchRules.Merge(recent, indexed, MaxFiles);
            }
            catch (OperationCanceledException) { return (IReadOnlyList<LaunchItem>)[]; }
            catch (Exception e)
            {
                var now = Environment.TickCount64;
                if (now - _lastErrorLogTicks > 60_000) { _lastErrorLogTicks = now; Log.Error("file search failed (file rows hidden)", e); }
                return (IReadOnlyList<LaunchItem>)[];
            }
        }, CancellationToken.None);
    }

    private IReadOnlyList<LaunchItem> Recent(string q, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_recentDir) || !Directory.Exists(_recentDir)) return [];
        var list = new List<LaunchItem>();
        foreach (var lnk in Directory.EnumerateFiles(_recentDir, "*.lnk"))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(lnk);
            if (!name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var target = ShellLinkNative.Read(lnk)?.Target;
            if (string.IsNullOrEmpty(target)) continue;
            if (File.Exists(target)) list.Add(new LaunchItem(target, Path.GetFileName(target), LaunchKind.File, target, []));
            else if (Directory.Exists(target)) list.Add(new LaunchItem(target, Path.GetFileName(target.TrimEnd('\\')), LaunchKind.Folder, target, []));
            if (list.Count >= 5) break;
        }
        return list;
    }

    // The OleDb assembly is only loaded when this first runs (first file search), not at startup.
    private static IReadOnlyList<LaunchItem> QueryIndex(string query, CancellationToken ct)
    {
        var sql = FileQuery.Build(query, MaxFiles);
        if (sql is null) return [];
        using var conn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
        conn.Open();
        using var cmd = new OleDbCommand(sql, conn) { CommandTimeout = 2 };
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
        var list = new List<LaunchItem>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.IsDBNull(0)) continue;
            var path = reader.GetString(0);
            var name = reader.IsDBNull(1) ? Path.GetFileName(path) : reader.GetString(1);
            var type = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var kind = string.Equals(type, "Directory", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path) && !File.Exists(path)
                ? LaunchKind.Folder : LaunchKind.File;
            list.Add(new LaunchItem(path, name, kind, path, []));
        }
        return list;
    }
}

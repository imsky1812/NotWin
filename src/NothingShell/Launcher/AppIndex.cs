using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using NothingShell.Core.Launcher;
using NothingShell.Native;

namespace NothingShell.Launcher;

/// <summary>
/// The launcher's searchable apps + settings, its launch history and the empty-state "Most used".
/// Apps come from <c>shell:AppsFolder</c> (enumerated on a background STA thread, never on the UI thread);
/// the UI always reads the last finished snapshot, so opening the launcher never waits for a refresh.
/// </summary>
public sealed class AppIndex
{
    private const string UserAssistKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

    private sealed record Snapshot(IReadOnlyList<LaunchItem> Apps, IReadOnlyList<LaunchItem> All,
        IReadOnlyList<UserAssistEntry> Recent, DateTime BuiltUtc);

    private readonly LaunchUsageStore _usage;
    private readonly object _gate = new();
    private volatile Snapshot _snap = new([], SettingsPages.All, [], DateTime.MinValue);
    private Task? _refreshing;

    public AppIndex(string dataDir) => _usage = new LaunchUsageStore(dataDir);

    public IReadOnlyList<LaunchItem> Apps => _snap.Apps;

    /// <summary>Apps then settings: everything the ranker searches synchronously.</summary>
    public IReadOnlyList<LaunchItem> All => _snap.All;

    /// <summary>Rebuilds the index in the background when it is older than <paramref name="maxAge"/> (or empty).</summary>
    public Task RefreshIfStaleAsync(TimeSpan maxAge)
    {
        lock (_gate)
        {
            if (_refreshing is { IsCompleted: false }) return _refreshing;
            var s = _snap;
            if (s.Apps.Count > 0 && DateTime.UtcNow - s.BuiltUtc < maxAge) return Task.CompletedTask;
            return _refreshing = RunOnSta(Rebuild);
        }
    }

    public IReadOnlyDictionary<string, LaunchUsage> Usage() => _usage.Load();

    public IReadOnlyList<LaunchItem> MostUsed()
    {
        var s = _snap;
        return MostUsedPicker.Pick(s.Apps, _usage.Load(), s.Recent);
    }

    /// <summary>Records the launch and opens the item. Never throws; logs the name only (never a file path).</summary>
    public bool Launch(LaunchItem item)
    {
        try { _usage.Record(item.Id, DateTimeOffset.Now); }
        catch (Exception e) { Log.Error("launcher usage save failed", e); }
        var ok = ShellLinkNative.Open(item.Target);
        Log.Info($"launch {item.Kind} ok={ok}" + (ok ? "" : $" error={ShellLinkNative.LastError}")); // never the name (privacy)
        return ok;
    }

    /// <summary>An indexed app whose Id is <paramref name="idOrExePath"/> (AUMID or path), or the only app with that exe file name.</summary>
    public LaunchItem? FindApp(string idOrExePath)
    {
        if (string.IsNullOrWhiteSpace(idOrExePath)) return null;
        var apps = _snap.Apps;
        var exact = apps.FirstOrDefault(a => string.Equals(a.Id, idOrExePath, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var file = Path.GetFileName(idOrExePath);
        if (string.IsNullOrEmpty(file) || !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        var byFile = apps.Where(a => string.Equals(Path.GetFileName(a.Id), file, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return byFile.Count == 1 ? byFile[0] : null;
    }

    private void Rebuild()
    {
        var sw = Stopwatch.StartNew();
        var entries = AppsFolderNative.Enumerate();
        if (entries.Count == 0)
        {
            Log.Info($"app index: enumeration returned nothing ({AppsFolderNative.LastError ?? "no error"}); keeping the previous index");
            return;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = entries
            .Where(AppIndexRules.IsLaunchable)
            .Where(e => seen.Add(e.DisplayName.Trim()))
            .Select(e => new LaunchItem(e.ParsingName, e.DisplayName.Trim(), LaunchKind.App, @"shell:AppsFolder\" + e.ParsingName, []))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var recent = ReadUserAssist();
        _snap = new Snapshot(apps, [.. apps, .. SettingsPages.All], recent, DateTime.UtcNow);
        var matched = recent.Count(r => apps.Any(a => string.Equals(a.Id, r.Name, StringComparison.OrdinalIgnoreCase)));
        Log.Info($"app index: {apps.Count} apps, {matched} with run history ({sw.ElapsedMilliseconds} ms)");
    }

    private static IReadOnlyList<UserAssistEntry> ReadUserAssist()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserAssistKey);
            if (key is null) return [];
            var list = new List<UserAssistEntry>();
            foreach (var name in key.GetValueNames())
                if (key.GetValue(name) is byte[] data && UserAssistParser.Parse(name, data) is { } e) list.Add(e);
            return list;
        }
        catch (Exception e)
        {
            Log.Error("reading UserAssist failed", e);
            return [];
        }
    }

    private static Task RunOnSta(Action work)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { work(); tcs.SetResult(); }
            catch (Exception e) { Log.Error("app index refresh failed", e); tcs.SetResult(); }
        }) { IsBackground = true, Name = "NothingShell.AppIndex" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }
}

namespace NothingShell.Core.Panel;

/// <summary>One Windows notification as the shell keeps it (text stays in memory; never logged).</summary>
public sealed record NotificationItem(uint Id, string AppId, string AppName, string Text, DateTimeOffset Time);

/// <summary>A panel row: one app's notifications (count, newest text), spec §6.3.</summary>
public sealed record NotificationGroup(string AppId, string AppName, int Count, string LatestText, DateTimeOffset LatestTime,
    IReadOnlyList<uint> Ids);

public static class NotificationGroups
{
    /// <summary>Groups by app (case-insensitive id), newest group first; each group carries its newest text.</summary>
    public static IReadOnlyList<NotificationGroup> Build(IEnumerable<NotificationItem> items) =>
        items
            .GroupBy(i => i.AppId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var newest = g.MaxBy(i => i.Time)!;
                return new NotificationGroup(newest.AppId, newest.AppName, g.Count(), newest.Text, newest.Time,
                    g.Select(i => i.Id).ToList());
            })
            .OrderByDescending(g => g.LatestTime)
            .ToList();
}

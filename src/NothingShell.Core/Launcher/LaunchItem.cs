namespace NothingShell.Core.Launcher;

/// <summary>Result kinds, in the spec's ranking order (apps → settings → files). Folders rank with files.</summary>
public enum LaunchKind { App, Setting, File, Folder }

/// <summary>
/// Something the launcher can open. <see cref="Id"/>: the AppsFolder parsing name for apps, the URI for settings,
/// the full path for files. <see cref="Target"/>: what ShellExecute opens (apps: <c>shell:AppsFolder\{Id}</c>).
/// </summary>
public sealed record LaunchItem(string Id, string Name, LaunchKind Kind, string Target, IReadOnlyList<string> Keywords);

/// <summary>How often (and when last) an item was launched from Nothing Shell.</summary>
public sealed record LaunchUsage(string Id, int Count, DateTimeOffset LastLaunched);

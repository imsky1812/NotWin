using System.IO;

namespace NothingShell;

/// <summary>Appends to %AppData%\NothingShell\log.txt, rolling to log.1.txt at 1 MB.</summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{Describe(ex)}");

    /// <summary>
    /// An exception for the log without its message: messages can hold full paths and file names, which the log never
    /// keeps (privacy rule; security review 2026-11-21). Type, HRESULT and stack trace, inner exceptions likewise.
    /// </summary>
    public static string Describe(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.AppendLine().Append(" ---> ");
            sb.Append(e.GetType().FullName).Append(" (0x").Append(e.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)).Append(')');
            if (e.StackTrace is { } st) sb.AppendLine().Append(st);
        }
        return sb.ToString();
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var path = Path.Combine(Dir, "log.txt");
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= MaxBytes)
                    File.Move(path, Path.Combine(Dir, "log.1.txt"), overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the shell down.
        }
    }
}

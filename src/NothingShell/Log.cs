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
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

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

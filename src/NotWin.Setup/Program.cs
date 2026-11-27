using System.Runtime.InteropServices;
using NotWin.Setup;

// NotWin-Setup.exe: double-click, say Yes to the admin prompt and to "Install NotWin?". With --uninstall (the copy kept
// in Program Files, run from Settings > Apps) it removes NotWin. --quiet skips the questions and the final message.
var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
var uninstall = args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase);
var self = Environment.ProcessPath!;
var log = new List<string>();
var logFile = Path.Combine(Path.GetTempPath(), "NotWin-Setup.log");
void Log(string line)
{
    log.Add(line);
    try { File.AppendAllText(logFile, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); } catch (Exception) { }
}

try
{
    if (uninstall)
    {
        if (!quiet && !Ui.Ask("Uninstall NotWin?\n\nYour Windows taskbar comes back. Your settings in %AppData%\\NotWin are kept.")) return 1;
        Installer.Uninstall(Log);
        if (!quiet) Ui.Info("NotWin is uninstalled.");
        return 0;
    }

    var payload = Payload.Find(self);
    if (payload is null)
    {
        // Logged too (user 2026-11-27: this message left no trace). A cut-short download, or a build file without the
        // app inside (out\setup\bin\NotWin.Setup.exe, the uninstaller) run by mistake.
        Log($"no app inside {Path.GetFileName(self)} ({new FileInfo(self).Length / (1024 * 1024)} MB)");
        if (!quiet) Ui.Error("This file doesn't have NotWin inside it, so it can't install.\n\n" +
                             "Download NotWin-Setup.exe again from github.com/imsky1812/NotWin/releases (about 115 MB) " +
                             "and run that file. A smaller file means the download was cut short.");
        return 2;
    }
    if (!quiet && !Ui.Ask("Install NotWin?\n\nNotWin replaces the Windows taskbar with a Nothing-style dock, launcher, notch and desktop widgets. " +
                          "It installs into Program Files and starts with Windows. You can uninstall it any time in Settings > Apps.")) return 1;
    Log($"install {Installer.Version}");
    Installer.Install(self, payload.Value, Log);
    if (!quiet) Ui.Info("NotWin is installed and running.\n\nRight-click the start button on the dock and choose Settings. To show your Google Calendar, open Settings > Google Calendar.");
    return 0;
}
catch (Exception e)
{
    Log($"failed: {e.GetType().Name}: {e.Message}");
    if (!quiet) Ui.Error($"Setup could not finish: {e.Message}\n\nDetails: {logFile}");
    return 3;
}

internal static partial class Ui
{
    private const uint MB_OKCANCEL = 0x1, MB_ICONINFORMATION = 0x40, MB_ICONERROR = 0x10, MB_ICONQUESTION = 0x20, IDOK = 1;
    private const string Title = "NotWin Setup";

    public static bool Ask(string text) => MessageBoxW(IntPtr.Zero, text, Title, MB_OKCANCEL | MB_ICONQUESTION) == IDOK;
    public static void Info(string text) => MessageBoxW(IntPtr.Zero, text, Title, MB_ICONINFORMATION);
    public static void Error(string text) => MessageBoxW(IntPtr.Zero, text, Title, MB_ICONERROR);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}

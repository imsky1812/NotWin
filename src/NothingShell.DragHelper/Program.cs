using System.Diagnostics;
using System.Runtime.InteropServices;
using NothingShell.Core;

namespace NothingShell.DragHelper;

/// <summary>
/// <c>NothingShell.DragHelper.exe --drag "C:\path\file" …</c>: drags the files from where the mouse is, at normal level,
/// so they drop into WhatsApp, browser upload boxes (Google Forms, Drive) and Explorer like a drag from Explorer.
/// The shell starts it while the user is already dragging a shelf chip. If the button is no longer down by then, it
/// does nothing: a drag that starts with the button up would drop at once wherever the pointer happens to be.
/// </summary>
internal static class Program
{
    private const int VkLButton = 0x01, VkRButton = 0x02, SmSwapButton = 23;

    [STAThread]
    private static int Main(string[] args)
    {
        var clock = Stopwatch.StartNew();
        if (DragArgs.Parse(args) is not { } parsed) return Done(2, "bad arguments", clock);
        var paths = parsed.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (paths.Length == 0) return Done(3, "no file exists", clock);
        if (!PrimaryButtonDown()) return Done(4, "button already released", clock); // never drop blindly

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // drop positions in real screen pixels on every monitor

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1))); // DROPEFFECT_COPY

        // Only the foreground window can take the mouse (SetCapture), and the drag loop needs it: a helper left in
        // the background never sees the pointer move or the button go up (live report 2026-11-20: nothing dropped).
        // The shell allows the next foreground change before starting us; this tiny off-screen window takes it.
        using var source = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-200, -200), Size = new System.Drawing.Size(1, 1), Opacity = 0.01,
        };
        source.Show();
        var foreground = SetForegroundWindow(source.Handle);
        var result = source.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Link);
        return Done(0, $"dragged {paths.Length} foreground={foreground} result={result}", clock);
    }

    // One line per run in %AppData%\NothingShell\draghelper.log (never file names), kept under 64 KB.
    private static int Done(int code, string what, Stopwatch clock)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell");
            Directory.CreateDirectory(dir);
            var log = Path.Combine(dir, "draghelper.log");
            if (File.Exists(log) && new FileInfo(log).Length > 64 * 1024) File.Delete(log);
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} exit={code} {what} after {clock.ElapsedMilliseconds} ms{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return code;
    }

    // The physical button that acts as the primary one (left, unless the user swapped the buttons).
    private static bool PrimaryButtonDown()
    {
        var vk = GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}

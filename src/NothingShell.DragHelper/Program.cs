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
        if (DragArgs.Parse(args) is not { } parsed) return 2;
        var paths = parsed.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (paths.Length == 0) return 3;
        if (!PrimaryButtonDown()) return 4; // released before we started: never drop blindly

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // drop positions in real screen pixels on every monitor

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1))); // DROPEFFECT_COPY
        using var source = new Control();
        source.CreateControl();
        source.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Link);
        return 0;
    }

    // The physical button that acts as the primary one (left, unless the user swapped the buttons).
    private static bool PrimaryButtonDown()
    {
        var vk = GetSystemMetrics(SmSwapButton) != 0 ? VkRButton : VkLButton;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}

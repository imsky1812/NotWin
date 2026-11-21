using System.Diagnostics;
using System.Runtime.InteropServices;
using NothingShell.Core;

namespace NothingShell.DragHelper;

/// <summary>
/// <c>NothingShell.DragHelper.exe --drag "C:\path\file" …</c>: drags the files from where the mouse is, at normal level,
/// so they drop into WhatsApp, browser upload boxes (Google Forms, Drive) and Explorer like a drag from Explorer.
/// The shell starts it while the user is already dragging a shelf chip.
/// <para>
/// Windows sends a drag's mouse movement to the window where the button went down — the shell's notch — so a drag
/// started here from a background window never sees the pointer move and drops back where it began (live log
/// 2026-11-20). So the helper puts a tiny "grip" window under the pointer, brings it to the front, and re-sends a
/// release and a press of the held button: the press now belongs to the grip, and the drag starts from it exactly
/// as if the user had pressed there. If the button is no longer held, nothing is sent and nothing is dropped.
/// </para>
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

        var outcome = (Code: 5, What: "no press reached the grip");
        var cursor = Cursor.Position;
        using var grip = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(cursor.X - 24, cursor.Y - 24), Size = new System.Drawing.Size(48, 48),
            BackColor = System.Drawing.Color.Black, Opacity = 0.02, // invisible, yet it still takes the press
        };
        var started = false;
        grip.MouseDown += (_, _) =>
        {
            if (started) return;
            started = true;
            var result = grip.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Link);
            outcome = (0, $"dragged {paths.Length} result={result}");
            grip.Close();
        };
        grip.Shown += (_, _) =>
        {
            var foreground = SetForegroundWindow(grip.Handle);
            // Re-check right before re-sending: if the user let go meanwhile, send nothing (a press with no release
            // to follow would leave a drag hanging).
            if (!PrimaryButtonDown())
            {
                outcome = (4, $"button released before the grip was ready foreground={foreground}");
                grip.Close();
                return;
            }
            var sent = Repress();
            outcome = (5, $"no press reached the grip foreground={foreground} sent={sent}");
        };
        var giveUp = new System.Windows.Forms.Timer { Interval = 2000 };
        giveUp.Tick += (_, _) => { giveUp.Stop(); if (!started) grip.Close(); };
        giveUp.Start();
        Application.Run(grip);
        return Done(outcome.Code, outcome.What, clock);
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
    private static bool Swapped => GetSystemMetrics(SmSwapButton) != 0;

    private static bool PrimaryButtonDown() => (GetAsyncKeyState(Swapped ? VkRButton : VkLButton) & 0x8000) != 0;

    // A release then a press of the primary button where the pointer is (which is now over the grip).
    private static uint Repress()
    {
        const uint leftDown = 0x0002, leftUp = 0x0004, rightDown = 0x0008, rightUp = 0x0010;
        var (up, down) = Swapped ? (rightUp, rightDown) : (leftUp, leftDown);
        var inputs = new[] { MouseInput(up), MouseInput(down) };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input MouseInput(uint flags) => new() { Type = 0 /* INPUT_MOUSE */, Mouse = new MouseData { Flags = flags } };

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int Dx, Dy;
        public uint MouseInfo, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MouseData Mouse; // 8 on 64-bit (the union starts after the padded type field)
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}

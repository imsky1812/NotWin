using System.Runtime.InteropServices;
using NothingShell.Core;

namespace NothingShell.Native;

/// <summary>
/// A live DWM picture of another window (the one the real taskbar shows, minimized windows included) drawn inside one
/// of ours. No pixels are copied. Used by the dock previews and the dot drain (Plan 7).
/// </summary>
public sealed class DwmThumbnail : IDisposable
{
    private const uint RectDestination = 0x1, Opacity = 0x4, Visible = 0x8, SourceClientAreaOnly = 0x10;
    private nint _id;

    private DwmThumbnail(nint id) => _id = id;

    /// <summary>Shows <paramref name="source"/> inside <paramref name="destination"/> (a top-level window of ours); null on failure.</summary>
    public static DwmThumbnail? Register(nint destination, nint source)
    {
        if (destination == 0 || source == 0) return null;
        return DwmRegisterThumbnail(destination, source, out var id) == 0 && id != 0 ? new DwmThumbnail(id) : null;
    }

    /// <summary>The source window's size in pixels as DWM sees it.</summary>
    public (int W, int H) SourceSize => _id != 0 && DwmQueryThumbnailSourceSize(_id, out var s) == 0 ? (s.X, s.Y) : (0, 0);

    /// <summary>Places the picture at <paramref name="destinationClient"/> (px, in the destination's client area).</summary>
    public bool Place(RectI destinationClient, byte opacity = 255)
    {
        if (_id == 0) return false;
        var props = new Properties
        {
            Flags = RectDestination | Opacity | Visible | SourceClientAreaOnly,
            Destination = new Rect { Left = destinationClient.Left, Top = destinationClient.Top, Right = destinationClient.Right, Bottom = destinationClient.Bottom },
            Opacity = opacity, Visible = 1, SourceClientAreaOnly = 0,
        };
        return DwmUpdateThumbnailProperties(_id, ref props) == 0;
    }

    /// <summary>Waits until DWM has composed the next frame.</summary>
    public static void Flush() => DwmFlush();

    /// <summary>Asks Windows 11 to round a window's corners (no effect on older Windows).</summary>
    public static void RoundCorners(nint hwnd)
    {
        var round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
    }

    public void Dispose()
    {
        if (_id == 0) return;
        DwmUnregisterThumbnail(_id);
        _id = 0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Properties
    {
        public uint Flags;
        public Rect Destination;
        public Rect Source;
        public byte Opacity;
        public int Visible;
        public int SourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref Properties props);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out Size size);
}

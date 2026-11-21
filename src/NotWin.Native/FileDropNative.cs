using System.Runtime.InteropServices;
using System.Text;

namespace NotWin.Native;

/// <summary>
/// Classic file drops (WM_DROPFILES) for a window in this uiAccess (High integrity) process. Explorer runs at Medium,
/// and UIPI blocks its OLE drag-and-drop into higher-integrity windows (hands-on ND.9, 2026-11-19: "drop refused").
/// The shell's documented way around it, as elevated apps do: accept WM_DROPFILES, let it (and its helpers
/// WM_COPYDATA / WM_COPYGLOBALDATA) through the message filter, and do not register an OLE drop target on the window,
/// so the drag source falls back to WM_DROPFILES.
/// </summary>
public static class FileDropNative
{
    public const int WmDropFiles = 0x0233;
    private const uint WmCopyData = 0x004A, WmCopyGlobalData = 0x0049, MsgfltAllow = 1;

    /// <summary>Makes <paramref name="hwnd"/> accept dropped files from lower-integrity apps (Explorer).</summary>
    public static void Enable(nint hwnd)
    {
        RevokeDragDrop(hwnd); // no OLE target: the drag falls back to WM_DROPFILES (fails harmlessly if none)
        foreach (var msg in new[] { (uint)WmDropFiles, WmCopyData, WmCopyGlobalData })
            ChangeWindowMessageFilterEx(hwnd, msg, MsgfltAllow, 0);
        DragAcceptFiles(hwnd, true);
    }

    /// <summary>Re-asserts "no OLE drop target" (a control that accepts drops may have registered one).</summary>
    public static void RevokeOle(nint hwnd) => RevokeDragDrop(hwnd);

    /// <summary>The paths in a WM_DROPFILES message (wParam = HDROP); frees the drop handle.</summary>
    public static string[] Read(nint hDrop)
    {
        try
        {
            var count = DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
            var paths = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var len = DragQueryFileW(hDrop, i, null, 0);
                var sb = new StringBuilder((int)len + 1);
                if (DragQueryFileW(hDrop, i, sb, (uint)sb.Capacity) > 0) paths.Add(sb.ToString());
            }
            return [.. paths];
        }
        finally { DragFinish(hDrop); }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint msg, uint action, nint changeInfo);
    [DllImport("shell32.dll")] private static extern void DragAcceptFiles(nint hwnd, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFileW(nint hDrop, uint index, StringBuilder? file, uint size);
    [DllImport("shell32.dll")] private static extern void DragFinish(nint hDrop);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint hwnd);
}

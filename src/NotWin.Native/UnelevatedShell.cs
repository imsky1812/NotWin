using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NotWin.Native;

/// <summary>
/// Starts programs through Explorer's desktop window, so they run at the user's normal (Medium) integrity instead of
/// inheriting this uiAccess process's High-integrity token. Chromium browsers quit at once when started with that token
/// (live bug 2026-11-19: Chrome / Opera GX closed themselves when launched from the dock or launcher), and no app should
/// run with more rights than when started from the desktop. The documented way: ShellWindows → desktop browser →
/// IShellBrowser → active IShellView → its automation object (IShellFolderViewDual) → Application (IShellDispatch2)
/// → ShellExecute, which runs inside Explorer. Call on an STA thread.
/// </summary>
public static class UnelevatedShell
{
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IidDispatch = new("00020400-0000-0000-C000-000000000046");
    private const int CsidlDesktop = 0, SwcDesktop = 8, SwfoNeedDispatch = 1, SvgioBackground = 0;

    /// <summary>Opens <paramref name="file"/> (exe, .lnk, shell: item, URI, document) through Explorer. False on any failure.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "ShellWindows is a COM class created from its CLSID; nothing to trim.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "IDispatch late binding on Explorer's COM objects (IShellFolderViewDual / IShellDispatch2); no managed members involved.")]
    public static bool Open(string file, string? directory, Action<string, Exception?>? onError = null) =>
        Open(file, "", directory, onError);

    /// <summary>
    /// Opens <paramref name="file"/> with <paramref name="args"/> through Explorer (so it runs at normal level, like a
    /// double-click). False on any failure.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "ShellWindows is a COM class created from its CLSID; nothing to trim.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "IDispatch late binding on Explorer's COM objects (IShellFolderViewDual / IShellDispatch2); no managed members involved.")]
    public static bool Open(string file, string args, string? directory, Action<string, Exception?>? onError = null)
    {
        object? windows = null, desktop = null, browser = null, view = null, folderView = null;
        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellWindows, throwOnError: true)!);
            object loc = CsidlDesktop, root = "";
            desktop = ((IShellWindows)windows!).FindWindowSW(ref loc, ref root, SwcDesktop, out _, SwfoNeedDispatch);
            if (desktop is null) { onError?.Invoke("unelevated launch: no desktop window", null); return false; }

            var sid = SidTopLevelBrowser;
            var iid = IidShellBrowser;
            ((IServiceProvider)desktop).QueryService(ref sid, ref iid, out browser);
            ((IShellBrowser)browser).QueryActiveShellView(out var shellView);
            view = shellView;
            var dispIid = IidDispatch;
            ((IShellView)view).GetItemObject(SvgioBackground, ref dispIid, out folderView);

            // IDispatch late binding: folderView.Application (IShellDispatch2).ShellExecute(file, args, dir, verb, show).
            var app = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null)
                      ?? throw new InvalidOperationException("no Shell.Application");
            try
            {
                app.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, app,
                    [file, args, directory ?? "", "open", 1 /* SW_SHOWNORMAL */]);
            }
            finally { if (Marshal.IsComObject(app)) Marshal.ReleaseComObject(app); }
            return true;
        }
        catch (Exception e)
        {
            onError?.Invoke("unelevated launch failed", e);
            return false;
        }
        finally
        {
            foreach (var o in new[] { folderView, view, browser, desktop, windows })
                if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }

    // Only the vtable slots up to the methods used are declared; earlier ones are placeholders in their exact order.

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
        void Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int cookie);
        void RegisterPending(int threadId, ref object loc, ref object locRoot, int swClass, out int cookie);
        void Revoke(int cookie);
        void OnNavigate(int cookie, ref object loc);
        void OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object loc, ref object locRoot, int swClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out nint hwnd);
        void ContextSensitiveHelp(int enter);
        void InsertMenusSB(nint menu, nint widths);
        void SetMenuSB(nint menu, nint olemenu, nint active);
        void RemoveMenusSB(nint menu);
        void SetStatusTextSB(nint text);
        void EnableModelessSB(int enable);
        void TranslateAcceleratorSB(nint msg, ushort id);
        void BrowseObject(nint pidl, uint flags);
        void GetViewStateStream(uint mode, out nint stream);
        void GetControlWindow(uint id, out nint hwnd);
        void SendControlMsg(uint id, uint msg, nint wParam, nint lParam, out nint result);
        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(out nint hwnd);
        void ContextSensitiveHelp(int enter);
        void TranslateAccelerator(nint msg);
        void EnableModeless(int enable);
        void UIActivate(uint state);
        void Refresh();
        void CreateViewWindow(nint previous, nint settings, nint browser, nint rect, out nint hwnd);
        void DestroyViewWindow();
        void GetCurrentInfo(nint settings);
        void AddPropertySheetPages(uint reserved, nint add, nint lParam);
        void SaveViewState();
        void SelectItem(nint pidl, uint flags);
        void GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }
}

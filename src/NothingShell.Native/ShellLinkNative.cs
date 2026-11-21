using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace NothingShell.Native;

/// <summary>What a .lnk points at. <see cref="Aumid"/> is its System.AppUserModel.ID, if it has one.</summary>
public readonly record struct ShortcutInfo(string? Target, string? Aumid);

/// <summary>Shortcut resolution and ShellExecute launching. Call on a COM-initialized (STA) thread.</summary>
public static unsafe class ShellLinkNative
{
    /// <summary>Reads target path and AppUserModel ID from a .lnk without resolving/searching for moved targets. Null on failure.</summary>
    public static ShortcutInfo? Read(string lnkPath)
    {
        void* rawLink = null;
        IShellLinkW* link = null;
        IPersistFile* file = null;
        IPropertyStore* props = null;
        try
        {
            var clsid = new Guid("00021401-0000-0000-C000-000000000046"); // CLSID_ShellLink
            var iid = IShellLinkW.IID_Guid;
            if (PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &rawLink).Failed || rawLink is null)
                return null;
            link = (IShellLinkW*)rawLink;

            var iidFile = IPersistFile.IID_Guid;
            void* rawFile;
            if (link->QueryInterface(&iidFile, &rawFile).Failed || rawFile is null) return null;
            file = (IPersistFile*)rawFile;
            // The generated wrapper returns void and throws on a failed HRESULT (caught below).
            fixed (char* p = lnkPath) file->Load(p, STGM.STGM_READ);

            string? target = null;
            var buf = new char[1024];
            fixed (char* b = buf)
            {
                WIN32_FIND_DATAW fd;
                // Flags 0: the expanded target path (not SLGP_RAWPATH, which keeps environment variables).
                if (link->GetPath(b, buf.Length, &fd, 0).Succeeded)
                {
                    var s = new string(b);
                    if (s.Length > 0) target = s;
                }
            }

            string? aumid = null;
            var iidProps = typeof(IPropertyStore).GUID;
            void* rawProps;
            if (link->QueryInterface(&iidProps, &rawProps).Succeeded && rawProps is not null)
            {
                props = (IPropertyStore*)rawProps;
                aumid = ReadAumid(props);
            }
            return new ShortcutInfo(target, aumid);
        }
        catch { return null; }
        finally
        {
            if (props is not null) props->Release();
            if (file is not null) file->Release();
            if (link is not null) link->Release();
        }
    }

    private static string? ReadAumid(IPropertyStore* store)
    {
        var key = PInvoke.PKEY_AppUserModel_ID;
        PROPVARIANT pv = default; // VT_EMPTY: safe to clear even if GetValue fails
        try
        {
            try { store->GetValue(&key, &pv); }
            catch { return null; }
            PWSTR s;
            if (PInvoke.PropVariantToStringAlloc(&pv, &s).Failed) return null;
            try { var str = s.ToString(); return string.IsNullOrWhiteSpace(str) ? null : str; }
            finally { PInvoke.CoTaskMemFree(s); }
        }
        finally { PInvoke.PropVariantClear(&pv); }
    }

    /// <summary>Diagnostics sink for launches (the native layer has no logger of its own).</summary>
    public static Action<string, Exception?>? OnError { get; set; }

    /// <summary>
    /// Opens a file path, a .lnk, a shell: item or a URI the way the desktop would: through Explorer, so the program
    /// runs at the user's normal integrity rather than inheriting this uiAccess process's High token (Chromium browsers
    /// quit at once under it — live bug 2026-11-19). When Explorer's desktop is not available (Explorer restarting or
    /// killed) nothing is started: a direct start would hand this process's raised token to whatever the click names
    /// (security review 2026-11-21, #3). Returns false (never throws).
    /// </summary>
    public static bool Open(string path)
    {
        LastError = 0;
        // Files and exes start in their own folder (apps that load files relative to the working directory break
        // otherwise); shell: and ms-settings: items have no folder.
        string? dir;
        try { dir = System.IO.Path.IsPathRooted(path) ? System.IO.Path.GetDirectoryName(path) : null; }
        catch (ArgumentException) { dir = null; }
        if (UnelevatedOpener(path, dir)) return true;
        OnError?.Invoke("launch: Explorer unavailable, nothing started (never with this process's raised rights)", null);
        LastError = 1;
        return false;
    }

    /// <summary>How a launch reaches Explorer (tests replace it).</summary>
    public static Func<string, string?, bool> UnelevatedOpener { get; set; } = (path, dir) => UnelevatedShell.Open(path, dir, OnError);



    /// <summary>Win32 error of the last failed <see cref="Open"/> on this thread (0 after a success; -1 for an exception).</summary>
    [ThreadStatic] private static int t_lastError;
    public static int LastError { get => t_lastError; private set => t_lastError = value; }
}

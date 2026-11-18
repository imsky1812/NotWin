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

    /// <summary>
    /// ShellExecuteEx "open" on a file path, a .lnk or a shell: item, without waiting (ASYNCOK) and without error UI.
    /// Returns false (never throws) if the shell rejected the request.
    /// </summary>
    public static bool Open(string path)
    {
        try
        {
            fixed (char* file = path)
            fixed (char* verb = "open")
            {
                var info = new SHELLEXECUTEINFOW
                {
                    cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                    // SEE_MASK_ASYNCOK (0x100000) | SEE_MASK_FLAG_NO_UI (0x400) | SEE_MASK_NOASYNC would block, so not used.
                    fMask = 0x100000 | 0x400,
                    lpVerb = verb,
                    lpFile = file,
                    nShow = 1, // SW_SHOWNORMAL
                };
                return PInvoke.ShellExecuteEx(&info);
            }
        }
        catch { return false; }
    }
}

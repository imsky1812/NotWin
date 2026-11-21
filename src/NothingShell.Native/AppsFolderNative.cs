using NothingShell.Core.Launcher;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace NothingShell.Native;

/// <summary>
/// Enumerates <c>shell:AppsFolder</c>, the same list Start's "All apps" shows: Start Menu shortcuts (all users +
/// this user) and installed packaged apps. Call on an STA thread.
/// </summary>
public static unsafe class AppsFolderNative
{
    /// <summary>Last failure (HRESULT text), for logging; null after a successful call.</summary>
    public static string? LastError { get; private set; }

    public static IReadOnlyList<AppsFolderEntry> Enumerate()
    {
        var result = new List<AppsFolderEntry>();
        IShellItem* folder = null;
        IEnumShellItems* items = null;
        LastError = null;
        try
        {
            var iidItem = IShellItem.IID_Guid;
            void* raw;
            HRESULT hr;
            fixed (char* p = "shell:AppsFolder") hr = PInvoke.SHCreateItemFromParsingName(p, null, &iidItem, &raw);
            if (hr.Failed || raw is null) { LastError = $"SHCreateItemFromParsingName 0x{hr.Value:X8}"; return result; }
            folder = (IShellItem*)raw;

            var bhid = PInvoke.BHID_EnumItems;
            var iidEnum = IEnumShellItems.IID_Guid;
            void* rawEnum;
            folder->BindToHandler(null, &bhid, &iidEnum, &rawEnum);
            if (rawEnum is null) { LastError = "BindToHandler(BHID_EnumItems) returned null"; return result; }
            items = (IEnumShellItems*)rawEnum;

            while (true)
            {
                IShellItem* child = null;
                uint fetched = 0;
                items->Next(1, &child, &fetched); // throws on a failed HRESULT; S_FALSE (end) comes back with fetched == 0
                if (fetched == 0 || child is null) break;
                try
                {
                    var parsing = Name(child, SIGDN.SIGDN_PARENTRELATIVEPARSING);
                    var display = Name(child, SIGDN.SIGDN_NORMALDISPLAY);
                    if (!string.IsNullOrEmpty(parsing) && !string.IsNullOrEmpty(display))
                        result.Add(new AppsFolderEntry(parsing, display));
                }
                finally { child->Release(); }
            }
        }
        catch (Exception e) { LastError = e.Message; }
        finally
        {
            if (items is not null) items->Release();
            if (folder is not null) folder->Release();
        }
        return result;
    }

    private static string? Name(IShellItem* item, SIGDN kind)
    {
        PWSTR s = default;
        try
        {
            item->GetDisplayName(kind, &s);
            return s.Value is null ? null : new string(s.Value);
        }
        catch { return null; }
        finally { if (s.Value is not null) PInvoke.CoTaskMemFree(s.Value); }
    }
}
